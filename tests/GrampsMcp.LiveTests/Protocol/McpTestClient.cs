using GrampsMcp.LiveTests.LiveStack;
using ModelContextProtocol.Client;

namespace GrampsMcp.LiveTests.Protocol;

/// <summary>
/// Connects a real <see cref="McpClient"/> to the running <c>gramps-mcp</c> container over the wire
/// (streamable HTTP, per <c>MapMcp()</c>'s route — confirmed empirically against the live stack to
/// be mounted at the app root, i.e. <see cref="LiveStackOptions.McpUrl"/> itself, not a subpath),
/// as opposed to <see cref="LiveGrampsFixture.ApiClient"/>/<see cref="LiveGrampsFixture.RawClient"/>,
/// which both talk to Gramps Web directly and never exercise our own MCP tool registration or
/// protocol surface. Every test under <c>Protocol/</c> goes through this so the transport wiring
/// lives in one place.
/// </summary>
internal static class McpTestClient
{
    public static async Task<McpClientConnection> ConnectAsync(LiveGrampsFixture fixture, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(fixture.Options.McpUrl),
        });

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        return new McpClientConnection(client, transport);
    }
}

/// <summary>
/// Owns both the connected <see cref="McpClient"/> and the <see cref="HttpClientTransport"/> that
/// backs it. <c>McpClient.CreateAsync</c> only retains the resulting session (<c>ITransport</c>),
/// not the <see cref="HttpClientTransport"/> wrapper passed in — so disposing just the
/// <see cref="McpClient"/> never disposes the transport's own internal <c>HttpClient</c>/
/// <c>SocketsHttpHandler</c>. Callers <c>await using</c> this instead of the bare
/// <see cref="McpClient"/> so both get disposed deterministically.
/// </summary>
internal sealed class McpClientConnection(McpClient client, HttpClientTransport transport) : IAsyncDisposable
{
    public McpClient Client { get; } = client;

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await transport.DisposeAsync();
    }
}
