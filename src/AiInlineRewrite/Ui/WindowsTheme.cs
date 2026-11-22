using Microsoft.Win32;

namespace AiInlineRewrite.Ui;

/// <summary>The Windows app mode (Settings > Personalization > Colors), and a signal when it changes.</summary>
public sealed class WindowsTheme : IDisposable
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public WindowsTheme()
    {
        IsDark = Read();
        SystemEvents.UserPreferenceChanged += OnChanged;
    }

    public event Action? Changed;

    public bool IsDark { get; private set; }

    private static bool Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private void OnChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        bool dark = Read();
        if (dark == IsDark) return;
        IsDark = dark;
        Changed?.Invoke();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnChanged;
}
