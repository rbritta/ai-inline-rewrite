using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiInlineRewrite.Core;

/// <summary>
/// Loads and saves <see cref="Settings"/> as JSON, enums by name. A file that cannot be read is kept aside as
/// settings.unreadable.json and the defaults are used, so a bad hand edit never stops the app.
/// </summary>
public static class SettingsStore
{
    public const string FileName = "settings.json";
    public const string UnreadableFileName = "settings.unreadable.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>%AppData%\AiInlineRewrite</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiInlineRewrite");

    public static Settings Load(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new Settings();
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryCopy(path, Path.Combine(directory, UnreadableFileName));
            return new Settings();
        }
    }

    public static Settings Parse(string json) =>
        (JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings()).Normalize();

    public static string Serialize(Settings settings) => JsonSerializer.Serialize(settings, Json);

    /// <summary>Writes through a temporary file, so a crash mid-write never leaves half a file.</summary>
    public static void Save(string directory, Settings settings)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(settings));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryCopy(string from, string to)
    {
        try { File.Copy(from, to, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
