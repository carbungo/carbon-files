using System.Net;
using System.Text;

namespace CarbonFiles.Client.Tests;

/// <summary>
/// A test HttpMessageHandler that returns a preconfigured response.
/// Captures the last request for assertion.
/// </summary>
public class MockHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Content, string ContentType)> _responses = new();
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> RequestBodies { get; } = new();

    public void Enqueue(HttpStatusCode status, string content, string contentType = "application/json")
    {
        _responses.Enqueue((status, content, contentType));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        RequestBodies.Add(request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null);

        if (_responses.Count == 0)
            throw new InvalidOperationException("No more responses queued in MockHandler");

        var (status, content, contentType) = _responses.Dequeue();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, contentType),
            RequestMessage = request
        };
        return response;
    }
}
