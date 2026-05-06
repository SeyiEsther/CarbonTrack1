using System.ComponentModel.DataAnnotations;

namespace CarbonTrack.Models
{
    public class LoginViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required]
        public string FullName { get; set; } = "";

        [Required]
        public string OrgName { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string Password { get; set; } = "";

        [Required, Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public class AcceptInviteViewModel
    {
        public string Token { get; set; } = "";
        public string InviteEmail { get; set; } = "";
        public string OrgName { get; set; } = "";

        [Required]
        public string FullName { get; set; } = "";

        [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string Password { get; set; } = "";

        [Required, Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
