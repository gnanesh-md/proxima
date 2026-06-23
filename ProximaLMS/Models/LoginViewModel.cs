using System.ComponentModel.DataAnnotations;

namespace ProximaLMS.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Full Name is required")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Gender is required")]
        public string Gender { get; set; }

        [Required(ErrorMessage = "Country Code is required")]
        [Display(Name = "Country Code")]
        public string CountryCode { get; set; }

        [Required(ErrorMessage = "Phone Number is required")]
        [Display(Name = "Phone Number")]
        [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Phone number must be exactly 10 digits and only numbers.")]
        public string PhoneNumber { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address (e.g., name@example.com).")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
            ErrorMessage = "Password must be at least 8 characters long, contain 1 uppercase letter, 1 number, and 1 special character.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Confirm Password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; }

        [MustBeTrue(ErrorMessage = "You must accept the terms and conditions.")]
        [Display(Name = "Accept Terms & Conditions")]
        public bool AcceptTerms { get; set; }

        public int? EmployeeID { get; set; }

        [Display(Name = "Referral Code")]
        public string? ReferralCode { get; set; }
    }

    public class ApiResponse
    {
        public string Message { get; set; }
    }

    public class RegisterApiRequest
    {
        public string Name { get; set; }
        public string Gender { get; set; }
        public string MobileNumber { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string? ReferralCode { get; set; }
    }

    public class ResendOtpResponse
    {
        public string Message { get; set; }
        public int UserId { get; set; }
        public string OTP { get; set; }
    }

    // ── UPDATED: added RoleID so role can be checked before sending OTP ──
    public class LoginApiResponse
    {
        public int UserId { get; set; }
        public string OTP { get; set; }

        // Added: returned by API after credential check
        // Used to verify role IsActive BEFORE sending OTP
        public int RoleID { get; set; } = 0;
        public bool IsActive { get; set; } = true;
    }

    // ── UPDATED: added RoleIsActive + IsActive + RoleName ──
    public class VerifyOtpResponse
    {
        public string Message { get; set; }
        public string Token { get; set; }
        public int RoleID { get; set; }

        // Added: used to block login if role or user is disabled
        public string RoleName { get; set; } = "";
        public bool RoleIsActive { get; set; } = true;
        public bool IsActive { get; set; } = true;
    }

    // ── Combined model for Login + Register page ──────────────
    public class LoginRegisterViewModel
    {
        public LoginViewModel Login { get; set; } = new LoginViewModel();
        public RegisterViewModel Register { get; set; } = new RegisterViewModel();
    }

    // ── OtpResponse (used by AccountController ForgotPassword) ─
    //public class OtpResponse
    //{
    //    public int UserId { get; set; }
    //}
}