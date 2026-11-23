using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;
using Xunit;

namespace AiInlineRewrite.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private const string Base = "https://models.test/";

    private static readonly byte[] One = Encoding.ASCII.GetBytes("first part of a made-up model file");
    private static readonly byte[] Two = Encoding.ASCII.GetBytes("and the second part");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "air-download-" + Guid.NewGuid().ToString("N"));

    private static IReadOnlyList<ModelPart> Parts =>
    [
        new("model-00001.gguf", One.Length, Sha(One)),
        new("model-00002.gguf", Two.Length, Sha(Two)),
    ];

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Each_part_is_fetched_checked_and_given_its_name()
    {
        var http = Serving(new() { ["model-00001.gguf"] = One, ["model-00002.gguf"] = Two });
        var reports = new List<DownloadProgress>();

        await Downloader(http).DownloadAsync(new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(One, File.ReadAllBytes(Path.Combine(_dir, "model-00001.gguf")));
        Assert.Equal(Two, File.ReadAllBytes(Path.Combine(_dir, "model-00002.gguf")));
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
        Assert.Equal([Base + "model-00001.gguf", Base + "model-00002.gguf"], http.Requests.Select(r => r.Uri.ToString()));
        Assert.Contains(reports, r => r.Checking);
        Assert.Equal(One.Length + Two.Length, reports[^1].Done);
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public async Task A_part_already_there_is_not_fetched_again()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "model-00001.gguf"), One);
        var http = Serving(new() { ["model-00002.gguf"] = Two });

        await Downloader(http).DownloadAsync(null, CancellationToken.None);

        Assert.Equal([Base + "model-00002.gguf"], http.Requests.Select(r => r.Uri.ToString()));
    }

    [Fact]
    public async Task A_partial_file_resumes_with_a_range_request()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "model-00001.gguf.part"), One[..10]);
        var ranges = new List<string?>();
        var http = new FakeHttp(req =>
        {
            ranges.Add(req.Headers.Range?.ToString());
            var whole = req.RequestUri!.AbsolutePath.EndsWith("00001.gguf") ? One : Two;
            long from = req.Headers.Range?.Ranges.First().From ?? 0;
            return Bytes(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK, whole[(int)from..]);
        });

        await Downloader(http).DownloadAsync(null, CancellationToken.None);

        Assert.Equal(["bytes=10-", null], ranges);
        Assert.Equal(One, File.ReadAllBytes(Path.Combine(_dir, "model-00001.gguf")));
    }

    [Fact]
    public async Task A_server_that_ignores_the_range_starts_the_part_over()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "model-00001.gguf.part"), Encoding.ASCII.GetBytes("stale bytes"));
        var http = Serving(new() { ["model-00001.gguf"] = One, ["model-00002.gguf"] = Two }); // always 200 with the whole file

        await Downloader(http).DownloadAsync(null, CancellationToken.None);

        Assert.Equal(One, File.ReadAllBytes(Path.Combine(_dir, "model-00001.gguf")));
    }

    [Fact]
    public async Task A_part_that_does_not_match_its_SHA_256_is_deleted_and_reported()
    {
        var damaged = (byte[])One.Clone();
        damaged[0] ^= 0xFF;
        var http = Serving(new() { ["model-00001.gguf"] = damaged, ["model-00002.gguf"] = Two });

        var e = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(http).DownloadAsync(null, CancellationToken.None));

        Assert.Contains("damaged", e.Message);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_error_answer_stops_the_download_with_its_status(HttpStatusCode status)
    {
        var http = new FakeHttp(_ => Bytes(status, []));
        var e = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(http).DownloadAsync(null, CancellationToken.None));
        Assert.Contains(((int)status).ToString(), e.Message);
        Assert.Single(http.Requests); // "no" is not tried again
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_busy_answer_is_tried_again(HttpStatusCode status)
    {
        bool first = true;
        var files = new Dictionary<string, byte[]> { ["model-00001.gguf"] = One, ["model-00002.gguf"] = Two };
        var http = new FakeHttp(req =>
        {
            if (first)
            {
                first = false;
                return Bytes(status, []);
            }
            return Bytes(HttpStatusCode.OK, files[req.RequestUri!.Segments[^1]]);
        });

        await Downloader(http).DownloadAsync(null, CancellationToken.None);

        Assert.Equal(3, http.Requests.Count);
        Assert.Equal(One, File.ReadAllBytes(Path.Combine(_dir, "model-00001.gguf")));
    }

    [Fact]
    public async Task A_dropped_connection_is_tried_again_from_where_it_stopped()
    {
        var ranges = new List<string?>();
        var http = new FakeHttp(req =>
        {
            ranges.Add(req.Headers.Range?.ToString());
            if (req.RequestUri!.AbsolutePath.EndsWith("00002.gguf")) return Bytes(HttpStatusCode.OK, Two);
            long from = req.Headers.Range?.Ranges.First().From ?? 0;
            return from == 0 ? Bytes(HttpStatusCode.OK, One[..10]) : Bytes(HttpStatusCode.PartialContent, One[(int)from..]);
        });
        var reports = new List<DownloadProgress>();
        var waits = new List<int>();

        await Downloader(http, wait: waits.Add).DownloadAsync(new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal([null, "bytes=10-", null], ranges);
        Assert.Equal(One, File.ReadAllBytes(Path.Combine(_dir, "model-00001.gguf")));
        Assert.Equal([1], waits);
        Assert.Contains(reports, r => r.Retrying && r.Done == 10);
        Assert.False(reports[^1].Retrying);
    }

    [Fact]
    public async Task A_stalled_connection_is_cut_and_tried_again()
    {
        var http = new FakeHttp(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("00002.gguf")) return Bytes(HttpStatusCode.OK, Two);
            long from = req.Headers.Range?.Ranges.First().From ?? 0;
            if (from > 0) return Bytes(HttpStatusCode.PartialContent, One[(int)from..]);
            // five bytes, then nothing more, ever
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream(One[..5])) };
        });

        await Downloader(http, stall: TimeSpan.FromMilliseconds(200)).DownloadAsync(null, CancellationToken.None);

        Assert.Equal(One, File.ReadAllBytes(Path.Combine(_dir, "model-00001.gguf")));
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Tries_that_bring_nothing_stop_after_the_limit_waiting_longer_each_time()
    {
        var http = FakeHttp.Unreachable();
        var waits = new List<int>();

        var e = await Assert.ThrowsAsync<ModelDownloadException>(
            () => Downloader(http, wait: waits.Add).DownloadAsync(null, CancellationToken.None));

        Assert.True(e.Transient);
        Assert.Equal(ModelDownloader.MaxTries, http.Requests.Count);
        Assert.Equal(Enumerable.Range(2, ModelDownloader.MaxTries - 1), waits);
    }

    [Fact]
    public async Task No_connection_says_so()
    {
        var e = await Assert.ThrowsAsync<ModelDownloadException>(
            () => Downloader(FakeHttp.Unreachable()).DownloadAsync(null, CancellationToken.None));
        Assert.Contains("internet", e.Message);
    }

    [Fact]
    public async Task A_short_answer_keeps_what_came_for_the_next_try()
    {
        var http = new FakeHttp(_ => Bytes(HttpStatusCode.OK, One[..12]));

        await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(http).DownloadAsync(null, CancellationToken.None));

        Assert.Equal(One[..12], File.ReadAllBytes(Path.Combine(_dir, "model-00001.gguf.part")));
    }

    [Fact]
    public async Task Cancelling_stops_it()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var http = Serving(new() { ["model-00001.gguf"] = One, ["model-00002.gguf"] = Two });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(http).DownloadAsync(null, cts.Token));
    }

    private ModelDownloader Downloader(FakeHttp http, TimeSpan? stall = null, Action<int>? wait = null) =>
        new(_dir, Base, Parts, new HttpClient(http), stall, (n, _) =>
        {
            wait?.Invoke(n);
            return Task.CompletedTask;
        });

    private static FakeHttp Serving(Dictionary<string, byte[]> files) =>
        new(req => files.TryGetValue(req.RequestUri!.Segments[^1], out var bytes)
            ? Bytes(HttpStatusCode.OK, bytes)
            : Bytes(HttpStatusCode.NotFound, []));

    private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new HttpResponseMessage(status) { Content = content };
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // A body that sends its first bytes and then goes quiet until the reader gives up.
    private sealed class StallingStream(byte[] first) : Stream
    {
        private bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_sent)
            {
                _sent = true;
                first.CopyTo(buffer);
                return first.Length;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Progress<T> posts to a context; this one reports in place, so the list is complete when the download returns.
    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
