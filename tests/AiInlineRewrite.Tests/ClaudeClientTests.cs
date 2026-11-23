using System.Net;
using System.Text.Json;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;
using Anthropic;
using Xunit;

namespace AiInlineRewrite.Tests;

/// <summary>
/// The Claude engine against a scripted Anthropic API (server-sent events, error bodies), through the official SDK:
/// no account, no key and no network.
/// </summary>
public class ClaudeClientTests
{
    private const string Sentence = "acho que a gente precisa conversar sobre o prazo amanhã";
    private const string Model = "claude-haiku-5-5";

    private static string Sse(params (string Event, string Data)[] events) =>
        string.Concat(events.Select(e => $"event: {e.Event}\ndata: {e.Data}\n\n"));

    private const string MessageStart =
        """{"type":"message_start","message":{"id":"msg_test","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":120,"output_tokens":1}}}""";

    private static string Text(string text) =>
        """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":""" + JsonSerializer.Serialize(text) + "}}";

    private static string Stop(string reason) =>
        """{"type":"message_delta","delta":{"stop_reason":""" + JsonSerializer.Serialize(reason) + ""","stop_sequence":null},"usage":{"output_tokens":12}}""";

    private static readonly string Answer = Sse(
        ("message_start", MessageStart),
        ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""),
        ("content_block_delta", Text("We need")),
        ("content_block_delta", Text(" to talk about the deadline tomorrow.")),
        ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
        ("message_delta", Stop("end_turn")),
        ("message_stop", """{"type":"message_stop"}"""));

    private static string Error(string type, string message) =>
        """{"type":"error","error":{"type":""" + JsonSerializer.Serialize(type) + ""","message":""" + JsonSerializer.Serialize(message) + "}}";

    // The key is set outright (null for none), so a key in the machine's environment never leaks into a test.
    private static ClaudeClient Client(FakeHttp http, string? key = "sk-ant-test") => new(
        Model,
        new AnthropicClient { ApiKey = key, AuthToken = null, HttpClient = new HttpClient(http), MaxRetries = 0 });

    [Fact]
    public async Task A_version_streams_in_shaped()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, Answer, "text/event-stream");
        var partials = new List<string>();

        string text = await Client(http).RewriteAsync(Sentence, Tone.Casual, partials.Add, CancellationToken.None);

        Assert.Equal("we need to talk about the deadline tomorrow", text); // the user's lowercase start and no period
        Assert.Equal(["we need", "we need to talk about the deadline tomorrow"], partials);
    }

    [Fact]
    public async Task The_request_is_one_streamed_message_with_the_tone_as_the_system_prompt()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, Answer, "text/event-stream");
        await Client(http).RewriteAsync(Sentence, Tone.Polished, _ => { }, CancellationToken.None);

        var (method, uri, body) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/v1/messages", uri.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(Model, root.GetProperty("model").GetString());
        Assert.Equal(ClaudeClient.MaxTokens, root.GetProperty("max_tokens").GetInt32());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal(Prompts.Instructions(Tone.Polished, Sentence), root.GetProperty("system").GetString());
        var message = Assert.Single(root.GetProperty("messages").EnumerateArray().ToList());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal(Prompts.Input(Sentence), message.GetProperty("content").GetString());
        Assert.Equal("low", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.False(root.TryGetProperty("temperature", out _)); // current models take no sampling parameters
        Assert.DoesNotContain("sk-ant-test", body);                // the key travels in a header, never in the body
    }

    [Fact]
    public async Task A_refusal_declines_only_that_version()
    {
        string refused = Sse(("message_start", MessageStart), ("message_delta", Stop("refusal")), ("message_stop", """{"type":"message_stop"}"""));
        await Assert.ThrowsAsync<VersionDeclinedException>(() =>
            Client(FakeHttp.Always(HttpStatusCode.OK, refused, "text/event-stream")).RewriteAsync(Sentence, Tone.Diplomatic, _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent_and_the_bubble_says_how_to_set_one()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, Answer, "text/event-stream");
        var e = await Assert.ThrowsAsync<EngineException>(() =>
            Client(http, key: null).RewriteAsync(Sentence, Tone.Casual, _ => { }, CancellationToken.None));
        Assert.Equal(EngineState.NoKey, e.State);
        Assert.Equal(EngineState.NoKey, await Client(http, key: " ").CheckAsync(CancellationToken.None));
        Assert.Empty(http.Requests); // the sentence never left the machine
        Assert.Contains("ANTHROPIC_API_KEY", EngineStatus.Hint(EngineKind.Claude, e.State, Model));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error", EngineState.KeyRefused)]
    [InlineData(HttpStatusCode.Forbidden, "permission_error", EngineState.KeyRefused)]
    [InlineData(HttpStatusCode.NotFound, "not_found_error", EngineState.ModelMissing)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", EngineState.Failing)]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", EngineState.Failing)]
    [InlineData((HttpStatusCode)529, "overloaded_error", EngineState.Failing)]
    public async Task An_API_error_becomes_an_engine_state(HttpStatusCode status, string type, EngineState expected)
    {
        var http = FakeHttp.Always(status, Error(type, "test"));
        var e = await Assert.ThrowsAsync<EngineException>(() =>
            Client(http).RewriteAsync(Sentence, Tone.Casual, _ => { }, CancellationToken.None));
        Assert.Equal(expected, e.State);
        Assert.Equal(expected, await Client(http).CheckAsync(CancellationToken.None)); // the check agrees
    }

    [Fact]
    public async Task No_connection_means_Claude_cannot_be_reached()
    {
        var e = await Assert.ThrowsAsync<EngineException>(() =>
            Client(FakeHttp.Unreachable()).RewriteAsync(Sentence, Tone.Casual, _ => { }, CancellationToken.None));
        Assert.Equal(EngineState.NotRunning, e.State);
        Assert.Equal("Claude cannot be reached", EngineStatus.Status(EngineKind.Claude, e.State, Model));
        Assert.Equal(EngineState.NotRunning, await Client(FakeHttp.Unreachable()).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_is_not_an_engine_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(FakeHttp.Always(HttpStatusCode.OK, Answer, "text/event-stream")).RewriteAsync(Sentence, Tone.Casual, _ => { }, cts.Token));
    }

    [Fact]
    public async Task Check_looks_the_model_up_without_writing_anything()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK,
            """{"type":"model","id":"claude-haiku-5-5","display_name":"Claude Haiku 5.5","created_at":"2026-10-01T00:00:00Z"}""");

        Assert.Equal(EngineState.Ready, await Client(http).CheckAsync(CancellationToken.None));

        var (method, uri, _) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/v1/models/claude-haiku-5-5", uri.AbsolutePath); // the Models API, which is not billed
    }

    [Fact]
    public async Task Preload_sends_nothing()
    {
        var http = FakeHttp.Unreachable();
        await Client(http).PreloadAsync(CancellationToken.None);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void Claude_in_the_settings_creates_only_the_Claude_engine()
    {
        var claude = RewriteEngines.Create(new Settings { Engine = EngineKind.Claude, ClaudeModel = "claude-sonnet-5-5" });
        Assert.IsType<ClaudeClient>(claude);
        Assert.Equal(EngineKind.Claude, claude.Kind);
        Assert.Equal("claude-sonnet-5-5", claude.Model);

        var ollama = RewriteEngines.Create(new Settings { Engine = EngineKind.Ollama });
        Assert.IsType<OllamaClient>(ollama);
        Assert.Equal("qwen2.5:7b", ollama.Model);
    }
}
