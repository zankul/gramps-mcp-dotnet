using GrampsMcp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace GrampsMcp.Server.Http;

/// <summary>
/// Registers the Origin-allowlist check ahead of <c>app.MapMcp()</c>. This is a local single-user
/// tool with no built-in auth scheme (see <c>SECURITY.md</c>); Origin validation is what actually
/// stops a malicious page in a browser from using DNS rebinding to reach the loopback-bound server.
/// An absent <c>Origin</c> header passes through untouched, since most non-browser MCP clients
/// (CLIs, desktop apps) never send one — only a present-but-disallowed value is rejected.
/// </summary>
public static class OriginValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseOriginValidation(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();

            if (!string.IsNullOrEmpty(origin))
            {
                var options = context.RequestServices.GetRequiredService<IOptions<OriginValidationOptions>>().Value;
                if (!options.AllowedOrigins.Contains(origin))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Origin not allowed.");
                    return;
                }
            }

            await next(context);
        });
    }
}
