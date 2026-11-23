using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace AiInlineRewrite.Native;

/// <summary>
/// The "Start with Windows" entry. Installed from the Microsoft Store (an MSIX package), it is the package's startup
/// task (<see cref="TaskId"/> in the manifest), which Windows lists under Settings &gt; Apps &gt; Startup; a package
/// cannot write the Run key, its registry writes stay inside the package. From the zip, a value under
/// HKCU\...\Run. Neither needs admin rights.
/// </summary>
public static class StartWithWindows
{
    /// <summary>The StartupTask's TaskId in packaging/AppxManifest.xml.</summary>
    public const string TaskId = "AirStartup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AI Inline Rewrite";
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>Is AIR running from an installed package (the Microsoft Store), not from the zip?</summary>
    public static bool Packaged { get; } = IsPackaged();

    public static async Task<bool> IsOnAsync()
    {
        if (!Packaged)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        var task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>Turns it on or off; returns whether it is on now (Windows may refuse: the user turned it off there).</summary>
    public static async Task<bool> SetAsync(bool on)
    {
        if (!Packaged)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return on;
        }
        var task = await StartupTask.GetAsync(TaskId);
        if (on) return await task.RequestEnableAsync() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        task.Disable();
        return false;
    }

    private static bool IsPackaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
