// ============================================================
// ProximaLMS/Models/ProfileViewModel.cs
// ============================================================
namespace ProximaLMS.Models
{
    public class ProfileViewModel
    {
        public int       UserID            { get; set; }
        public string    FullName          { get; set; } = "";
        public string    Email             { get; set; } = "";
        public string    MobileNumber      { get; set; } = "";
        public string    Gender            { get; set; } = "";
        public int       RoleID            { get; set; }

        public DateTime? DateOfBirth       { get; set; }
        public string    Bio               { get; set; } = "";
        public string    ProfilePhoto      { get; set; } = "";
        public string    Interests         { get; set; } = "";
        public string    PreferredLanguage { get; set; } = "";
        public string    Location          { get; set; } = "";
        public string    LinkedInUrl       { get; set; } = "";
        public string    WebsiteUrl        { get; set; } = "";
        public int       CompletionPercent { get; set; }

        public DateTime? CreatedDate       { get; set; }

        public string RoleLabel => RoleID switch
        {
            1 => "Administrator",
            2 => "Tutor",
            3 => "Student",
            4 => "Employee",
            _ => "User"
        };
    }
}
