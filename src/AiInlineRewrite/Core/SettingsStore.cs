using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiInlineRewrite.Core;

/// <summary>
/// Loads and saves <see cref="Settings"/> as JSON, enums and keys by name. A file that cannot be read is kept aside as
/// settings.unreadable.json and the defaults are used, so a bad hand edit never stops the app; a key name that is not
/// known just falls back to a default key (<see cref="Settings.Normalize"/>).
/// </summary>
public static class SettingsStore
{
    public const string FileName = "settings.json";
    public const string UnreadableFileName = "settings.unreadable.json";

    // A file on this PC, never put in a web page: the relaxed encoder writes "Ctrl+Shift+Space" as such, where the
    // default one writes the + as +.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(), new AirKeyConverter() },
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

    /// <summary>
    /// A file with no Engine field was written before there was a choice, when Ollama was the only engine: it keeps
    /// Ollama. Only a new install (no file) starts on the built-in engine.
    /// </summary>
    public static Settings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<Settings>(json, Json) ?? new Settings();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object && !doc.RootElement.TryGetProperty(nameof(Settings.Engine), out _))
                settings.Engine = EngineKind.Ollama;
        }
        return settings.Normalize();
    }

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

    // A key as its name ("RightCtrlTap", "Ctrl+Shift+Space"); a name that is not a key reads as null.
    private sealed class AirKeyConverter : JsonConverter<AirKey>
    {
        public override AirKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String) return AirKeys.Parse(reader.GetString())!;
            reader.Skip(); // a number, an object or an array: not a key
            return null!;
        }

        public override void Write(Utf8JsonWriter writer, AirKey value, JsonSerializerOptions options) =>
            writer.WriteStringValue(AirKeys.Name(value));
    }

    private static void TryCopy(string from, string to)
    {
        try { File.Copy(from, to, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
