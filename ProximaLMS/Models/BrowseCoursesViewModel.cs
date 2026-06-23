// ============================================================
// ProximaLMS/Models/BrowseCoursesViewModel.cs
// ------------------------------------------------------------
// View-models for the student "Browse / Purchase / Wishlist" screens.
// Module 06 update: CreatePaymentOrderRequest and
// VerifyPaymentRequest extended for coupon / referral stacking.
// ============================================================
using System;
using System.Collections.Generic;

namespace ProximaLMS.Models
{
    // ════════════════════════════════════════════════════════════
    //  BROWSE
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Backs the main browse page (/Courses/Browse) — shows
    /// "My Courses" + "All Courses" with search.
    /// </summary>
    public class BrowseCoursesViewModel
    {
        public string StudentName { get; set; } = "";
        public int StudentID { get; set; }
        public string RazorpayKey { get; set; } = "";

        public List<BrowseCourseCard> AssignedCourses { get; set; } = new();
        public List<BrowseCourseCard> AllCourses { get; set; } = new();
    }

    /// <summary>
    /// One card on the browse page. Matches the JSON returned by
    /// GET /api/courseassignment/courses/{studentId}
    /// (SP_Course_GetAllWithAssignment).
    /// </summary>
    public class BrowseCourseCard
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; } = "";
        public string TutorName { get; set; } = "";
        public string CourseLevelName { get; set; } = "";
        public string Language { get; set; } = "";
        public string CoverImage { get; set; } = "";
        public decimal Price { get; set; }
        public bool IsAssigned { get; set; }

        /// <summary>Set by the controller after a wishlist lookup.</summary>
        public bool IsWishlisted { get; set; }

        /// <summary>Set by the controller from the review summary.</summary>
        public double AvgRating { get; set; }
        public int ReviewCount { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    //  PREVIEW
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Backs /Courses/Preview/{id} — promo plays, content is locked.
    /// </summary>
    public class BrowsePreviewViewModel
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; } = "";
        public string TutorName { get; set; } = "";
        public string CourseLevelName { get; set; } = "";
        public string Language { get; set; } = "";
        public string CoverImage { get; set; } = "";
        public string PromoVideo { get; set; } = "";
        public string OneLineDescription { get; set; } = "";
        public decimal Price { get; set; }
        public decimal DurationHrs { get; set; }
        public int StudentID { get; set; }
        public bool IsAssigned { get; set; }
        public bool IsWishlisted { get; set; }
        public string RazorpayKey { get; set; } = "";

        public List<BrowseContentCard> Contents { get; set; } = new();
    }

    /// <summary>One locked content row on the preview page.</summary>
    public class BrowseContentCard
    {
        public string ContentTitle { get; set; } = "";
        public string Description { get; set; } = "";
        public string VideoThumbnail { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════
    //  WISHLIST
    // ════════════════════════════════════════════════════════════

    /// <summary>Backs the student wishlist page (/Courses/Wishlist).</summary>
    public class WishlistViewModel
    {
        public string StudentName { get; set; } = "";
        public int StudentID { get; set; }
        public string RazorpayKey { get; set; } = "";

        public List<WishlistCourseCard> Courses { get; set; } = new();
    }

    /// <summary>
    /// One wishlisted course. Matches the JSON from
    /// GET /api/wishlist/{studentId} (SP_Wishlist_GetForStudent).
    /// </summary>
    public class WishlistCourseCard
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; } = "";
        public string TutorName { get; set; } = "";
        public string CourseLevelName { get; set; } = "";
        public string Language { get; set; } = "";
        public string CoverImage { get; set; } = "";
        public decimal Price { get; set; }
        public bool IsAssigned { get; set; }
        public DateTime? AddedOn { get; set; }

        /// <summary>Set by the controller from the review summary.</summary>
        public double AvgRating { get; set; }
        public int ReviewCount { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    //  REQUEST DTOs  (MVC controller <- browser JSON)
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Sent by the checkout modal when the student clicks "Pay".
    /// CouponCode is optional — null / empty string = no coupon.
    /// </summary>
    public class CreatePaymentOrderRequest
    {
        public int CourseID { get; set; }
        public string? CouponCode { get; set; }   // Module 06 - nullable
    }

    /// <summary>
    /// Sent after Razorpay returns its payment fields.
    /// CouponID / DiscountAmount / OriginalAmount are echoed back
    /// from the create-order API response so the verify step can
    /// record them server-side without re-validating the coupon.
    /// </summary>
    public class VerifyPaymentRequest
    {
        public int CourseID { get; set; }
        public string RazorpayOrderId { get; set; } = "";
        public string RazorpayPaymentId { get; set; } = "";
        public string RazorpaySignature { get; set; } = "";
        public int CouponID { get; set; }   // Module 06 - 0 = no coupon
        public decimal DiscountAmount { get; set; }   // Module 06 - 0.00 = no discount
        public decimal OriginalAmount { get; set; }   // Module 06 - full course price
    }

    public class WishlistToggleRequest
    {
        public int CourseID { get; set; }
    }
}