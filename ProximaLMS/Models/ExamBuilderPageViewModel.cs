// ============================================================
// ProximaLMS/Models/ExamBuilderPageViewModel.cs
// ------------------------------------------------------------
// Minimal model for the builder page. Everything else (exams,
// questions, options) is loaded/saved by the page via AJAX.
// ============================================================
namespace ProximaLMS.Models
{
    public class ExamBuilderPageViewModel
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; } = "";
    }
}
