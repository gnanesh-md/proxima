namespace ProximaLMSAPI.Models
{
    public class TutorDto
    {
        public int TutorID { get; set; }

        public string? TutorCode { get; set; }
        public string? FullName { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Email { get; set; }
        public string? MobileNumber { get; set; }
        public string? AlternateMobile { get; set; }
        public string? Qualification { get; set; }
        public int? ExperienceYears { get; set; }
        public string? ExpertiseAreas { get; set; }
        public string? ProfileSummary { get; set; }
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
        public int LoginUserID { get; set; }
        public bool IsActive { get; set; }
        public string? CreatedBy { get; set; }
    }

    public class ToggleStatusModel
    {
        public int TutorID { get; set; }
        public bool IsActive { get; set; }
    }
    public class EmployeeStatusModel
    {
        public int ID { get; set; }
        public bool IsActive { get; set; }
    }
}
