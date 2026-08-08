namespace GrampsMcp.UnitTests.Gramps;

/// <summary>
/// Fakes the HTTP transport for tests that exercise <see cref="GrampsMcp.Server.Gramps.GrampsApiClient"/>
/// or <see cref="GrampsMcp.Server.Gramps.GrampsTokenStore"/> without a real network call. Records every
/// request (and, since the request's content stream is disposed once the send completes, its body read
/// up front) so tests can assert on what was sent.
/// </summary>
internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    /// <summary>Async overload for responders that need to block — e.g. holding a response open
    /// with a <see cref="TaskCompletionSource"/> until several concurrent callers are in flight.</summary>
    public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Request bodies, index-aligned with <see cref="Requests"/>; null where the request
    /// had no content.</summary>
    public List<string?> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        return await _responder(request, cancellationToken);
    }
}
