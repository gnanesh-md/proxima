using System.ComponentModel.DataAnnotations;

namespace ProximaLMS.Models
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; }
    }
    public class OtpResponse
    {
        public string Message { get; set; }
        public int UserId { get; set; }
        public string OTP { get; set; }
    }

}
