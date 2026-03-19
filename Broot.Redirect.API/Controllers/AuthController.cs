using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly BrootRedirectOptions _options;
        private readonly BruteForceProtectionService _bruteForce;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IOptions<BrootRedirectOptions> options,
            BruteForceProtectionService bruteForce,
            ILogger<AuthController> logger)
        {
            _options = options.Value;
            _bruteForce = bruteForce;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Check if IP is blocked before processing the request
            if (_bruteForce.IsBlocked(ip))
            {
                _logger.LogWarning("Blocked login attempt from {RemoteIp}", ip);

                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "Too many failed login attempts. Try again later."
                });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Password is required" });
            }

            var inputHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Password));
            var storedHash = SHA256.HashData(Encoding.UTF8.GetBytes(_options.AdminPassword));

            if (!CryptographicOperations.FixedTimeEquals(inputHash, storedHash))
            {
                _bruteForce.RecordFailure(ip);

                _logger.LogWarning("Failed login attempt from {RemoteIp}", ip);

                return Unauthorized(new { error = "Wrong password" });
            }

            // Successful login: reset brute force counter
            _bruteForce.ResetAttempts(ip);

            HttpContext.Session.Clear();

            await HttpContext.Session.LoadAsync();

            HttpContext.Session.SetString(SessionKeys.IsAdminAuthenticated, "true");
            HttpContext.Session.SetString(SessionKeys.AdminLoginTime, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

            await HttpContext.Session.CommitAsync();

            _logger.LogInformation("Successful admin login from {RemoteIp}", ip);

            return Ok(new LoginResponse { Success = true });
        }

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