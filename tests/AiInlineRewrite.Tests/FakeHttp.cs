using System.Net;
using System.Text;

namespace AiInlineRewrite.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a script instead of the network, and keeps every request it
/// got (with its body read), so the engine tests run with no Ollama, no Anthropic account and no connection.
/// </summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

    public FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;

    public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];

    /// <summary>The headers of each request, in the same order (to see which credential went out).</summary>
    public List<string> Headers { get; } = [];

    /// <summary>The same answer to every request.</summary>
    public static FakeHttp Always(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(_ => Response(status, body, mediaType));

    /// <summary>Fails every request the way a refused connection does.</summary>
    public static FakeHttp Unreachable() => new(_ => throw new HttpRequestException(
        HttpRequestError.ConnectionError, "No connection could be made", new System.Net.Sockets.SocketException(10061)));

    public static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!, body));
        Headers.Add(request.Headers.ToString());
        return _answer(request);
    }
}
