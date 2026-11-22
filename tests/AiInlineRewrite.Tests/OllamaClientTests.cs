using System.Net;
using System.Text.Json;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;
using Xunit;

namespace AiInlineRewrite.Tests;

public class OllamaClientTests
{
    private const string Sentence = "i dont think we can finish this until friday";

    // What /api/generate streams: one JSON object per line, the answer in pieces, then done.
    private static readonly string Stream = string.Join("\n",
        """{"model":"qwen2.5:7b","response":"Corrected: I don't think","done":false}""",
        "",
        """{"model":"qwen2.5:7b","response":" we can finish this by Friday.","done":false}""",
        """{"model":"qwen2.5:7b","response":"","done":true}""",
        """{"model":"qwen2.5:7b","response":" never read","done":false}""");

    private static OllamaClient Client(FakeHttp http, string endpoint = "http://localhost:11434") =>
        new("qwen2.5:7b", endpoint, new HttpClient(http));

    private static string? PromptOf(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("prompt").GetString();
    }

    [Fact]
    public async Task A_version_streams_in_shaped_and_stops_at_done()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, Stream, "application/x-ndjson");
        var partials = new List<string>();

        string text = await Client(http).RewriteAsync(Sentence, Tone.Casual, partials.Add, CancellationToken.None);

        Assert.Equal("I don't think we can finish this by Friday", text); // label gone, no period (the user typed none), I kept upper
        Assert.Equal(["I don't think", "I don't think we can finish this by Friday", "I don't think we can finish this by Friday"], partials);
    }

    [Fact]
    public async Task The_request_carries_the_model_the_prompt_and_the_keep_alive()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, Stream);
        await Client(http, "http://gpu-box:11434/").RewriteAsync(Sentence, Tone.Concise, _ => { }, CancellationToken.None);

        var (method, uri, body) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("http://gpu-box:11434/api/generate", uri.ToString()); // the endpoint is honored, its trailing slash dropped
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("qwen2.5:7b", root.GetProperty("model").GetString());
        Assert.Equal(Prompts.For(Tone.Concise, Sentence), root.GetProperty("prompt").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal(OllamaClient.KeepAlive, root.GetProperty("keep_alive").GetString());
        Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task A_version_that_left_English_is_asked_for_once_more_saying_so()
    {
        // what qwen2.5:7b answered for Ornate on this sentence on 2026-11-22, and then its retry
        const string sentence = "e a 7 opção que usa girias novas, dá?";
        string[] answers =
        [
            """{"response":"and the seventh option, which employs新颖的俚语, does it offer?","done":true}""",
            """{"response":"And the seventh option, employing fresh slang, does it?","done":true}""",
        ];
        int n = 0;
        var http = new FakeHttp(_ => FakeHttp.Response(HttpStatusCode.OK, answers[n++]));

        string text = await Client(http).RewriteAsync(sentence, Tone.Ornate, _ => { }, CancellationToken.None);

        Assert.Equal("and the seventh option, employing fresh slang, does it?", text);
        Assert.Equal([Prompts.For(Tone.Ornate, sentence), Prompts.For(Tone.Ornate, sentence, retry: true)],
            http.Requests.Select(r => PromptOf(r.Body)));
    }

    [Fact]
    public async Task A_second_drift_goes_back_to_the_app_to_hide_with_no_third_request()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, """{"response":"and the seventh option, which employs新颖的俚语","done":true}""");

        string text = await Client(http).RewriteAsync("e a 7 opção que usa girias novas", Tone.Ornate, _ => { }, CancellationToken.None);

        Assert.True(TextShape.HasForeignLetters(text));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"error":"model 'qwen2.5:7b' not found"}""", EngineState.ModelMissing)]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":"out of memory"}""", EngineState.Failing)]
    public async Task An_error_answer_becomes_an_engine_state(HttpStatusCode status, string body, EngineState expected)
    {
        var e = await Assert.ThrowsAsync<EngineException>(() =>
            Client(FakeHttp.Always(status, body)).RewriteAsync(Sentence, Tone.Casual, _ => { }, CancellationToken.None));
        Assert.Equal(expected, e.State);
    }

    [Theory]
    [InlineData("""{"error":"model runner stopped"}""")]  // an error mid-stream
    [InlineData("this is not json")]                       // a broken stream
    public async Task A_bad_stream_is_failing(string line)
    {
        var e = await Assert.ThrowsAsync<EngineException>(() =>
            Client(FakeHttp.Always(HttpStatusCode.OK, line)).RewriteAsync(Sentence, Tone.Casual, _ => { }, CancellationToken.None));
        Assert.Equal(EngineState.Failing, e.State);
    }

    [Fact]
    public async Task No_server_means_not_running()
    {
        var e = await Assert.ThrowsAsync<EngineException>(() =>
            Client(FakeHttp.Unreachable()).RewriteAsync(Sentence, Tone.Casual, _ => { }, CancellationToken.None));
        Assert.Equal(EngineState.NotRunning, e.State);
        Assert.Equal(EngineState.NotRunning, await Client(FakeHttp.Unreachable()).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_is_not_an_engine_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(FakeHttp.Always(HttpStatusCode.OK, Stream)).RewriteAsync(Sentence, Tone.Casual, _ => { }, cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"models":[{"name":"qwen2.5:7b","model":"qwen2.5:7b"}]}""", EngineState.Ready)]
    [InlineData(HttpStatusCode.OK, """{"models":[{"name":"llama3.1:8b"}]}""", EngineState.ModelMissing)]
    [InlineData(HttpStatusCode.InternalServerError, "", EngineState.Failing)]
    public async Task Check_reads_the_model_list(HttpStatusCode status, string body, EngineState expected)
    {
        var http = FakeHttp.Always(status, body);
        Assert.Equal(expected, await Client(http).CheckAsync(CancellationToken.None));
        var (method, uri, _) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/tags", uri.AbsolutePath);
    }

    [Fact]
    public async Task A_cancelled_check_is_unknown()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Assert.Equal(EngineState.Unknown, await Client(FakeHttp.Always(HttpStatusCode.OK, "{}")).CheckAsync(cts.Token));
    }

    [Fact]
    public async Task Preload_loads_the_model_with_one_request_and_never_throws()
    {
        var ok = FakeHttp.Always(HttpStatusCode.OK, Stream);
        await Client(ok).PreloadAsync(CancellationToken.None);
        Assert.Single(ok.Requests);

        await Client(FakeHttp.Unreachable()).PreloadAsync(CancellationToken.None); // swallowed
        await Client(FakeHttp.Always(HttpStatusCode.OK, "not json")).PreloadAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_model_list_comes_from_the_tags()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, """{"models":[{"name":"qwen2.5:7b"},{"name":"gemma4:12b"}]}""");
        Assert.Equal(["qwen2.5:7b", "gemma4:12b"], await Client(http).ListModelsAsync(CancellationToken.None));
        var (method, uri, _) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/tags", uri.AbsolutePath);
    }

    [Fact]
    public async Task No_model_list_when_Ollama_cannot_be_asked()
    {
        Assert.Null(await Client(FakeHttp.Unreachable()).ListModelsAsync(CancellationToken.None));
        Assert.Null(await Client(FakeHttp.Always(HttpStatusCode.InternalServerError, "")).ListModelsAsync(CancellationToken.None));
        Assert.Null(await Client(FakeHttp.Always(HttpStatusCode.OK, "not json")).ListModelsAsync(CancellationToken.None));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Assert.Null(await Client(FakeHttp.Always(HttpStatusCode.OK, "{}")).ListModelsAsync(cts.Token));
    }

    [Fact]
    public void It_is_the_Ollama_engine()
    {
        var client = Client(FakeHttp.Always(HttpStatusCode.OK, ""));
        Assert.Equal(EngineKind.Ollama, client.Kind);
        Assert.Equal("qwen2.5:7b", client.Model);
    }
}
