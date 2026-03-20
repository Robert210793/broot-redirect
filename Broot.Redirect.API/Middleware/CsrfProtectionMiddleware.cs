namespace Broot.Redirect.API.Middleware
{
    public class CsrfProtectionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CsrfProtectionMiddleware> _logger;

        private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "GET",
            "HEAD",
            "OPTIONS"
        };

        public CsrfProtectionMiddleware(
            RequestDelegate next,
            ILogger<CsrfProtectionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);

                return;
            }

            if (SafeMethods.Contains(context.Request.Method))
            {
                await _next(context);

                return;
            }

            var host = context.Request.Host.Host;
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            var referer = context.Request.Headers.Referer.FirstOrDefault();

            if (!string.IsNullOrEmpty(origin))
            {
                if (!ValidateOrigin(origin, host))
                {
                    _logger.LogWarning(
                        "CSRF check failed: Origin '{Origin}' does not match Host '{Host}' for {Method} {Path}",
                        origin,
                        host,
                        context.Request.Method,
                        path);

                    await WriteError(context, "CSRF check failed: origin mismatch");

                    return;
                }
            }
            else if (!string.IsNullOrEmpty(referer))
            {
                if (!ValidateReferer(referer, host))
                {
                    _logger.LogWarning(
                        "CSRF check failed: Referer '{Referer}' does not match Host '{Host}' for {Method} {Path}",
                        referer,
                        host,
                        context.Request.Method,
                        path);

                    await WriteError(context, "CSRF check failed: referer mismatch");

                    return;
                }
            }
            else
            {
                _logger.LogWarning(
                    "CSRF check failed: missing Origin and Referer for {Method} {Path} from {Ip}",
                    context.Request.Method,
                    path,
                    context.Connection.RemoteIpAddress);

                await WriteError(context, "CSRF check failed: missing Origin/Referer");

                return;
            }

            await _next(context);
        }

        private static bool ValidateOrigin(string origin, string host)
        {
            if (origin == "null")
            {
                return false;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            {
                return false;
            }

            return string.Equals(originUri.Host, host, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidateReferer(string referer, string host)
        {
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                return false;
            }

            return string.Equals(refererUri.Host, host, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task WriteError(HttpContext context, string message)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new { error = message });
        }
    }
}