namespace ProximaLMS.Models
{
    public class StudentDashboardViewModel
    {
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public int TotalAssignedCourses { get; set; }
        public int CompletedCourses { get; set; }
        public int InProgressCourses { get; set; }
        public List<StudentCourseCardViewModel> AssignedCourses { get; set; } = new();
    }

    public class StudentCourseCardViewModel
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
        public string TutorName { get; set; }
        public string CourseLevelName { get; set; }
        public string CoverImage { get; set; }
        public decimal Price { get; set; }
        public string AssignedDate { get; set; }
        public string Language { get; set; }

        // ?? real completion progress (0-100) ??
        public int ProgressPercent { get; set; }

        // ?? review rating ??
        public double AvgRating { get; set; }
        public int ReviewCount { get; set; }
    }
}
