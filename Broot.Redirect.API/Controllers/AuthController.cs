using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Dtos;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly BrootRedirectOptions _options;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IOptions<BrootRedirectOptions> options,
            ILogger<AuthController> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// POST /api/auth/login
        /// Authenticates admin with simple password comparison.
        /// Both submitted and stored passwords are SHA256-hashed and compared using
        /// CryptographicOperations.FixedTimeEquals to prevent timing attacks.
        /// On success, sets session cookie (HttpOnly, Secure in production, SameSite=Lax).
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Password is required" });
            }

            var inputHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Password));
            var storedHash = SHA256.HashData(Encoding.UTF8.GetBytes(_options.AdminPassword));

            if (!CryptographicOperations.FixedTimeEquals(inputHash, storedHash))
            {
                _logger.LogWarning("Failed login attempt from {RemoteIp}", HttpContext.Connection.RemoteIpAddress);

                return Unauthorized(new { error = "Wrong password" });
            }

            HttpContext.Session.Clear();

            await HttpContext.Session.LoadAsync();

            HttpContext.Session.SetString(SessionKeys.IsAdminAuthenticated, "true");
            HttpContext.Session.SetString(SessionKeys.AdminLoginTime, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

            await HttpContext.Session.CommitAsync();

            _logger.LogInformation("Successful admin login from {RemoteIp}", HttpContext.Connection.RemoteIpAddress);

            return Ok(new LoginResponse { Success = true });
        }

        /// <summary>
        /// GET /api/auth/status
        /// Returns whether the current session is authenticated.
        /// Requires an active session (enforced by AdminSessionMiddleware).
        /// </summary>
        [HttpGet("status")]
        public IActionResult Status()
        {
            var isAuthenticated = HttpContext.Session.GetString(SessionKeys.IsAdminAuthenticated) == "true";

            long? loginTime = null;
            var loginTimeString = HttpContext.Session.GetString(SessionKeys.AdminLoginTime);

            if (long.TryParse(loginTimeString, out var parsed))
            {
                loginTime = parsed;
            }

            return Ok(new AuthStatusResponse
            {
                IsAuthenticated = isAuthenticated,
                LoginTime = loginTime
            });
        }

        /// <summary>
        /// POST /api/auth/logout
        /// Destroys the current session and clears the session cookie.
        /// </summary>
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();

            HttpContext.Response.Cookies.Delete("admin_session");

            _logger.LogInformation("Admin logout from {RemoteIp}", HttpContext.Connection.RemoteIpAddress);

            return Ok(new { success = true });
        }
    }
}