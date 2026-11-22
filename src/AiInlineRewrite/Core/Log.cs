using System.IO;

namespace AiInlineRewrite.Core;

/// <summary>
/// The diagnostic log, off by default (Settings.DiagnosticLog). It records app events only, never what the user
/// types: callers pass counts, tone names, states and timings, not text. The file rotates at
/// <see cref="MaxBytes"/>: log.txt becomes log.1.txt (replacing the older one), so it never takes more than twice
/// that.
/// </summary>
public static class Log
{
    public const long MaxBytes = 256 * 1024;

    private static readonly object Gate = new();
    private static string? _path;

    public static bool Enabled { get; private set; }

    public static void Configure(string path, bool enabled)
    {
        lock (Gate)
        {
            _path = path;
            Enabled = enabled;
        }
    }

    public static void Write(string evt)
    {
        if (!Enabled || _path is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var info = new FileInfo(_path);
                if (info.Exists && info.Length >= MaxBytes)
                    File.Move(_path, RotatedPath(_path), overwrite: true);
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {evt}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // logging must never break the app
        }
    }

    public static string RotatedPath(string path) =>
        Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".1" + Path.GetExtension(path));
}
