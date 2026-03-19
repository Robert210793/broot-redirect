using System.ComponentModel.DataAnnotations;

namespace Broot.Redirect.API.Dtos
{
    public class LoginRequest
    {
        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
    }

    public class AuthStatusResponse
    {
        public bool IsAuthenticated { get; set; }

        public long? LoginTime { get; set; }
    }

    // -- Blocked IP management (Phase 5.2) --

    public class BlockIpRequest
    {
        [Required]
        public string Ip { get; set; } = string.Empty;
    }

    public class BlockedIpResponse
    {
        public string Ip { get; set; } = string.Empty;

        public int Attempts { get; set; }

        public DateTimeOffset BlockedUntil { get; set; }
    }
}