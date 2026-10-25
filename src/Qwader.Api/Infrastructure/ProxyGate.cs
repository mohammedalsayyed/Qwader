using System.Security.Cryptography;
using System.Text;

namespace Qwader.Api.Infrastructure;

/// <summary>
/// The hosting provider offers no TLS, so the public entry point is the Cloudflare Worker (HTTPS), which forwards
/// /api/* here with a shared secret header. When Proxy:SharedSecret is configured:
/// API calls without the secret are refused (no logins over plain HTTP, no bypassing Cloudflare), and
/// direct page visits are redirected to Proxy:PublicUrl. Without the setting (development, tests) nothing changes.
/// </summary>
public static class ProxyGate
{
    public const string HeaderName = "X-Qwader-Proxy";

    public static IApplicationBuilder UseProxyGate(this IApplicationBuilder app, IConfiguration config)
    {
        var secret = config["Proxy:SharedSecret"];
        if (string.IsNullOrEmpty(secret)) return app;

        var expected = Encoding.UTF8.GetBytes(secret);
        var publicUrl = config["Proxy:PublicUrl"]?.TrimEnd('/');

        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            // Uptime checks carry no data.
            if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var provided = Encoding.UTF8.GetBytes(context.Request.Headers[HeaderName].ToString());
            if (CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                await next();
                return;
            }

            if (path.StartsWithSegments("/api"))
            {
                await ProblemWriter.WriteAsync(context, 403, "يرجى استخدام الرابط الرسمي الآمن للنظام");
                return;
            }

            if (publicUrl is not null && HttpMethods.IsGet(context.Request.Method))
            {
                context.Response.Redirect(publicUrl + path + context.Request.QueryString, permanent: false);
                return;
            }

            context.Response.StatusCode = 403;
        });
    }
}
