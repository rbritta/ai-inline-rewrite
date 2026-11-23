using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AiInlineRewrite.Core;

/// <summary>One file of the built-in model as Hugging Face publishes it: its name, size in bytes and SHA-256.</summary>
public sealed record ModelPart(string FileName, long Size, string Sha256);

/// <summary>
/// The model the built-in engine runs: Qwen2.5 7B Instruct quantized to 4 bits (Q4_K_M), from the Qwen team's own
/// GGUF repository on Hugging Face (Apache 2.0). They publish it in two parts; llama.cpp loads them from the first.
/// It is the model Ollama serves as <c>qwen2.5:7b</c>, the one the prompts were tuned on, so when Ollama already has
/// that model on this PC its file is used as is and nothing is downloaded.
/// </summary>
public static class LocalModel
{
    public const string Id = "qwen2.5-7b-instruct-q4_k_m";
    public const string Name = "Qwen2.5 7B Instruct";
    public const string License = "Apache 2.0";
    public const string BaseUrl = "https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF/resolve/main/";

    public static readonly IReadOnlyList<ModelPart> Parts =
    [
        new("qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf", 3_993_201_344, "dfce12e3862a5283ccfb88221b48480e58745165de856439950d0f22590580db"),
        new("qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf", 689_872_288, "539cf93f78e887edea1c04e2d7d8cdaca9d01dae9c9025bcb8accbe29df3d72a"),
    ];

    /// <summary>Ollama's <c>qwen2.5:7b</c>: the manifest that names it and the digest and size of its model layer.</summary>
    public const string OllamaManifest = @"manifests\registry.ollama.ai\library\qwen2.5\7b";
    public const string OllamaDigest = "sha256:2bada8a7450677000f678be90653b85d364de7db25eb5ea54136ada5f3933730";
    public const long OllamaSize = 4_683_073_952;

    public static long DownloadSize => Parts.Sum(p => p.Size);

    /// <summary>The download size for people: "4.7 GB".</summary>
    public static string DownloadSizeText => string.Create(CultureInfo.InvariantCulture, $"{DownloadSize / 1e9:0.0} GB");

    /// <summary>%LocalAppData%\AiInlineRewrite\models</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiInlineRewrite", "models");

    /// <summary>Where Ollama keeps its models: OLLAMA_MODELS when set, else %UserProfile%\.ollama\models.</summary>
    public static string OllamaModelsDirectory(Func<string, string?> environment)
    {
        string? set = environment("OLLAMA_MODELS");
        if (!string.IsNullOrWhiteSpace(set)) return set.Trim();
        string home = environment("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".ollama", "models");
    }

    /// <summary>
    /// The file to load: AIR's own download when every part is there (a part gets its final name only after its
    /// SHA-256 matched), else Ollama's copy; null when there is neither.
    /// </summary>
    public static string? Find(string directory, string? ollamaModels) =>
        Downloaded(directory) ? Path.Combine(directory, Parts[0].FileName) : OllamaCopy(ollamaModels);

    /// <summary>Every part of AIR's own download is in <paramref name="directory"/>, whole.</summary>
    public static bool Downloaded(string directory) =>
        Parts.All(p => SizeOf(Path.Combine(directory, p.FileName)) == p.Size);

    /// <summary>
    /// Ollama's file for <c>qwen2.5:7b</c>, when its manifest names the known model layer and the blob is whole. Ollama
    /// checked the digest when it pulled the blob, and named the file after it. Null otherwise. Never throws.
    /// </summary>
    public static string? OllamaCopy(string? modelsDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelsDirectory)) return null;
        try
        {
            string manifest = Path.Combine(modelsDirectory, OllamaManifest);
            if (!File.Exists(manifest)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!doc.RootElement.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array) return null;
            foreach (var layer in layers.EnumerateArray())
            {
                if (layer.ValueKind != JsonValueKind.Object
                    || !layer.TryGetProperty("mediaType", out var type) || type.GetString() != "application/vnd.ollama.image.model"
                    || !layer.TryGetProperty("digest", out var digest) || digest.GetString() != OllamaDigest)
                    continue;
                string blob = Path.Combine(modelsDirectory, "blobs", OllamaDigest.Replace(':', '-'));
                return SizeOf(blob) == OllamaSize ? blob : null;
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static long SizeOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : -1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }
}
