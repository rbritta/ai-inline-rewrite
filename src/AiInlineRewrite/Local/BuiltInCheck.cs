using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AiInlineRewrite.Core;

namespace AiInlineRewrite.Local;

/// <summary>
/// <c>--check-builtin &lt;file.md&gt;</c>: runs the built-in engine on a few sample sentences in every tone, without
/// showing anything, and writes the versions and their times as a Markdown table, with which model file it loaded and
/// what llama.cpp logged about the device (GPU or CPU) next to it in a .log file. For checking a PC (or a clean
/// Windows with the packaged app) the way tools/ToneCheck checks Ollama. Exit code 0 when every version came back.
/// </summary>
public static class BuiltInCheck
{
    private static readonly string[] Samples =
    [
        "acho que a gente precisa conversar sobre o prazo amanhã",
        "i dont think we can finish this until friday",
        "Me manda o arquivo quando puder",
        "can we postpone the meeting for next week? i am very busy this days",
    ];

    public static async Task<int> RunAsync(string outFile)
    {
        string log = Path.ChangeExtension(outFile, ".log");
        Log.Configure(log, enabled: true);
        var engine = new BuiltInEngine(() => false);
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"Built-in engine, model `{LocalModel.Id}`, {DateTime.Now:yyyy-MM-dd HH:mm}.");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Model file: `{engine.ModelPath ?? "(none: not downloaded, and no Ollama copy)"}`");
        if (engine.ModelPath is null)
        {
            await File.WriteAllTextAsync(outFile, md.ToString());
            return 2;
        }

        var watch = Stopwatch.StartNew();
        await engine.PreloadAsync(CancellationToken.None);
        md.AppendLine(CultureInfo.InvariantCulture, $"Load and warm-up: {watch.Elapsed.TotalSeconds:0.0} s (the device is in {Path.GetFileName(log)})");

        var times = new List<double>();
        int failed = 0;
        foreach (string sentence in Samples)
        {
            md.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"**{sentence}**").AppendLine();
            md.AppendLine("| Tone | Version | Time |").AppendLine("|---|---|---|");
            foreach (var tone in Tones.All)
            {
                var t = Stopwatch.StartNew();
                string text;
                try
                {
                    text = await engine.RewriteAsync(sentence, tone, _ => { }, CancellationToken.None);
                }
                catch (Llm.EngineException e)
                {
                    failed++;
                    text = $"(failed: {e.State})";
                }
                times.Add(t.Elapsed.TotalSeconds);
                md.AppendLine(CultureInfo.InvariantCulture, $"| {tone} | {text.Replace("|", "\\|")} | {t.Elapsed.TotalSeconds:0.00} s |");
            }
        }
        times.Sort();
        md.AppendLine().AppendLine(CultureInfo.InvariantCulture,
            $"{times.Count} versions: median {times[times.Count / 2]:0.00} s, slowest {times[^1]:0.00} s; {failed} failed.");
        await File.WriteAllTextAsync(outFile, md.ToString());
        return failed == 0 ? 0 : 1;
    }
}
