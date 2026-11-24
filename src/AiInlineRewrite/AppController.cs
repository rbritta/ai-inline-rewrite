using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AiInlineRewrite.Automation;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;
using AiInlineRewrite.Native;
using AiInlineRewrite.Ui;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AiInlineRewrite;

/// <summary>
/// The wiring: the tray icon, the keyboard hook (the AIR key and pick mode), the pause in typing, the rewrite
/// pipeline (read the field, ask the engine for one version per tone, show them) and applying a version.
/// </summary>
public sealed class AppController : ISettingsHost, IDisposable
{
    private enum Mode
    {
        Idle,
        Typing,
        Writing,
        Ready,
        Applied,
        Error,
        Password,
    }

    private readonly string _dir = SettingsStore.DefaultDirectory;
    private readonly Settings _settings;
    private readonly KeyboardHook _hook = new();
    private readonly MouseHook _mouse = new();
    private readonly PickMode _pick = new();
    private readonly HashSet<int> _swallowed = [];
    private readonly DispatcherTimer _pause = new();
    private readonly DispatcherTimer _pickTimer = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly WindowsTheme _windowsTheme = new();
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    private IRewriteEngine _rewriter;
    private AirKeyDetector _air;

    // The off key (Settings.OffKey): the same rules as the AIR key, so a tap of Shift still types capitals.
    private AirKeyDetector _off;
    private BubbleWindow _bubble = null!;
    private SettingsWindow? _settingsWindow;
    private WinForms.NotifyIcon _tray = null!;
    private WinForms.ToolStripMenuItem _onItem = null!;
    private Drawing.Icon? _iconOn, _iconOff;

    private Mode _mode = Mode.Idle;
    private EngineState _engine = EngineState.Unknown;

    // the built-in model's download: running while _download is set; the error of the last one that failed
    private CancellationTokenSource? _download;
    private DownloadProgress? _downloadProgress;
    private string? _downloadError;

    // the first start: no settings file yet, so the settings open by themselves when the built-in model is missing
    private bool _firstRun;
    private bool _copiedOnly;
    private List<BubbleRow> _rows = [];
    private string _source = "";
    private bool _translated;
    private Tone _appliedTone;
    private string _lastProcessed = "";
    private (string Source, List<BubbleRow> Rows, bool Translated)? _cache;
    private Rect _caret = Rect.Empty;
    private int _generation;
    private CancellationTokenSource? _cts;
    private bool _renderQueued;
    private bool _applying;

    private readonly bool _acceptInjected;

    /// <param name="acceptInjected">
    /// Lets keystrokes synthesized by other programs drive AIR (<c>--accept-injected</c>), for the automated smoke test
    /// (tools/smoke/smoke_test.py). AIR's own paste is always ignored. Off, as in normal use, only real typing counts.
    /// </param>
    public AppController(bool acceptInjected = false)
    {
        _acceptInjected = acceptInjected;
        _settings = SettingsStore.Load(_dir);
        _firstRun = !_settings.Welcomed;
        Log.Configure(Path.Combine(_dir, "log.txt"), _settings.DiagnosticLog);
        RewriteEngines.BuiltIn = () => new Local.BuiltInEngine(() => _download is not null);
        _rewriter = RewriteEngines.Create(_settings);
        _air = new AirKeyDetector(_settings.AirKey);
        _off = new AirKeyDetector(_settings.OffKey);
    }

    public Settings Settings => _settings;

    public Palette Palette => Palette.For(IsDark(_settings.Theme));

    public bool WindowsIsDark => _windowsTheme.IsDark;

