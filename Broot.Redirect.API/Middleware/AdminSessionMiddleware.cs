using Broot.Redirect.API.Configuration;

namespace Broot.Redirect.API.Middleware
{
    public class AdminSessionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AdminSessionMiddleware> _logger;

        public AdminSessionMiddleware(RequestDelegate next, ILogger<AdminSessionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;

            if (!RequiresAuthentication(path, method))
            {
                await _next(context);

                return;
            }

            await context.Session.LoadAsync();

            var isAuthenticated = context.Session.GetString(SessionKeys.IsAdminAuthenticated);

            if (isAuthenticated != "true")
            {
                _logger.LogWarning("Unauthorized access attempt to {Method} {Path}", method, path);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new { error = "Access denied. Please log in to the admin panel." });

                return;
            }

            await _next(context);
        }

        private static bool RequiresAuthentication(string path, string method)
        {
            if (path.StartsWith("/api/rules", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith("/api/global-rules", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith("/api/stats", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith("/api/settings", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase);
            }

            if (path.Equals("/api/auth/status", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith("/api/auth/blocked-ips", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}