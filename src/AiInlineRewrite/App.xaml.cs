using System.Windows;
using System.Windows.Threading;
using AiInlineRewrite.Ui;
using Application = System.Windows.Application;

namespace AiInlineRewrite;

/// <summary>
/// Starts one copy of the app. Starting it again while it runs opens the settings of the running copy instead.
/// <c>--render-previews &lt;folder&gt;</c> renders the bubble and the settings window to PNG files without showing
/// anything (docs/ARCHITECTURE.md), then exits. <c>--accept-injected</c> lets synthesized keystrokes drive AIR, for
/// the automated smoke test (tools/smoke/smoke_test.py).
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\AiInlineRewrite.SingleInstance";
    private const string ShowSettingsEvent = @"Local\AiInlineRewrite.ShowSettings";

    private Mutex? _single;
    private EventWaitHandle? _showSettings;
    private RegisteredWaitHandle? _wait;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 2 && e.Args[0] == "--render-previews")
        {
            PreviewRenderer.RenderAll(e.Args[1]);
            Shutdown();
            return;
        }

        _single = new Mutex(initiallyOwned: true, MutexName, out bool first);
        _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
        if (!first)
        {
            _showSettings.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        _controller = new AppController(acceptInjected: e.Args.Contains("--accept-injected"));
        _controller.Start();
        _wait = ThreadPool.RegisterWaitForSingleObject(_showSettings, (_, _) =>
            Dispatcher.BeginInvoke(() => _controller?.OpenSettings()), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    // A bug in a handler must not take the whole app (and its keyboard hook) down; it is logged and the app goes on.
    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Core.Log.Write("unhandled: " + e.Exception.GetType().Name + " at " + e.Exception.TargetSite?.Name);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wait?.Unregister(null);
        _controller?.Dispose();
        _showSettings?.Dispose();
        if (_single is not null)
        {
            try { _single.ReleaseMutex(); }
            catch (ApplicationException) { }
            _single.Dispose();
        }
        base.OnExit(e);
    }
}
