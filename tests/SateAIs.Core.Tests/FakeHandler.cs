using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SateAIs.Core.Tests;

/// <summary>Scripted HTTP responses, with a record of what was sent.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public sealed record Sent(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<Sent> Requests { get; } = [];

    public FakeHandler Enqueue(HttpStatusCode status, string body = "", string? contentType = "application/json")
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
            response.Content.Headers.ContentType = contentType is null ? null : new MediaTypeHeaderValue(contentType);
            return response;
        });
        return this;
    }

    public FakeHandler EnqueueRedirect(string location)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location);
            return response;
        });
        return this;
    }

    public FakeHandler EnqueueThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new Sent(request.Method, request.RequestUri!, headers, body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"no scripted response for {request.Method} {request.RequestUri}");
        }
        return _responses.Dequeue()(request);
    }
}
