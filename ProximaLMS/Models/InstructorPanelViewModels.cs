// ============================================================
// ProximaLMS/Models/InstructorPanelViewModels.cs
// ------------------------------------------------------------
// View models for the tutor-scoped Instructor Panel (Piece 1).
// ============================================================
using System;
using System.Collections.Generic;

namespace ProximaLMS.Models
{
    /// <summary>Tutor identity resolved from the logged-in user.</summary>
    public class InstructorIdentity
    {
        public int TutorID { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string ProfilePhoto { get; set; } = "";
    }

    /// <summary>Header stat tiles on the dashboard.</summary>
    public class InstructorStats
    {
        public int TotalCourses { get; set; }
        public int PublishedCourses { get; set; }
        public int DraftCourses { get; set; }
        public int TotalEnrollments { get; set; }
        public int TotalLessons { get; set; }
    }

    /// <summary>One card in the "My Courses" grid.</summary>
    public class InstructorCourseCard
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; } = "";
        public string CourseLevelName { get; set; } = "";
        public string Language { get; set; } = "";
        public decimal Price { get; set; }
        public int EnrollmentLimit { get; set; }
        public decimal DurationHrs { get; set; }
        public string OneLineDescription { get; set; } = "";
        public string CoverImage { get; set; } = "";
        public string CourseLogo { get; set; } = "";
        public bool IsActive { get; set; }          // true = Published
        public int LessonCount { get; set; }
        public int EnrolledCount { get; set; }
        public DateTime? CreatedDate { get; set; }
    }

    public class InstructorDashboardViewModel
    {
        public InstructorIdentity Tutor { get; set; } = new();
        public InstructorStats Stats { get; set; } = new();
        public List<InstructorCourseCard> Courses { get; set; } = new();
    }

    /// <summary>A single enrolled student + their progress in one course.</summary>
    public class InstructorStudentRow
    {
        public int StudentID { get; set; }
        public string StudentEmail { get; set; } = "";
        public string MobileNumber { get; set; } = "";
        public int CompletedContents { get; set; }
        public int TotalContents { get; set; }
        public int ProgressPercent { get; set; }
    }

    public class InstructorStudentsViewModel
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; } = "";
        public bool IsActive { get; set; }
        public List<InstructorStudentRow> Students { get; set; } = new();
    }
    public class CourseRevenueRow
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; } = "";
        public decimal Price { get; set; }
        public int PaidOrders { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal NetRevenue { get; set; }
    }

    public class InstructorRevenueViewModel
    {
        public string TutorName { get; set; } = "";
        public List<CourseRevenueRow> Courses { get; set; } = new();
        public decimal TotalGross { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal TotalNet { get; set; }
        public int TotalOrders { get; set; }
    }

}
