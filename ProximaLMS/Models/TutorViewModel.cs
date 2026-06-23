using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

public class TutorViewModel
{
    public int? TutorID { get; set; }

    //[Required]
    //[StringLength(50)]
    public string? TutorCode { get; set; }

    [Required]
    [StringLength(150)]
    public string? FullName { get; set; }

    [Required]
    public string? Gender { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }

    [Required]
    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Enter valid 10 digit mobile number")]
    public string? MobileNumber { get; set; }

    
    public string? AlternateMobile { get; set; }

    [Required]
    public string? Qualification { get; set; }

    [Range(0, 50)]
    public int? ExperienceYears { get; set; }

    public string? ExpertiseAreas { get; set; }

    public string? ProfileSummary { get; set; }

    public IFormFile? ProfilePhotoFile { get; set; }
    public IFormFile? ResumeFileUpload { get; set; }

    public string? ProfilePhoto { get; set; }
    public string? ResumeFile { get; set; }
  
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

   
    public string? City { get; set; }

   
    public string? State { get; set; }

 
    public string? Country { get; set; }

    
    public string? Pincode { get; set; }

   
    public string? BankAccountNumber { get; set; }

   
    public string? IFSCCode { get; set; }

   
    public string? BankName { get; set; }

    public string? UPIID { get; set; }

    public int? LoginUserID { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
}
public class ToggleStatusModel
{
    public int TutorID { get; set; }
    public bool IsActive { get; set; }
}
