using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Broot.Redirect.API.Configuration;

namespace Broot.Redirect.API.Middleware
{
    public class RateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitMiddleware> _logger;
        private readonly int _globalMax;
        private readonly int _trackingMax;
        private readonly int _adminMax;
        private readonly TimeSpan _window;

        private static readonly ConcurrentDictionary<string, RateLimitEntry> Entries = new();
        private static Timer? _cleanupTimer;

        public RateLimitMiddleware(
            RequestDelegate next,
            IOptions<BrootRedirectOptions> options,
            ILogger<RateLimitMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            _globalMax = options.Value.RateLimitGlobalMax;
            _trackingMax = options.Value.RateLimitTrackingMax;
            _adminMax = options.Value.RateLimitAdminMax;
            _window = TimeSpan.FromSeconds(options.Value.RateLimitWindowSeconds);

            // Initialize cleanup timer once
            _cleanupTimer ??= new Timer(
                CleanupExpiredEntries,
                null,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Only rate-limit API requests
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);

                return;
            }

            var ip = GetClientIp(context);
            var tier = ClassifyRequest(path, context.Request.Method);
            var limit = GetLimit(tier);
            var key = $"{tier}:{ip}";

            var now = DateTimeOffset.UtcNow;

            var entry = Entries.GetOrAdd(key, _ => new RateLimitEntry
            {
                Count = 0,
                WindowStart = now
            });

            lock (entry)
            {
                // Reset window if expired
                if (now - entry.WindowStart >= _window)
                {
                    entry.Count = 0;
                    entry.WindowStart = now;
                }

                entry.Count++;

                var remaining = Math.Max(0, limit - entry.Count);
                var resetTime = entry.WindowStart + _window;

                // Always set rate limit headers
                context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
                context.Response.Headers["X-RateLimit-Reset"] = resetTime.ToString("o");

                if (entry.Count > limit)
                {
                    var retryAfterSeconds = (int)Math.Ceiling((resetTime - now).TotalSeconds);

                    _logger.LogWarning(
                        "Rate limit exceeded for {Ip} on tier {Tier}: {Count}/{Limit}",
                        ip,
                        tier,
                        entry.Count,
                        limit);

                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                    context.Response.ContentType = "application/json";

                    var responseBody = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        error = "Too many requests",
                        retryAfter = retryAfterSeconds
                    });

                    context.Response.WriteAsync(responseBody).GetAwaiter().GetResult();

                    return;
                }
            }

            await _next(context);
        }

        private RateLimitTier ClassifyRequest(string path, string method)
        {
            // Tracking endpoints get their own tier (higher ceiling)
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                if (path.Equals("/api/track", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/api/feedback", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitTier.Tracking;
                }
            }

            // Admin routes: anything under /api/rules, /api/global-rules, /api/stats, /api/settings (PUT)
            if (path.StartsWith("/api/rules", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/global-rules", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/stats", StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitTier.Admin;
            }

            if (path.StartsWith("/api/settings", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitTier.Admin;
            }

            return RateLimitTier.Global;
        }

        private int GetLimit(RateLimitTier tier)
        {
            return tier switch
            {
                RateLimitTier.Tracking => _trackingMax,
                RateLimitTier.Admin => _adminMax,
                _ => _globalMax
            };
        }

        private static string GetClientIp(HttpContext context)
        {
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private static void CleanupExpiredEntries(object? state)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var kvp in Entries)
            {
                // Remove entries whose window expired more than 2 minutes ago
                if (now - kvp.Value.WindowStart > TimeSpan.FromMinutes(2))
                {
                    Entries.TryRemove(kvp.Key, out _);
                }
            }
        }

        private sealed class RateLimitEntry
        {
            public int Count { get; set; }

            public DateTimeOffset WindowStart { get; set; }
        }

        private enum RateLimitTier
        {
            Global,
            Tracking,
            Admin
        }
    }
}