    public void Start()
    {
        _bubble = new BubbleWindow();
        _bubble.DragStarted += () =>
        {
            if (_settings.FollowCaret) SetFollow(false, "bubble dragged: follow off");
        };
        _bubble.Dropped += (left, top) =>
        {
            _settings.BubbleLeft = left;
            _settings.BubbleTop = top;
            SaveSoon();
            Render(); // a drag skips renders, so the follow arrow greys out here
        };

        _pause.Interval = TimeSpan.FromMilliseconds(_settings.DebounceMs);
        _pause.Tick += async (_, _) => await OnPauseAsync();
        _pickTimer.Tick += (_, _) => OnPickTimer();
        _saveTimer.Tick += (_, _) => SaveNow();
        _windowsTheme.Changed += () =>
        {
            if (_settings.Theme != AppTheme.Windows) return;
            Render();
            _settingsWindow?.Rebuild();
        };
        _mouse.Input += () =>
        {
            _air.MouseInput();
            _off.MouseInput(); // Shift+click extends a selection: not a tap
        };

        _hook.Handler = OnKey;
        try
        {
            _hook.Install();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Write("keyboard hook failed: " + e.NativeErrorCode);
        }

        BuildTray();
        Log.Write($"start: version {Version}, engine {_rewriter.Kind}, model {_rewriter.Model}, AIR key {_settings.AirKey}, on {_settings.On}");
        if (_settings.On) Render();
        _ = CheckEngineAsync();
    }

    private static string Version =>
        typeof(AppController).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    // ---------- tray ----------

    private void BuildTray()
    {
        _iconOn = LoadIcon("AiInlineRewrite.app.ico");
        _iconOff = LoadIcon("AiInlineRewrite.tray-off.ico");

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem("Settings", null, (_, _) => OpenSettings()));
        _onItem = new WinForms.ToolStripMenuItem("AIR on", null, (_, _) => SetOn(!_settings.On)) { Checked = _settings.On };
        menu.Items.Add(_onItem);
        var startItem = new WinForms.ToolStripMenuItem("Start with Windows");
        _ = ReadStartAsync(startItem);
        startItem.Click += async (_, _) =>
        {
            try
            {
                startItem.Checked = await StartWithWindows.SetAsync(!startItem.Checked);
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or COMException)
            {
                Log.Write("start with Windows failed: " + e.GetType().Name);
            }
        };
        menu.Items.Add(startItem);
        menu.Items.Add(new WinForms.ToolStripMenuItem("Report a version", null, (_, _) => OpenReport())
        {
            ToolTipText = "Tell us about a version that was offensive, harmful or otherwise wrong (opens GitHub)",
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown()));

