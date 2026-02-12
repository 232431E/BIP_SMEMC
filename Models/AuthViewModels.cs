using System.ComponentModel.DataAnnotations;

namespace BIP_SMEMC.Models
{
    public class LoginViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class SignupViewModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Industry { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must agree to the Terms and Conditions.")]
        public bool AgreeTerms { get; set; }
    }

    public class ForgotPasswordViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordViewModel
    {
        [Required]
        public string AccessToken { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class TwoFactorVerifyViewModel
    {
        [Required]
        public string Code { get; set; } = string.Empty;
    }

    public class AccountSettingsViewModel
    {
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
        public bool IsTwoFactorEnabled { get; set; }
        public string TwoFactorMethod { get; set; } = "email";
        public string Industry { get; set; } = string.Empty;
        public string? AuthenticatorSecret { get; set; }
        public string? AuthenticatorUri { get; set; }
    }

    public class ProfileViewModel
    {
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "User";

        [Required]
        public string Industry { get; set; } = string.Empty;

        public string? NewPassword { get; set; }

        public string? ConfirmPassword { get; set; }
    }

    public class AdminUsersViewModel
    {
        public List<UserModel> Users { get; set; } = new();
    }
}
