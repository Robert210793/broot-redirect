using System.Security.Cryptography;
using System.Text;
using Broot.Redirect.API.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Broot.Redirect.API.Filters
{
    public sealed class QmProApiKeyFilter : IAuthorizationFilter
    {
        public const string HeaderName = "X-Api-Key";

        private readonly byte[]? _expectedKeyHash;
        private readonly ILogger<QmProApiKeyFilter> _logger;

        public QmProApiKeyFilter(
            IOptions<BrootRedirectOptions> options,
            ILogger<QmProApiKeyFilter> logger)
        {
            _logger = logger;

            var configuredKey = options.Value.QmProApiKey;

            _expectedKeyHash = string.IsNullOrWhiteSpace(configuredKey)
                ? null
                : SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        }

        public bool IsConfigured => _expectedKeyHash != null;

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (IsAuthorized(context.HttpContext.Request))
            {
                return;
            }

            _logger.LogWarning(
                "QM Pro API key check failed for {Method} {Path} from {RemoteIp}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path.Value,
                context.HttpContext.Connection.RemoteIpAddress);

            context.Result = new ObjectResult(new
            {
                error = "Ungültiger oder fehlender API-Key",
                code = "UNAUTHORIZED"
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }

        private bool IsAuthorized(HttpRequest request)
        {
            if (_expectedKeyHash == null)
            {
                return false;
            }

            if (!request.Headers.TryGetValue(HeaderName, out var providedValues) || providedValues.Count != 1)
            {
                return false;
            }

            var providedKey = providedValues[0];

            if (string.IsNullOrEmpty(providedKey))
            {
                return false;
            }

            var providedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));

            return CryptographicOperations.FixedTimeEquals(providedKeyHash, _expectedKeyHash);
        }
    }
}
