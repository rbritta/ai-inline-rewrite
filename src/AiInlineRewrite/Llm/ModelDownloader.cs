using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AiInlineRewrite.Core;

namespace AiInlineRewrite.Llm;

/// <summary>
/// How far the model download is: bytes in, of the total; <see cref="Checking"/> while a part is hashed,
/// <see cref="Retrying"/> while it waits to reconnect after the connection dropped.
/// </summary>
public sealed record DownloadProgress(long Done, long Total, bool Checking = false, bool Retrying = false)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);
}

/// <summary>
/// The download could not finish; the message says why, for the settings window. <see cref="Transient"/>: a dropped,
/// stalled or refused connection, or a server busy for now, which another try may get past.
/// </summary>
public sealed class ModelDownloadException(string message, Exception? inner = null, bool transient = false)
    : Exception(message, inner)
{
    public bool Transient { get; } = transient;
}

/// <summary>
/// Downloads the built-in model's parts into a folder. Each part goes to "name.part" first and resumes from there
/// (an HTTP Range request) after a cancel, a lost connection or a restart; it takes its real name only when its SHA-256
/// matches, so a whole file is always a checked one. A part already there is skipped. A dropped or stalled connection
/// is tried again by itself, waiting longer each time, for as long as tries bring bytes in; after
/// <see cref="MaxTries"/> tries in a row that bring nothing, the download stops with the reason.
/// </summary>
public sealed class ModelDownloader
{
    /// <summary>Tries in a row with no byte in before giving up.</summary>
    internal const int MaxTries = 8;

    private const int BufferSize = 1 << 20;
    private const long ReportEvery = 8L << 20;

    private static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly string _baseUrl;
    private readonly IReadOnlyList<ModelPart> _parts;
    private readonly TimeSpan _stall;
    private readonly Func<int, CancellationToken, Task> _wait;

    public ModelDownloader(string directory)
        : this(directory, LocalModel.BaseUrl, LocalModel.Parts, Shared, TimeSpan.FromSeconds(60), Backoff)
    {
    }

    /// <summary>
    /// With the caller's parts and <see cref="HttpClient"/> (the tests pass small parts and a fake handler), how long a
    /// connection may go without a byte, and the wait before try n + 1 (none by default, for the tests).
    /// </summary>
    internal ModelDownloader(string directory, string baseUrl, IReadOnlyList<ModelPart> parts, HttpClient http,
        TimeSpan? stall = null, Func<int, CancellationToken, Task>? wait = null)
    {
        _directory = directory;
        _baseUrl = baseUrl;
        _parts = parts;
        _http = http;
        _stall = stall ?? TimeSpan.FromSeconds(60);
        _wait = wait ?? ((_, _) => Task.CompletedTask);
    }

    public long Total => _parts.Sum(p => p.Size);

    /// <summary>2, 4, 8, 16, 32 seconds, then a minute.</summary>
    private static Task Backoff(int tries, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 1 << Math.Min(tries, 6))), ct);

    /// <exception cref="ModelDownloadException">A part could not be fetched or did not match its SHA-256.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; what came in stays for the next try.</exception>
    public async Task DownloadAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ModelDownloadException("The models folder could not be created", e);
        }

        long done = 0;
        foreach (var part in _parts)
        {
            string final = Path.Combine(_directory, part.FileName);
            if (File.Exists(final) && new FileInfo(final).Length == part.Size)
            {
                done += part.Size;
                progress?.Report(new DownloadProgress(done, Total));
                continue;
            }
            await FetchWithRetriesAsync(part, final + ".part", done, progress, ct);
            progress?.Report(new DownloadProgress(done + part.Size, Total, Checking: true));
            await CheckAsync(part, final + ".part", ct);
            File.Move(final + ".part", final, overwrite: true);
            done += part.Size;
            progress?.Report(new DownloadProgress(done, Total));
        }
    }

    private async Task FetchWithRetriesAsync(ModelPart part, string partial, long before, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        int fruitless = 0;
        while (true)
        {
            long had = Length(partial);
            try
            {
                await FetchAsync(part, partial, before, progress, ct);
                return;
            }
            catch (ModelDownloadException e) when (e.Transient)
            {
                long has = Length(partial);
                fruitless = has > had ? 0 : fruitless + 1;
                Log.Write($"model download: {e.Message} ({part.FileName}; {fruitless} of {MaxTries} tries in a row brought nothing)");
                if (fruitless >= MaxTries) throw;
                progress?.Report(new DownloadProgress(before + has, Total, Retrying: true));
                await _wait(fruitless + 1, ct);
            }
        }
    }

    private static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private async Task FetchAsync(ModelPart part, string partial, long before, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        long have = Length(partial);
        if (have > part.Size)
        {
            File.Delete(partial);
            have = 0;
        }
        if (have == part.Size) return;

        using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + part.FileName);
        if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);

        // cut a connection that goes quiet: the answer must start, and each read must bring bytes, within _stall
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(_stall);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        }
        catch (HttpRequestException e)
        {
            throw new ModelDownloadException("Hugging Face could not be reached; check the internet connection", e, transient: true);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new ModelDownloadException("Hugging Face did not answer; check the internet connection", e, transient: true);
        }

        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.OK) have = 0; // the server sent the whole file: start over
            else if (resp.StatusCode != HttpStatusCode.PartialContent)
                throw new ModelDownloadException($"Hugging Face answered {(int)resp.StatusCode}", transient: Busy(resp.StatusCode));

            try
            {
                await using var source = await resp.Content.ReadAsStreamAsync(stall.Token);
                await using var file = new FileStream(partial, have == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write,
                    FileShare.None, BufferSize, useAsync: true);
                var buffer = new byte[BufferSize];
                long sinceReport = ReportEvery; // the first bytes report at once, which also clears "trying again"
                int read;
                stall.CancelAfter(_stall);
                while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(_stall);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    have += read;
                    sinceReport += read;
                    if (have > part.Size) throw new ModelDownloadException("Hugging Face sent more than the file's size");
                    if (sinceReport >= ReportEvery)
                    {
                        sinceReport = 0;
                        progress?.Report(new DownloadProgress(before + have, Total));
                    }
                }
            }
            catch (Exception e) when (e is IOException or HttpRequestException
                                      || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                throw new ModelDownloadException("The download was interrupted; it resumes from where it stopped", e, transient: true);
            }
            if (have != part.Size)
                throw new ModelDownloadException("The download was interrupted; it resumes from where it stopped", transient: true);
        }
    }

    /// <summary>Answers that say "not now" rather than "no": a timeout, too many requests, a server error.</summary>
    private static bool Busy(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static async Task CheckAsync(ModelPart part, string partial, CancellationToken ct)
    {
        string hash;
        await using (var file = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
        if (hash == part.Sha256) return;
        File.Delete(partial);
        throw new ModelDownloadException("The downloaded file was damaged and was deleted; download it again");
    }
}
