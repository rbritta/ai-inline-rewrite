using System.Net;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;
using Anthropic;
using Xunit;

namespace AiInlineRewrite.Tests;

/// <summary>Tests that change process environment variables run alone, after the others.</summary>
[CollectionDefinition(nameof(EnvironmentCollection), DisableParallelization = true)]
public sealed class EnvironmentCollection;

/// <summary>
/// With no key in the environment, the Anthropic SDK falls back to a profile in its config folder, the kind
/// `ant auth login` writes. These tests point the SDK at a made-up config folder (ANTHROPIC_CONFIG_DIR) holding a
/// profile and its token, and check that AIR lets such a user through and that the token is what goes out. No
/// account, no `ant` CLI, no network.
/// </summary>
[Collection(nameof(EnvironmentCollection))]
public sealed class ClaudeProfileTests : IDisposable
{
    private static readonly string[] Variables =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_PROFILE", "ANTHROPIC_CONFIG_DIR", "ANTHROPIC_BASE_URL",
        "ANTHROPIC_FEDERATION_RULE_ID", "ANTHROPIC_IDENTITY_TOKEN", "ANTHROPIC_IDENTITY_TOKEN_FILE",
    ];

    private readonly Dictionary<string, string?> _saved = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "air-anthropic-" + Guid.NewGuid().ToString("N"));

    private const string ModelFound = """{"type":"model","id":"claude-haiku-5-5","display_name":"Claude Haiku 5.5","created_at":"2026-10-01T00:00:00Z"}""";

    public ClaudeProfileTests()
    {
        foreach (var v in Variables) Environment.SetEnvironmentVariable(v, null);
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("ANTHROPIC_CONFIG_DIR", _dir);
    }

    public void Dispose()
    {
        foreach (var (v, value) in _saved) Environment.SetEnvironmentVariable(v, value);
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private void Profile(bool withToken)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "configs"));
        File.WriteAllText(Path.Combine(_dir, "configs", "default.json"), """{ "authentication": { "type": "user_oauth" } }""");
        if (!withToken) return;
        Directory.CreateDirectory(Path.Combine(_dir, "credentials"));
        File.WriteAllText(Path.Combine(_dir, "credentials", "default.json"), """{ "type": "oauth_token", "access_token": "made-up-profile-token" }""");
    }

    // A client with no credential of its own, as the app makes it, so the SDK looks for one.
    private static ClaudeClient AsTheAppMakesIt(FakeHttp http) =>
        new("claude-haiku-5-5", new AnthropicClient { HttpClient = new HttpClient(http), MaxRetries = 0 }, ClaudeClient.ProfileOrFederation);

    [Fact]
    public async Task A_profile_is_used_when_no_key_is_set()
    {
        Profile(withToken: true);
        var http = FakeHttp.Always(HttpStatusCode.OK, ModelFound);

        Assert.Equal(EngineState.Ready, await AsTheAppMakesIt(http).CheckAsync(CancellationToken.None));

        Assert.Single(http.Requests);
        Assert.Contains("Bearer made-up-profile-token", Assert.Single(http.Headers)); // the profile's token went out
    }

    [Fact]
    public async Task No_key_and_no_profile_sends_nothing()
    {
        var http = FakeHttp.Always(HttpStatusCode.OK, ModelFound);
        var claude = AsTheAppMakesIt(http);

        Assert.Equal(EngineState.NoKey, await claude.CheckAsync(CancellationToken.None));
        var e = await Assert.ThrowsAsync<EngineException>(() => claude.RewriteAsync("we gonna go", Tone.Casual, _ => { }, CancellationToken.None));
        Assert.Equal(EngineState.NoKey, e.State);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_profile_without_its_token_is_a_credential_problem_not_a_network_one()
    {
        Profile(withToken: false);
        var http = FakeHttp.Always(HttpStatusCode.OK, ModelFound);

        Assert.Equal(EngineState.KeyRefused, await AsTheAppMakesIt(http).CheckAsync(CancellationToken.None));
        Assert.Empty(http.Requests);
    }
}
