namespace ProximaLMS.Models
{
    public class CourseApiResponse
    {
        public CourseDto Course { get; set; }
        public List<CourseContentDto> Contents { get; set; }
    }

    public class CourseDto
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
        public string CourseLevel { get; set; }
         public string CourseLevelName { get; set; }
        public string Language { get; set; }
        public string Category { get; set; }
        public string Instructor { get; set; }
        public string TutorName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Price { get; set; }
        public int EnrollmentLimit { get; set; }
        public decimal DurationHrs { get; set; }
        public string OneLineDescription { get; set; }
        public string CourseLogo { get; set; }
        public string CoverImage { get; set; }
        public string PromoVideo { get; set; }
        public bool IsActive { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }


    }

    public class CourseContentDto
    {
        public int ContentID { get; set; }
        public int CourseID { get; set; }
        public string ContentTitle { get; set; }
        public string Description { get; set; }
        public string FileType { get; set; }
        public int SortOrder { get; set; }
        public string VideoThumbnail { get; set; }
        public string ContentFile { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
    }
    public class CourseAssignmentItem
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
        public string CourseLevelName { get; set; }
        public string TutorName { get; set; }
        public string CoverImage { get; set; }
        public string Language { get; set; }
        public decimal Price { get; set; }
        public bool IsAssigned { get; set; }
        public string AssignedDate { get; set; }
    }

    public class StudentCourseAssignViewModel
    {
        public int StudentID { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public List<CourseAssignmentItem> Courses { get; set; } = new();
    }

    // ── REQUEST DTOs ───────────────────────────────────────────

    public class AssignCourseRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
    }

    public class SaveAllAssignmentsRequest
    {
        public int StudentID { get; set; }
        public List<int> CourseIDs { get; set; } = new();
    }
    public class BulkAssignMvcRequest
    {
        public List<int> StudentIDs { get; set; } = new();
        public List<int> CourseIDs { get; set; } = new();
        public DateTime? DueDate { get; set; }
        public bool IsMandatory { get; set; }
        public string Note { get; set; }
        public bool SendEmails { get; set; } = true;   // UI checkbox in piece 3
    }
    

}
