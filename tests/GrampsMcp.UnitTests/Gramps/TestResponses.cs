using System.Net;
using System.Text;

namespace GrampsMcp.UnitTests.Gramps;

/// <summary>Small builders for the JSON <see cref="HttpResponseMessage"/> shapes repeated across
/// tool and client tests, so call sites don't each hand-roll the same content/header setup.</summary>
internal static class TestResponses
{
    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A JSON array response carrying Gramps Web's <c>x-total-count</c> header, the shape
    /// every list/search endpoint returns.</summary>
    public static HttpResponseMessage JsonList(string json, int totalCount)
    {
        var response = Json(HttpStatusCode.OK, json);
        response.Headers.Add("x-total-count", totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }
}
