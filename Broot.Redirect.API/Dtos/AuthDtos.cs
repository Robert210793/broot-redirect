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
}