        _tray = new WinForms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ToggleSettings(fromBubble: false);
        };
        PaintTray();

        if (!_settings.Welcomed)
        {
            _settings.Welcomed = true;
            SaveSoon();
            _tray.BalloonTipTitle = "AI Inline Rewrite is on";
            _tray.BalloonTipText = _rewriter is Local.BuiltInEngine { ModelPath: null }
                ? $"First, AIR needs its model on this PC: download it once in the settings ({LocalModel.DownloadSizeText})."
                : $"Type in any app. At a pause, AIR writes the versions; {AirKeys.Label(_settings.AirKey).ToLowerInvariant()} to pick one. Click this icon for the settings.";
            _tray.ShowBalloonTip(8000);
        }
    }

    /// <summary>Opens the issue form for an inappropriate version in the default browser (Store policy 11.16).</summary>
    private void OpenReport()
    {
        string url = ReportLink.For(Version, _settings.Engine, _settings.ActiveModel());
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write("report link failed: " + e.GetType().Name);
        }
    }

    private static async Task ReadStartAsync(WinForms.ToolStripMenuItem item)
    {
        try { item.Checked = await StartWithWindows.IsOnAsync(); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or COMException)
        {
            Log.Write("start with Windows unreadable: " + e.GetType().Name);
        }
    }

    private static Drawing.Icon? LoadIcon(string name)
    {
        using var stream = typeof(AppController).Assembly.GetManifestResourceStream(name);
        return stream is null ? null : new Drawing.Icon(stream);
    }

    private void PaintTray()
    {
        _tray.Icon = (_settings.On ? _iconOn : _iconOff) ?? Drawing.SystemIcons.Application;
        _tray.Text = $"AI Inline Rewrite (AIR) · {(_settings.On ? "on" : "off")}";
        _onItem.Checked = _settings.On;
    }

    // ---------- the keyboard hook (runs on the UI thread; must return quickly) ----------

    private bool OnKey(KeyEvent e)
    {
        if (e.Own || (e.Injected && !_acceptInjected)) return false; // AIR's own paste, or another program's keystrokes
        if (e.Down)
        {
            // both detectors see every key-down, so each knows when the other's key is held
            bool offFired = _off.KeyDown(e.Vk, e.TimeMs);
            bool airFired = _air.KeyDown(e.Vk, e.TimeMs);
            if (offFired)
            {
                Post(OnOffKey);
                return true; // a plain off key belongs to AIR
            }
            if (_off.IsAirKey(e.Vk))
            {
                if (!_off.IsTap) return true;
                if (_off.Armed) Post(_mouse.Install);
            }
            if (airFired)
            {
                Post(OnAirKey);
                return true; // a plain AIR key belongs to AIR
            }
            if (_air.IsAirKey(e.Vk))
            {
                if (!_air.IsTap) return true;
                if (_air.Armed) Post(_mouse.Install);
            }
            bool chord = ChordHeld();
            if (_pick.Active)
            {
                var outcome = _pick.KeyDown(e.Vk, chord, e.TimeMs);
                if (outcome.Swallow)
                {
                    _swallowed.Add(e.Vk);
                    Post(() => OnPick(outcome));
                    return true;
                }
                if (outcome.Command == PickCommand.Exit) Post(() =>
                {
                    _pickTimer.Stop();
                    Log.Write("pick mode: closed by another key");
                    Render();
                });
            }
            if (_settings.On && Vk.Edits(e.Vk, chord)) Post(OnTyping);
            return false;
        }

        bool offTap = _off.KeyUp(e.Vk, e.TimeMs); // before the AIR key's, so both detectors see every key-up
        if (_air.KeyUp(e.Vk, e.TimeMs))
        {
            Post(() =>
            {
                _mouse.Uninstall();
                OnAirKey();
            });
            return false; // Right Ctrl / Right Shift up still reaches the app
        }
        if (offTap)
        {
            Post(() =>
            {
                _mouse.Uninstall();
                OnOffKey();
            });
            return false; // its key-up still reaches the app
        }
        if (_off.IsAirKey(e.Vk))
        {
            if (!_off.IsTap) return true;
            Post(_mouse.Uninstall);
        }
        if (_air.IsAirKey(e.Vk))
        {
            if (!_air.IsTap) return true;
            Post(_mouse.Uninstall);
        }
        return _swallowed.Remove(e.Vk);
    }

    private static bool ChordHeld() =>
        NativeMethods.GetAsyncKeyState(Vk.Control) < 0 || NativeMethods.GetAsyncKeyState(Vk.Menu) < 0
        || NativeMethods.GetAsyncKeyState(Vk.LWin) < 0 || NativeMethods.GetAsyncKeyState(Vk.RWin) < 0;

    private void Post(Action action) => _ui.BeginInvoke(action);

    private void OnAirKey()
    {
        if (!_settings.On)
        {
            SetOn(true);
            Log.Write("AIR key: turned AIR on");
            return;
        }
        if (_pick.Active)
        {
            _pick.Exit();
            _pickTimer.Stop();
            Log.Write("AIR key again: pick mode closed");
            Render();
            return;
        }
        if (_rows.Count == 0)
        {
            _bubble.Shake();
            return;
        }
        _pick.Enter(_rows.Count, Environment.TickCount64);
        ArmPickTimer();
        Log.Write("pick mode: on");
        Render();
    }

    /// <summary>The off key puts AIR to sleep: the bubble closes and nothing is written until the AIR key wakes it.</summary>
    private void OnOffKey()
    {
        if (!_settings.On) return;
        SetOn(false);
        Log.Write("off key: AIR asleep until the AIR key");
    }

    private void OnPick(PickOutcome o)
    {
        switch (o.Command)
        {
            case PickCommand.Moved:
                ArmPickTimer();
                Render();
                break;
            case PickCommand.Apply:
                _ = ApplyAsync(o.Index, "key");
                break;
            case PickCommand.Dismiss:
                Dismiss();
                break;
        }
    }

    private void ArmPickTimer()
    {
        _pickTimer.Stop();
        _pickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, _pick.RemainingMs(Environment.TickCount64)));
        _pickTimer.Start();
    }

    private void OnPickTimer()
    {
        if (_pick.Expired(Environment.TickCount64))
        {
            _pickTimer.Stop();
            _pick.Exit();
            Log.Write("pick mode: closed after 4 s");
            Render();
        }
        else if (_pick.Active)
        {
            ArmPickTimer();
        }
        else
        {
            _pickTimer.Stop();
        }
    }

    private void Dismiss()
    {
        _cts?.Cancel();
        _pick.Exit();
        _pickTimer.Stop();
        _rows = [];
        _mode = Mode.Idle;
        Log.Write("suggestions closed (Esc)");
        Render();
    }

    // ---------- typing and the pause ----------

    private void OnTyping()
    {
        if (!_settings.On || _applying) return;
        _cts?.Cancel();
        if (_pick.Active)
        {
            _pick.Exit();
            _pickTimer.Stop();
        }
        if (_rows.Count > 0 || _mode != Mode.Typing)
        {
            _rows = [];
            _mode = Mode.Typing;
            RequestRender();
        }
        _pause.Stop();
        _pause.Start();
    }

    private async Task OnPauseAsync()
    {
        _pause.Stop();
        if (!_settings.On) return;

        var snap = await Task.Run(() => FocusedText.TryRead(out var s) ? s : (FocusSnapshot?)null);
        if (snap is null || !_settings.On) return;
        if (snap.Value.IsPassword)
        {
            _rows = [];
            _mode = Mode.Password;
            Render();
            return;
        }
        if (!snap.Value.CaretPx.IsEmpty) _caret = snap.Value.CaretPx;

        string sentence = Sentence.ExtractCurrent(snap.Value.FullText);
        if (!Sentence.IsWorthRewriting(sentence))
        {
            _mode = Mode.Idle;
            Render();
            return;
        }
        if (sentence == _lastProcessed)
        {
            if (_cache is { } c && c.Source == sentence)
            {
                (_source, _rows, _translated) = (c.Source, [.. c.Rows], c.Translated);
                _mode = Mode.Ready;
            }
            else if (_mode == Mode.Typing)
            {
                _mode = Mode.Idle;
            }
            Render();
            return;
        }
        _lastProcessed = sentence;
        await RewriteAsync(sentence);
    }

    private async Task RewriteAsync(string sentence)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        int gen = ++_generation;

        _source = sentence;
        _translated = Lang.LooksPortuguese(sentence);
        var tones = _settings.EnabledTones();
        _rows = [.. tones.Select(t => new BubbleRow(t, "", Done: false))];
        _mode = Mode.Writing;
        if (_pick.Active)
        {
            _pick.Exit();
            _pickTimer.Stop();
        }
        Render();

        var watch = Stopwatch.StartNew();
        await Task.WhenAll(tones.Select((tone, i) => WriteToneAsync(i, tone, sentence, gen, cts.Token)));
        if (gen != _generation || cts.IsCancellationRequested) return;

        bool allFailed = _rows.All(r => r.Error);
        _mode = allFailed ? Mode.Error : Mode.Ready;
        if (!allFailed)
        {
            if (_engine != EngineState.Ready) _settingsWindow?.SyncEngine();
            _engine = EngineState.Ready;
            _cache = (sentence, [.. _rows], _translated);
        }
        Log.Write($"rewrite: {tones.Count} tones in {watch.Elapsed.TotalSeconds:0.0} s, {(_translated ? "translated" : "corrected")}, {_rows.Count(r => r.Error)} failed");
        Render();
    }

    private async Task WriteToneAsync(int i, Tone tone, string sentence, int gen, CancellationToken ct)
    {
        try
        {
            string text = await _rewriter.RewriteAsync(sentence, tone, partial =>
            {
                if (gen != _generation || i >= _rows.Count) return;
                _rows[i] = new BubbleRow(tone, partial, Done: false);
                RequestRender();
            }, ct);
            bool leftEnglish = TextShape.HasForeignLetters(text);
            if (leftEnglish) Log.Write($"rewrite: {tone} left English, not offered");
            // An empty answer (only an echoed label, or the tokens ran out) must not be applied: it would delete the
            // sentence and paste nothing.
            bool empty = string.IsNullOrWhiteSpace(text);
            if (empty) Log.Write($"rewrite: {tone} came back empty, not offered");
            if (gen == _generation && i < _rows.Count)
                _rows[i] = leftEnglish
                    ? new BubbleRow(tone, "(this version left English; keep typing to try again)", Done: true, Error: true)
                    : empty
                        ? new BubbleRow(tone, "(the model gave no answer; keep typing to try again)", Done: true, Error: true)
                        : new BubbleRow(tone, text, Done: true);
        }
        catch (OperationCanceledException)
        {
            // superseded by newer typing
        }
        catch (VersionDeclinedException)
        {
            Log.Write($"rewrite: {tone} declined by the model");
            Row("(the model declined this one; keep typing to try again)");
        }
        catch (EngineException e)
        {
            if (gen == _generation)
            {
                _engine = e.State;
                _settingsWindow?.SyncEngine();
            }
            Row("(" + EngineStatus.Status(_rewriter.Kind, e.State, _rewriter.Model) + ")");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // an engine failure nobody foresaw must not leave this row writing forever
            Log.Write($"rewrite: {tone} failed with {e.GetType().Name}");
            Row("(" + EngineStatus.Status(_rewriter.Kind, EngineState.Failing, _rewriter.Model) + ")");
        }

        void Row(string why)
        {
            if (gen == _generation && i < _rows.Count)
                _rows[i] = new BubbleRow(tone, why, Done: true, Error: true);
            RequestRender();
        }
    }

    private async Task CheckEngineAsync()
    {
        var engine = _rewriter;
        var state = await engine.CheckAsync(CancellationToken.None);
        if (!ReferenceEquals(engine, _rewriter)) return; // switched meanwhile: the new engine's own check reports
        _engine = state;
        Log.Write($"engine {engine.Kind}: {_engine}");
        if (_engine == EngineState.Ready) _ = Task.Run(() => engine.PreloadAsync(CancellationToken.None));
        else if (_mode == Mode.Idle) _mode = Mode.Error;
        Render();
        _settingsWindow?.SyncEngine();
        if (_firstRun && engine.Kind == EngineKind.BuiltIn && state == EngineState.ModelMissing) OpenSettings();
        _firstRun = false;
    }

    // ---------- the built-in model's download ----------

    public DownloadProgress? ModelDownload => _download is null ? null : _downloadProgress;

    public string? ModelDownloadError => _downloadError;

    public void StartModelDownload()
    {
        if (_download is not null) return;
        var cts = new CancellationTokenSource();
        _download = cts;
        _downloadError = null;
        _downloadProgress = new DownloadProgress(0, LocalModel.DownloadSize);
        Log.Write("model download: start");
        _ = RunDownloadAsync(cts);
        if (_settings.Engine == EngineKind.BuiltIn) _ = CheckEngineAsync();
        _settingsWindow?.SyncEngine();
    }

    public void CancelModelDownload() => _download?.Cancel();

    private async Task RunDownloadAsync(CancellationTokenSource cts)
    {
        // created here, on the UI thread, so each report comes back to it
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (_download != cts) return;
            _downloadProgress = p;
            _settingsWindow?.SyncEngine();
        });
        var watch = Stopwatch.StartNew();
        try
        {
            await Task.Run(() => new ModelDownloader(LocalModel.DefaultDirectory).DownloadAsync(progress, cts.Token));
            Log.Write($"model download: done in {watch.Elapsed.TotalMinutes:0.0} min");
        }
        catch (OperationCanceledException)
        {
            Log.Write("model download: cancelled");
        }
        catch (ModelDownloadException e)
        {
            _downloadError = e.Message;
            Log.Write("model download failed: " + e.Message);
        }
        finally
        {
            _download = null;
            _downloadProgress = null;
            cts.Dispose();
            if (_settings.Engine == EngineKind.BuiltIn) _ = CheckEngineAsync();
            _settingsWindow?.SyncEngine();
        }
    }

    // A new engine or model from the settings window, in effect at once: what the old one was writing is dropped, the
    // new one is checked, and the next pause writes with it. Only the selected engine is ever created.
    private void SwitchEngine(string why)
    {
        _cts?.Cancel();
        _generation++;
        _cache = null;
        _lastProcessed = "";
        _rows = [];
        if (_mode is Mode.Writing or Mode.Ready or Mode.Error or Mode.Typing) _mode = Mode.Idle;
        _engine = EngineState.Unknown;
        _rewriter = RewriteEngines.Create(_settings);
        Log.Write($"engine {_rewriter.Kind}, model {_rewriter.Model} ({why})");
        SaveSoon();
        Render();
        _settingsWindow?.SyncEngine();
        _ = CheckEngineAsync();
    }

    // ---------- applying a version ----------

    private async Task ApplyAsync(int index, string via)
    {
        if (index >= _rows.Count || !_rows[index].Done || _rows[index].Error)
        {
            _bubble.Shake();
            return;
        }
        var row = _rows[index];
        string source = _source;
        _applying = true;
        try
        {
            var snap = await Task.Run(() => FocusedText.TryRead(out var s) ? s : (FocusSnapshot?)null);
            int backspaces = snap is { IsPassword: false } ? Sentence.BackspacesToReplace(snap.Value.FullText, source) : -1;
            _pick.Exit();
            _pickTimer.Stop();

            if (backspaces < 0)
            {
                // The field changed or cannot be read: hand the version over through the clipboard.
                try { Clipboard.SetText(row.Text); }
                catch (System.Runtime.InteropServices.ExternalException) { }
                _rows = [];
                _mode = Mode.Applied;
                _appliedTone = row.Tone;
                _copiedOnly = true;
                Log.Write($"apply {row.Tone} ({via}): field changed, copied instead");
                Render();
                return;
            }

            var saved = ClipboardSnapshot.Take();
            try { Clipboard.SetText(row.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { }
            bool pasted = await Task.Run(() => TextInjector.DeleteAndPaste(backspaces));
            if (pasted)
            {
                await Task.Delay(250); // the app reads the clipboard on Ctrl+V; give it time before restoring
                saved.Restore();
            }
            // else Windows refused the keystrokes: the version stays on the clipboard, so Ctrl+V still pastes it

            _lastProcessed = row.Text;
            _cache = null;
            _rows = [];
            _mode = Mode.Applied;
            _appliedTone = row.Tone;
            _copiedOnly = !pasted;
            Log.Write($"apply {row.Tone} ({via}): {backspaces} backspaces, paste {(pasted ? "sent" : "refused, left on the clipboard")}");
            Render();
        }
        finally
        {
            _applying = false;
        }
    }

    // ---------- the bubble ----------

    private void RequestRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _renderQueued = false;
            Render();
        });
    }

    private void Render()
    {
        if (!_settings.On)
        {
            _bubble.Hide();
            return;
        }
        if (_bubble.IsDragging) return;
        var state = new BubbleState
        {
            Layout = _settings.Layout,
            Rows = _rows,
            Source = _source,
            Translated = _translated,
            Status = StatusText(),
            Hint = HintText(),
            Pick = _pick.Active,
            Selected = _pick.Selected,
            PickRemainingMs = _pick.RemainingMs(Environment.TickCount64),
            Follow = _settings.FollowCaret,
        };
        _bubble.Render(state, Palette, new BubbleActions
        {
            RowClick = i => _ = ApplyAsync(i, "click"),
            Gear = () => ToggleSettings(fromBubble: true),
            Follow = () => SetFollow(!_settings.FollowCaret, "follow arrow"),
            BarDown = (_, e) => _bubble.StartDrag(e),
        });
        if (!_bubble.IsVisible) _bubble.Show();
        if (_settings.FollowCaret && !_caret.IsEmpty) _bubble.Anchor(_caret);
        else if (_settings.FollowCaret) PlaceDefault();
        else PlaceSaved(glide: false);
    }

    private string StatusText() => _mode switch
    {
        Mode.Typing => "Waiting for a pause",
        Mode.Writing => "Writing…",
        Mode.Ready when _translated => "Translated from Portuguese",
        Mode.Ready => _rows.All(r => r.Error || WordDiff.Unchanged(_source, r.Text)) ? "Looks right already" : "English, corrected",
        Mode.Applied => _copiedOnly ? $"{_appliedTone} copied · Ctrl+V pastes it" : $"Applied {_appliedTone} · Ctrl+Z undoes it",
        Mode.Password => "Password field",
        Mode.Error => EngineStatus.Status(_rewriter.Kind, _engine, _rewriter.Model),
        _ => EngineStatus.Blocks(_engine) ? EngineStatus.Status(_rewriter.Kind, _engine, _rewriter.Model) : "Ready",
    };

    private string HintText() => _mode switch
    {
        Mode.Applied => _copiedOnly ? "AIR could not replace the sentence here, so it copied the version instead." : "Keep typing; AIR waits for the next pause.",
        Mode.Password => "AIR never reads password fields.",
        Mode.Error => EngineStatus.Hint(_rewriter.Kind, _engine, _rewriter.Model),
        _ when EngineStatus.Blocks(_engine) => EngineStatus.Hint(_rewriter.Kind, _engine, _rewriter.Model),
        _ => "Type in Portuguese or English.",
    };

    private void PlaceSaved(bool glide)
    {
        if (_settings.BubbleLeft is { } l && _settings.BubbleTop is { } t) _bubble.PlaceAt(l, t, glide);
        else PlaceDefault();
    }

    private void PlaceDefault()
    {
        var area = SystemParameters.WorkArea;
        _bubble.PlaceAt(area.Left + area.Width / 2 - 220, area.Top + 80);
    }

    // ---------- settings (ISettingsHost) ----------

    /// <summary>The gear opens the settings beside the bubble; the tray icon, above the tray.</summary>
    private void ToggleSettings(bool fromBubble)
    {
        if (_settingsWindow is not null) _settingsWindow.Close();
        else OpenSettings(fromBubble);
    }

    public void OpenSettings() => OpenSettings(fromBubble: false);

    // An engine waiting for a step (a model to download, Ollama to start, a key) opens them on the Engine page.
    private void OpenSettings(bool fromBubble)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        var page = EngineStatus.Blocks(_engine) ? SettingsPage.Engine : SettingsPage.General;
        _settingsWindow = new SettingsWindow(this, fromBubble && _bubble.IsVisible ? _bubble.Bounds() : null, page);
        if (_iconOn is not null)
            _settingsWindow.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(_iconOn.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void SetOn(bool on)
    {
        if (_settings.On == on) return;
        _settings.On = on;
        if (!on)
        {
            _cts?.Cancel();
            _pause.Stop();
            _pick.Exit();
            _pickTimer.Stop();
            _rows = [];
            _mode = Mode.Idle;
        }
        else
        {
            _lastProcessed = "";
        }
        Log.Write("AIR " + (on ? "on" : "off"));
        PaintTray();
        Render();
        _settingsWindow?.Sync();
        SaveSoon();
    }

    public void SetToneOn(Tone tone, bool on)
    {
        var t = _settings.Tones.First(x => x.Tone == tone);
        t.On = on;
        if (!_settings.Tones.Any(x => x.On)) t.On = true; // at least one version
        SaveSoon();
        _cache = null;
        if (_source.Length > 0 && _mode is Mode.Ready or Mode.Writing) _ = RewriteAsync(_source);
        else Render();
    }

    public void SetOrder(IReadOnlyList<Tone> order)
    {
        var byTone = _settings.Tones.ToDictionary(t => t.Tone);
        _settings.Tones = [.. order.Select(t => byTone[t])];
        var rank = order.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        _rows = [.. _rows.OrderBy(r => rank[r.Tone])];
        if (_cache is { } c) _cache = (c.Source, [.. c.Rows.OrderBy(r => rank[r.Tone])], c.Translated);
        _pick.Exit();
        SaveSoon();
        Render();
    }

    public void SetLayout(BubbleLayout layout)
    {
        _settings.Layout = layout;
        SaveSoon();
        Render();
    }

    public void SetTheme(AppTheme theme)
    {
        _settings.Theme = theme;
        SaveSoon();
        Render();
        _settingsWindow?.Rebuild();
    }

    public void SetAirKey(AirKey key) => SetKeys(AirKeys.ChooseAir(_settings.AirKey, _settings.OffKey, key));

    public void SetOffKey(AirKey key) => SetKeys(AirKeys.ChooseOff(_settings.AirKey, _settings.OffKey, key));

    private void SetKeys((AirKey Air, AirKey Off) keys)
    {
        (_settings.AirKey, _settings.OffKey) = keys;
        _air = new AirKeyDetector(keys.Air);
        _off = new AirKeyDetector(keys.Off);
        _mouse.Uninstall();
        Log.Write($"AIR key: {keys.Air}, off key: {keys.Off}");
        SaveSoon();
    }

    public void SetFollow(bool on) => SetFollow(on, "settings");

    public EngineState EngineState => _engine;

    public void SetEngine(EngineKind engine)
    {
        if (_settings.Engine == engine) return;
        _settings.Engine = engine;
        SwitchEngine("settings: engine");
    }

    public void SetModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model == _settings.ActiveModel()) return;
        if (_settings.Engine == EngineKind.Claude) _settings.ClaudeModel = model;
        else _settings.OllamaModel = model;
        SwitchEngine("settings: model");
    }

    public async Task<IReadOnlyList<string>?> InstalledModelsAsync() =>
        _rewriter is OllamaClient ollama ? await ollama.ListModelsAsync(CancellationToken.None) : null;

    private void SetFollow(bool on, string why)
    {
        if (_settings.FollowCaret == on) return;
        if (!on)
        {
            var at = _bubble.BubbleTopLeft;
            _settings.BubbleLeft = at.X;
            _settings.BubbleTop = at.Y;
        }
        _settings.FollowCaret = on;
        Log.Write($"follow the text cursor: {(on ? "on" : "off")} ({why})");
        SaveSoon();
        _settingsWindow?.Sync();
        if (_bubble.IsDragging) return;
        Render();
        if (on) _ = GlideToCaretAsync();
    }

    private async Task GlideToCaretAsync()
    {
        var snap = await Task.Run(() => FocusedText.TryRead(out var s) ? s : (FocusSnapshot?)null);
        if (snap is { IsPassword: false } s && !s.CaretPx.IsEmpty && _settings.FollowCaret)
        {
            _caret = s.CaretPx;
            _bubble.Anchor(_caret, glide: true);
        }
    }

    private bool IsDark(AppTheme mode) => mode == AppTheme.Dark || (mode == AppTheme.Windows && _windowsTheme.IsDark);

    private void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            SettingsStore.Save(_dir, _settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("settings not saved: " + e.GetType().Name);
        }
    }

    public void Dispose()
    {
        if (_saveTimer.IsEnabled) SaveNow();
        _cts?.Cancel();
        _download?.Cancel();
        _hook.Dispose();
        _mouse.Dispose();
        _windowsTheme.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _iconOn?.Dispose();
        _iconOff?.Dispose();
    }
}
