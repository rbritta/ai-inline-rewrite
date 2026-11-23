using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public sealed class LocalModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "air-localmodel-" + Guid.NewGuid().ToString("N"));

    private string Models => Path.Combine(_root, "models");

    private string Ollama => Path.Combine(_root, "ollama");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_model_is_the_Qwen_team_s_two_part_GGUF_of_4_7_GB()
    {
        Assert.Equal(2, LocalModel.Parts.Count);
        Assert.Equal(4_683_073_632, LocalModel.DownloadSize);
        Assert.Equal("4.7 GB", LocalModel.DownloadSizeText);
        Assert.All(LocalModel.Parts, p => Assert.Matches("^[0-9a-f]{64}$", p.Sha256));
        Assert.StartsWith("https://huggingface.co/Qwen/", LocalModel.BaseUrl);
    }

    [Fact]
    public void Nothing_on_the_PC_finds_nothing()
        => Assert.Null(LocalModel.Find(Models, Ollama));

    [Fact]
    public void The_own_download_counts_only_when_every_part_is_whole()
    {
        Directory.CreateDirectory(Models);
        Sized(Path.Combine(Models, LocalModel.Parts[0].FileName), LocalModel.Parts[0].Size);
        Assert.Null(LocalModel.Find(Models, null)); // the second part is missing

        Sized(Path.Combine(Models, LocalModel.Parts[1].FileName), LocalModel.Parts[1].Size - 1);
        Assert.Null(LocalModel.Find(Models, null)); // the second part is short

        Sized(Path.Combine(Models, LocalModel.Parts[1].FileName), LocalModel.Parts[1].Size);
        Assert.Equal(Path.Combine(Models, LocalModel.Parts[0].FileName), LocalModel.Find(Models, null));
    }

    [Fact]
    public void Ollama_s_qwen2_5_7b_is_used_as_is()
    {
        string blob = OllamaWith(LocalModel.OllamaDigest, LocalModel.OllamaSize);
        Assert.Equal(blob, LocalModel.Find(Models, Ollama));
    }

    [Fact]
    public void The_own_download_wins_over_Ollama_s_copy()
    {
        OllamaWith(LocalModel.OllamaDigest, LocalModel.OllamaSize);
        Directory.CreateDirectory(Models);
        foreach (var part in LocalModel.Parts) Sized(Path.Combine(Models, part.FileName), part.Size);
        Assert.Equal(Path.Combine(Models, LocalModel.Parts[0].FileName), LocalModel.Find(Models, Ollama));
    }

    [Theory]
    [InlineData("sha256:0000000000000000000000000000000000000000000000000000000000000000", 4_683_073_952)] // another build of qwen2.5:7b
    [InlineData(LocalModel.OllamaDigest, 1_000)]                                                            // a partial blob
    public void Another_or_partial_Ollama_copy_is_not_used(string digest, long size)
    {
        OllamaWith(digest, size);
        Assert.Null(LocalModel.OllamaCopy(Ollama));
    }

    [Fact]
    public void A_broken_Ollama_manifest_is_no_copy()
    {
        string manifest = Path.Combine(Ollama, LocalModel.OllamaManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, "not json");
        Assert.Null(LocalModel.OllamaCopy(Ollama));
        Assert.Null(LocalModel.OllamaCopy(null));
    }

    [Fact]
    public void Ollama_s_folder_is_OLLAMA_MODELS_when_set_else_the_one_in_the_user_profile()
    {
        Assert.Equal(@"E:\Ollama\models", LocalModel.OllamaModelsDirectory(name => name == "OLLAMA_MODELS" ? @" E:\Ollama\models " : null));
        Assert.Equal(@"C:\Users\someone\.ollama\models",
            LocalModel.OllamaModelsDirectory(name => name == "USERPROFILE" ? @"C:\Users\someone" : null));
    }

    // Ollama's layout: the manifest of qwen2.5:7b naming a model layer, and the blob named after its digest.
    private string OllamaWith(string digest, long size)
    {
        string manifest = Path.Combine(Ollama, LocalModel.OllamaManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, $$"""
            {"schemaVersion":2,"layers":[
              {"mediaType":"application/vnd.ollama.image.model","digest":"{{digest}}","size":{{size}}},
              {"mediaType":"application/vnd.ollama.image.template","digest":"sha256:eb44","size":1482}]}
            """);
        string blob = Path.Combine(Ollama, "blobs", digest.Replace(':', '-'));
        Directory.CreateDirectory(Path.GetDirectoryName(blob)!);
        Sized(blob, size);
        return blob;
    }

    // A sparse file of that length: the size is what is checked, never the content.
    private static void Sized(string path, long length)
    {
        using var file = File.Create(path);
        file.SetLength(length);
    }
}
