using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Controllers;
using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Net;
using Xunit;

namespace Broot.Redirect.Tests.API.Controllers
{
    public class AuthControllerTests : IDisposable
    {
        private readonly BruteForceProtectionService _bruteForce;
        private readonly AuthController _sut;
        private readonly DefaultHttpContext _httpContext;

        public AuthControllerTests()
        {
            var options = Options.Create(new BrootRedirectOptions
            {
                AdminPassword = "test123",
                LoginMaxAttempts = 3,
                LoginBlockDurationMinutes = 1
            });

            var bruteForceLogger = Substitute.For<ILogger<BruteForceProtectionService>>();
            _bruteForce = new BruteForceProtectionService(options, bruteForceLogger);

            var controllerLogger = Substitute.For<ILogger<AuthController>>();
            _sut = new AuthController(options, _bruteForce, controllerLogger);

            _httpContext = new DefaultHttpContext();
            _httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
            _httpContext.InstallMockSession();

            _sut.ControllerContext = new ControllerContext
            {
                HttpContext = _httpContext
            };
        }

        public void Dispose()
        {
            _bruteForce.Dispose();
        }

        [Fact]
        public async Task Login_CorrectPassword_ReturnsOk()
        {
            var request = new LoginRequest { Password = "test123" };

            var result = await _sut.Login(request) as OkObjectResult;

            result.Should().NotBeNull();
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsUnauthorized()
        {
            var request = new LoginRequest { Password = "wrong" };

            var result = await _sut.Login(request);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Login_BlockedIp_Returns429()
        {
            _bruteForce.BlockIp("127.0.0.1");

            var request = new LoginRequest { Password = "test123" };

            var result = await _sut.Login(request) as ObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(429);
        }

        [Fact]
        public void Status_Authenticated_ReturnsTrue()
        {
            _httpContext.Session.SetString(SessionKeys.IsAdminAuthenticated, "true");
            _httpContext.Session.SetString(SessionKeys.AdminLoginTime, "1234567890");

            var result = _sut.Status() as OkObjectResult;

            result.Should().NotBeNull();

            var response = result!.Value as AuthStatusResponse;

            response.Should().NotBeNull();
            response!.IsAuthenticated.Should().BeTrue();
            response.LoginTime.Should().Be(1234567890);
        }

        [Fact]
        public void Status_NotAuthenticated_ReturnsFalse()
        {
            var result = _sut.Status() as OkObjectResult;

            result.Should().NotBeNull();

            var response = result!.Value as AuthStatusResponse;

            response.Should().NotBeNull();
            response!.IsAuthenticated.Should().BeFalse();
        }

        [Fact]
        public void Logout_ClearsSession_ReturnsOk()
        {
            _httpContext.Session.SetString(SessionKeys.IsAdminAuthenticated, "true");

            var result = _sut.Logout() as OkObjectResult;

            result.Should().NotBeNull();
        }

        [Fact]
        public void GetBlockedIps_ReturnsBlockedList()
        {
            _bruteForce.BlockIp("10.0.0.1");

            var result = _sut.GetBlockedIps() as OkObjectResult;

            result.Should().NotBeNull();
        }

        [Fact]
        public void BlockIp_ValidIp_ReturnsOk()
        {
            var request = new BlockIpRequest { Ip = "10.0.0.2" };

            var result = _sut.BlockIp(request) as OkObjectResult;

            result.Should().NotBeNull();
            _bruteForce.IsBlocked("10.0.0.2").Should().BeTrue();
        }

        [Fact]
        public void BlockIp_AlreadyBlocked_ReturnsConflict()
        {
            _bruteForce.BlockIp("10.0.0.3");

            var request = new BlockIpRequest { Ip = "10.0.0.3" };

            var result = _sut.BlockIp(request);

            result.Should().BeOfType<ConflictObjectResult>();
        }

        [Fact]
        public void BlockIp_EmptyIp_ReturnsBadRequest()
        {
            var request = new BlockIpRequest { Ip = "" };

            var result = _sut.BlockIp(request);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public void UnblockIp_ReturnsOk()
        {
            _bruteForce.BlockIp("10.0.0.4");

            var result = _sut.UnblockIp("10.0.0.4") as OkObjectResult;

            result.Should().NotBeNull();
            _bruteForce.IsBlocked("10.0.0.4").Should().BeFalse();
        }

        [Fact]
        public void ClearBlockedIps_ReturnsOk()
        {
            _bruteForce.BlockIp("10.0.0.5");

            var result = _sut.ClearBlockedIps() as OkObjectResult;

            result.Should().NotBeNull();
            _bruteForce.GetBlockedIps().Should().BeEmpty();
        }

        [Fact]
        public void Status_InvalidLoginTime_ReturnsNullLoginTime()
        {
            _httpContext.Session.SetString(SessionKeys.IsAdminAuthenticated, "true");
            _httpContext.Session.SetString(SessionKeys.AdminLoginTime, "not-a-number");

            var result = _sut.Status() as OkObjectResult;

            var response = result!.Value as AuthStatusResponse;

            response!.IsAuthenticated.Should().BeTrue();
            response.LoginTime.Should().BeNull();
        }

        [Fact]
        public void Status_NoLoginTimeSet_ReturnsNullLoginTime()
        {
            var result = _sut.Status() as OkObjectResult;

            var response = result!.Value as AuthStatusResponse;

            response!.LoginTime.Should().BeNull();
        }

        [Fact]
        public void UnblockIp_EmptyIp_ReturnsBadRequest()
        {
            var result = _sut.UnblockIp(" ");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public void UnblockIp_UrlEncodedIp_DecodesCorrectly()
        {
            var encodedIp = Uri.EscapeDataString("192.168.1.1");
            _bruteForce.BlockIp("192.168.1.1");

            var result = _sut.UnblockIp(encodedIp) as OkObjectResult;

            result.Should().NotBeNull();
            _bruteForce.IsBlocked("192.168.1.1").Should().BeFalse();
        }

        [Fact]
        public async Task Login_CorrectPassword_SetsSessionState()
        {
            var request = new LoginRequest { Password = "test123" };

            await _sut.Login(request);

            _httpContext.Session.GetString(SessionKeys.IsAdminAuthenticated).Should().Be("true");
            _httpContext.Session.GetString(SessionKeys.AdminLoginTime).Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Login_WrongPassword_RecordsFailure()
        {
            var request = new LoginRequest { Password = "wrong" };

            await _sut.Login(request);
            await _sut.Login(request);
            await _sut.Login(request);

            _bruteForce.IsBlocked("127.0.0.1").Should().BeTrue();
        }
    }
}
