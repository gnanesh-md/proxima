// ============================================================
// ProximaLMS/Models/RegisterWizardViewModel.cs
// ============================================================
using System.ComponentModel.DataAnnotations;

namespace ProximaLMS.Models
{
    public class RegisterWizardViewModel
    {
        // Step 1
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, MinLength(8)]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = "";

        [Range(typeof(bool), "true", "true",
            ErrorMessage = "You must accept the terms.")]
        public bool AcceptTerms { get; set; }

        // Step 3
        [Required]
        public string FullName { get; set; } = "";

        public string? Gender { get; set; }

        [Required]
        public string MobileNumber { get; set; } = "";

        [Required]
        public string CountryCode { get; set; } = "+91";

        public DateTime? DateOfBirth { get; set; }

        // Step 4 (all optional)
        public string? Bio               { get; set; }
        public string? Interests         { get; set; }
        public string? PreferredLanguage { get; set; }
        public string? Location          { get; set; }
        public string? LinkedInUrl       { get; set; }
        public string? WebsiteUrl        { get; set; }
        public string? ProfilePhoto      { get; set; }
    }
}
