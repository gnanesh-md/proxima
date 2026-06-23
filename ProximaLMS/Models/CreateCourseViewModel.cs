using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProximaLMS.Models
{
    public class CreateCourseViewModel
    {
        public int? CourseID { get; set; }

        [Required]
        public string CourseTitle { get; set; }

        [Required]
        public string CourseCode { get; set; }

        [Required]
        public int CourseLevel { get; set; }

        [Required]
        public int LanguageId { get; set; }

        // Category (module 03)
        [Required(ErrorMessage = "Please choose a category.")]
        public int CategoryId { get; set; }

        [ValidateNever]
        public string CourseLevelName { get; set; }

        [ValidateNever]
        public string TutorName { get; set; }

        [Required]
        public int InstructorId { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime? CourseStartDate { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime? CourseEndDate { get; set; }

        public decimal? Price { get; set; }
        public int? EnrollmentLimit { get; set; }
        public int? CourseDurationHours { get; set; }

        public string SmallDescription { get; set; }

        // Media files
        public IFormFile CourseLogo { get; set; }
        public IFormFile CoverImage { get; set; }
        public IFormFile PromotionalVideo { get; set; }

        // For storing existing file names during edit
        public string ExistingLogoFileName { get; set; }
        public string ExistingCoverFileName { get; set; }
        public string ExistingPromoFileName { get; set; }

        // ── Dropdowns ──
        [ValidateNever] public List<SelectListItem> Levels { get; set; } = new();
        [ValidateNever] public List<SelectListItem> Languages { get; set; } = new();
        [ValidateNever] public List<SelectListItem> Instructors { get; set; } = new();
        [ValidateNever] public List<SelectListItem> Categories { get; set; } = new();

        // ── Tag picker ──
        // AllTags = master list; SelectedTagIds = picked.
        [ValidateNever] public List<TagOption> AllTags { get; set; } = new();
        [ValidateNever] public List<int> SelectedTagIds { get; set; } = new();

        // New content to add (module 2B: Video | PDF | Text)
        [ValidateNever]
        public List<CourseVideoRow> Videos { get; set; } = new();

        // Existing content (for edit mode)
        [ValidateNever]
        public List<ExistingContentViewModel> ExistingContents { get; set; } = new();
    }

    /// <summary>One selectable tag for the wizard's tag picker.</summary>
    public class TagOption
    {
        public int TagId { get; set; }
        public string TagName { get; set; } = "";
        public string ColorHex { get; set; } = "#7B2CBF";
    }

    public class CourseVideoRow
    {
        public int ContentID { get; set; }
        public string ContentTitle { get; set; }
        public string Description { get; set; }

        // module 2B: lesson type — "Video" | "PDF" | "Text"
        public string LessonType { get; set; } = "Video";

        // Video lesson
        public IFormFile VideoThumbnail { get; set; }
        public IFormFile CourseVideo { get; set; }

        // PDF lesson
        public IFormFile LessonFile { get; set; }

        // Text lesson (body stored into Description by the controller)
        public string TextBody { get; set; }
    }

    /// <summary>One existing content row when editing a course.</summary>
    public class ExistingContentViewModel
    {
        public int ContentID { get; set; }
        public string ContentTitle { get; set; }
        public string Description { get; set; }
        public string VideoThumbnail { get; set; }
        public string ContentFile { get; set; }
        public int SortOrder { get; set; }
        public string FileType { get; set; } = "Video";   // Video | PDF | Text
    }

    /// <summary>DTO for the reorder endpoint.</summary>
    public class ReorderContentMvcRequest
    {
        public int CourseID { get; set; }
        public List<int> ContentIDs { get; set; } = new();
    }
}