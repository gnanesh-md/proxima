using System;
using System.Collections.Generic;

namespace ProximaLMS.Models
{
    public class CourseSummaryViewModel
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
        public string Instructor { get; set; }
        public string TutorName { get; set; }
        public string CourseLevel { get; set; }
        public string CourseLevelName { get; set; }
        public string Language { get; set; }
        public decimal Price { get; set; }
        public string CoverImage { get; set; }
        public bool IsActive { get; set; }
    }

    public class CourseDetailsViewModel
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
        public string CourseLevel { get; set; }
        public string CourseLevelName { get; set; }
        public string Language { get; set; }
        public string Category { get; set; }
        public string Instructor { get; set; }
        public string TutorName { get; set; }
        public decimal Price { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal DurationHrs { get; set; }
        public string OneLineDescription { get; set; }
        public string CourseLogo { get; set; }
        public string CoverImage { get; set; }
        public string PromoVideo { get; set; }
        
    public bool IsActive { get; set; }
        public List<CourseContentViewModel> Contents { get; set; }

        // ── progress (populated for students only) ──
        public bool IsStudent { get; set; }
        public bool IsEnrolled { get; set; }   // student is enrolled in this course (gates the player)
        public int TotalContents { get; set; }
        public int CompletedContents { get; set; }
        public int ProgressPercent { get; set; }

        // ── reviews ──
        public double AvgRating { get; set; }       // 0.0 – 5.0
        public int ReviewCount { get; set; }
        public List<CourseReviewItem> Reviews { get; set; } = new();
        public CourseReviewItem MyReview { get; set; }   // this student's review, or null
        public int[] RatingBreakdown { get; set; } = new int[5]; // [0]=5★ … [4]=1★
    }

    public class CourseContentViewModel
    {
        public int ContentID { get; set; }
        public string ContentTitle { get; set; }
        public string Description { get; set; }
        public string VideoThumbnail { get; set; }
        public string ContentFile { get; set; }

        // ── set per-student on the Details page ──
        public bool IsCompleted { get; set; }
        public string FileType { get; set; } = "Video";
        public int LastPositionSeconds { get; set; }   // resume point, in seconds
    }

    // ════════════════════════════════════════════════════════════
    //  PROGRESS
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// One row from GET /api/progress/summary/{studentId}
    /// (SP_Progress_GetSummary) — total vs completed lessons per course.
    /// </summary>
    public class ProgressSummaryRow
    {
        public int CourseID { get; set; }
        public int TotalContents { get; set; }
        public int CompletedContents { get; set; }
    }

    /// <summary>Body of POST /Courses/MarkContentComplete.</summary>
    public class MarkContentRequest
    {
        public int CourseID { get; set; }
        public int ContentID { get; set; }
        public bool Completed { get; set; }
    }

    /// <summary>
    /// One row from GET /api/progress/course/{studentId}/{courseId}
    /// (SP_Progress_GetForCourse) — completion + resume position.
    /// </summary>
    public class CourseProgressRow
    {
        public int ContentID { get; set; }
        public int LastPositionSeconds { get; set; }
        public bool IsCompleted { get; set; }
    }

    /// <summary>
    /// Body of POST /Courses/SaveVideoPosition — sent every few
    /// seconds while a lesson video plays.
    /// </summary>
    public class SaveVideoPositionRequest
    {
        public int CourseID { get; set; }
        public int ContentID { get; set; }
        public int Position { get; set; }   // current playback point, seconds
        public int Duration { get; set; }   // total video length, seconds
    }

    // ════════════════════════════════════════════════════════════
    //  REVIEWS
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// One review row from GET /api/review/course/{courseId}
    /// (SP_Review_GetForCourse).
    /// </summary>
    public class CourseReviewItem
    {
        public int ReviewID { get; set; }
        public int CourseID { get; set; }
        public int StudentID { get; set; }
        public string StudentName { get; set; } = "Student";
        public int Rating { get; set; }
        public string ReviewText { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    /// <summary>
    /// One row from GET /api/review/summary (SP_Review_GetSummary) —
    /// average rating + count per course.
    /// </summary>
    public class ReviewSummaryRow
    {
        public int CourseID { get; set; }
        public double AvgRating { get; set; }
        public int ReviewCount { get; set; }
    }

    /// <summary>Body of POST /Courses/SubmitReview.</summary>
    public class SubmitReviewRequest
    {
        public int CourseID { get; set; }
        public int Rating { get; set; }
        public string ReviewText { get; set; } = "";
    }
}