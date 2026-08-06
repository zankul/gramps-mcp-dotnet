using System.Net;

namespace GrampsMcp.Server.Gramps;

/// <summary>
/// Translated failure from the Gramps Web API. <see cref="Message"/> carries the upstream error
/// verbatim where possible (e.g. a GQL 422 parse error) so a model can self-correct.
/// </summary>
public sealed class GrampsApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
