using System.Diagnostics;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;

// Every sample sentence through every tone, one request at a time, as a Markdown table (docs/PROMPTS.md), with the
// time each version took on stderr.
//   dotnet run --project tools/ToneCheck -c Release
//   dotnet run --project tools/ToneCheck -c Release -- --model llama3.1:8b --endpoint http://localhost:11434
//   dotnet run --project tools/ToneCheck -c Release -- --engine claude --model claude-sonnet-5-5
//   dotnet run --project tools/ToneCheck -c Release -- --sentence "a sentence a user reported" --tone Ornate
// Every option is optional; the defaults are the app's. --sentence and --tone repeat, and replace the samples and
// the tones. Claude needs ANTHROPIC_API_KEY and bills the account.
Console.OutputEncoding = System.Text.Encoding.UTF8; // redirected to a file, the console would write the OEM code page
var settings = new Settings
{
    Engine = string.Equals(Arg("--engine"), "claude", StringComparison.OrdinalIgnoreCase) ? EngineKind.Claude : EngineKind.Ollama,
};
if (Arg("--model") is { } model)
{
    if (settings.Engine == EngineKind.Claude) settings.ClaudeModel = model;
    else settings.OllamaModel = model;
}
if (Arg("--endpoint") is { } endpoint) settings.OllamaEndpoint = endpoint;

string[] samples =
[
    "acho que a gente precisa conversar sobre o prazo amanhã",
    "você é um idiota!",
    "i dont think we can finish this until friday",
    "can you send me the report until tomorrow?",
    "we gonna need more time to fix this bug",
    "o cliente reclamou de novo, isso é ridículo",
    "please let me know if you have any doubt.",
    "Me manda o arquivo quando puder",
    "não sei se vai dar tempo de entregar tudo hoje",
    "obrigado pela ajuda, você salvou meu dia",
];
if (Args("--sentence") is { Length: > 0 } given) samples = given;
IReadOnlyList<Tone> tones = Args("--tone") is { Length: > 0 } named
    ? [.. named.Select(t => Enum.Parse<Tone>(t, ignoreCase: true))]
    : Tones.All;

var engine = RewriteEngines.Create(settings);
var state = await engine.CheckAsync(CancellationToken.None);
if (state != EngineState.Ready)
{
    Console.Error.WriteLine($"{EngineStatus.Status(engine.Kind, state, engine.Model)}. {EngineStatus.Hint(engine.Kind, state, engine.Model)}");
    return 1;
}
await engine.PreloadAsync(CancellationToken.None); // the first version is not charged with loading the model

Console.WriteLine($"{engine.Kind}, model `{engine.Model}`, {DateTime.Now:yyyy-MM-dd}.");
Console.WriteLine();
var times = new List<double>();
int drifted = 0, declined = 0;
foreach (string sentence in samples)
{
    Console.WriteLine($"**{sentence}**");
    Console.WriteLine();
    Console.WriteLine("| Tone | Version |");
    Console.WriteLine("|---|---|");
    foreach (var tone in tones)
    {
        var watch = Stopwatch.StartNew();
        string text;
        try
        {
            text = await engine.RewriteAsync(sentence, tone, _ => { }, CancellationToken.None);
        }
        catch (VersionDeclinedException)
        {
            declined++;
            Console.WriteLine($"| {tone} | **(declined)** |");
            continue;
        }
        times.Add(watch.Elapsed.TotalSeconds);
        bool foreign = TextShape.HasForeignLetters(text);
        if (foreign) drifted++;
        Console.WriteLine($"| {tone} | {text.Replace("|", "\\|")}{(foreign ? " **(left English)**" : "")} |");
    }
    Console.WriteLine();
}
times.Sort();
Console.Error.WriteLine(times.Count == 0
    ? $"no version written; {declined} declined"
    : $"{times.Count} versions in {times.Sum():0.0} s: median {times[times.Count / 2]:0.00} s, slowest {times[^1]:0.00} s; " +
      $"{drifted} left English, {declined} declined");
return 0;

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

string[] Args(string name) =>
    [.. args.Select((a, i) => (a, i)).Where(x => x.a == name && x.i + 1 < args.Length).Select(x => args[x.i + 1])];
