using System.ComponentModel.DataAnnotations;

namespace ProximaLMS.Models
{
    public class EmployeeRequest
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
        
        public int? EmployeeID { get; set; }
        public int? RoleID { get; set; }
        public bool IsActive { get; set; }
    }
    public class EmployeeDto
    {
        public string Name { get; set; }
        public string Gender { get; set; }
        public string MobileNumber { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public bool IsActive { get; set; }
        public string CreatedBy { get; set; }
        public string CreatedIP { get; set; }
        public string ModifiedBy { get; set; }
        public string ModifiedIP { get; set; }
        public string CreatedDate { get; set; }
        public string ModifiedDate { get; set; }
        public string RoleID { get; set; }

        public int ID { get; set; }
    }
    public class EmployeeStatusModel
    {
        public int ID { get; set; }
        public bool IsActive { get; set; }
    }
}
