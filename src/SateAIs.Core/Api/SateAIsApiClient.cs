using System.Net;
using System.Text;
using System.Text.Json;

namespace SateAIs.Core.Api;

/// <summary>HTTP client for the SateAIs API.</summary>
public sealed class SateAIsApiClient : IDisposable
{
    public const string DefaultBaseUrl = "https://api.spcsft.com";
    public const string ApiVersionPath = "/api/v1";
    public const string ClientProduct = "sateais-arcgis-addin";

    /// <summary>Sent as User-Agent and X-SateAIs-Client, which is how the API tells clients apart.</summary>
    public static readonly string UserAgent = $"{ClientProduct}/{ClientVersion.Current}";

    private static readonly HttpStatusCode[] Redirects =
    [
        HttpStatusCode.MovedPermanently, HttpStatusCode.Found, HttpStatusCode.SeeOther,
        HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect,
    ];

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public SateAIsApiClient(
        string apiKey,
        string baseUrl = DefaultBaseUrl,
        HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
    {
        var scheme = new Uri(baseUrl).Scheme;
        if (scheme is not ("http" or "https"))
        {
            throw new ApiException(0, null, $"unsupported base URL scheme: {scheme}");
        }
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        // Redirects are followed by hand: the result endpoint answers 302 to S3,
        // which rejects a request that also carries the Bearer token.
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
    }

    public async Task<Job> SubmitAsync(AnalysisRequest request, CancellationToken ct = default)
    {
        request.Validate();
        using var document = await SendAsync(HttpMethod.Post, $"/analyze/{Uri.EscapeDataString(request.AnalysisType)}", request.ToBody(), false, ct);
        return Parsing.JobFromJson(document.RootElement);
    }

    /// <summary>Same body as a submission. No job is created and no credits are spent.</summary>
    public async Task<Preview> PreviewAsync(AnalysisRequest request, CancellationToken ct = default)
    {
        request.Validate();
        using var document = await SendAsync(HttpMethod.Post, $"/analyze/{Uri.EscapeDataString(request.AnalysisType)}/preview", request.ToBody(), false, ct);
        return Parsing.PreviewFromJson(document.RootElement);
    }

    public async Task<Job> GetJobAsync(string jobId, CancellationToken ct = default)
    {
        using var document = await SendAsync(HttpMethod.Get, $"/jobs/{Uri.EscapeDataString(jobId)}", null, false, ct);
        return Parsing.JobFromJson(document.RootElement);
    }

    /// <summary>The caller's most recent jobs.</summary>
    public async Task<IReadOnlyList<Job>> ListJobsAsync(
        IEnumerable<string>? statuses = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (statuses is not null)
        {
            var joined = string.Join(",", statuses);
            if (joined.Length > 0)
            {
                query.Add("status=" + Uri.EscapeDataString(joined));
            }
        }
        if (limit is { } n)
        {
            query.Add("limit=" + n);
        }
        var path = "/jobs" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        using var document = await SendAsync(HttpMethod.Get, path, null, false, ct);
        if (!document.RootElement.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
        {
            throw new ApiException(0, null, "unexpected jobs payload");
        }
        return jobs.EnumerateArray().Select(Parsing.JobFromJson).ToList();
    }

    /// <summary>The GeoJSON of a completed job, as text.</summary>
    public async Task<string> GetJobResultAsync(string jobId, CancellationToken ct = default)
    {
        using var response = await RequestAsync(HttpMethod.Get, $"/jobs/{Uri.EscapeDataString(jobId)}/result.geojson", null, ct);
        // Check the media type before reading: a result that is not GeoJSON
        // comes back as 200 with a large archive body.
        var media = response.Content.Headers.ContentType?.MediaType ?? "";
        if (media.Length > 0 && !(media == "application/json" || media.EndsWith("+json", StringComparison.Ordinal)))
        {
            throw new UnsupportedResultFormatException(media);
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<Account> GetMeAsync(CancellationToken ct = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "/me", null, false, ct);
        return Parsing.AccountFromJson(document.RootElement);
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string>? body,
        bool requireJson,
        CancellationToken ct)
    {
        using var response = await RequestAsync(method, path, body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return JsonDocument.Parse("{}");
        }
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException e)
        {
            throw new ApiException(0, null, $"invalid JSON response: {e.Message}", e);
        }
    }

    /// <summary>Sends one request and returns a successful response, following redirects by hand.</summary>
    private async Task<HttpResponseMessage> RequestAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string>? body,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, _baseUrl + ApiVersionPath + path);
        AddHeaders(request, withAuthorization: true);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            for (var hop = 0; hop < 5 && Redirects.Contains(response.StatusCode) && response.Headers.Location is { } location; hop++)
            {
                var target = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
                var sameHost = string.Equals(target.Host, request.RequestUri!.Host, StringComparison.OrdinalIgnoreCase);
                response.Dispose();
                request.Dispose();
                request = new HttpRequestMessage(HttpMethod.Get, target);
                // The token is for the API only; a presigned URL is its own authorisation.
                AddHeaders(request, withAuthorization: sameHost);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
        }
        catch (HttpRequestException e)
        {
            throw new ApiException(0, null, $"connection error: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(0, null, $"request timed out after {_http.Timeout.TotalSeconds:0}s", e);
        }
        finally
        {
            request.Dispose();
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var reason = response.ReasonPhrase is { Length: > 0 } r ? r : $"HTTP {status}";
            string raw;
            try
            {
                raw = await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                raw = "";
            }
            response.Dispose();
            var (code, message) = ParseError(raw, reason);
            throw ApiException.ForStatus(status, code, message);
        }
        return response;
    }

    private void AddHeaders(HttpRequestMessage request, bool withAuthorization)
    {
        if (withAuthorization)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
        }
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("X-SateAIs-Client", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    /// <summary>The error code and message from the body, in any of the shapes the API uses.</summary>
    internal static (string? Code, string Message) ParseError(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, fallback);
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            var snippet = raw.Length > 200 ? raw[..200] : raw;
            return (null, $"{fallback} (body: {snippet.Trim()})");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, fallback);
            }
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    return (Parsing.OptString(error, "code"), Parsing.OptString(error, "message") ?? fallback);
                }
                if (error.ValueKind == JsonValueKind.String)
                {
                    return (error.GetString(), Parsing.OptString(root, "error_message") ?? Parsing.OptString(root, "message") ?? fallback);
                }
            }
            return (Parsing.OptString(root, "error_code"), Parsing.OptString(root, "error_message") ?? Parsing.OptString(root, "message") ?? fallback);
        }
    }
}
