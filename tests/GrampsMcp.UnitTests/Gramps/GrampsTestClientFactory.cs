using GrampsMcp.Server.Configuration;
using GrampsMcp.Server.Gramps;

namespace GrampsMcp.UnitTests.Gramps;

/// <summary>Shared <see cref="GrampsApiClient"/> construction for tests that stub the HTTP pipeline
/// with a <see cref="TestHttpMessageHandler"/>.</summary>
internal static class GrampsTestClientFactory
{
    public static readonly GrampsOptions Options = new()
    {
        ApiUrl = "https://tree.example.com",
        Username = "owner",
        Password = "owner",
        MaxPageSize = 50,
    };

    public static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.ApiUrl) };
        return new GrampsApiClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options), new GrampsWriteGate());
    }
}
