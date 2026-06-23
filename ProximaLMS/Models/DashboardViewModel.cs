using ProximaLMS.Controllers;

namespace ProximaLMS.Models
{
    public class DashboardViewModel
    {
  
        public string TotalIncome { get; set; }
        public int IncomePercent { get; set; }

        public string RatingAverage { get; set; }
        public int RatingPercent { get; set; }

        public int NewStudents { get; set; }
        public int NewStudentsPercent { get; set; }

        public int TotalStudents { get; set; }
        public int AllStudentsPercent { get; set; }

        public int TotalCourses { get; set; }
        public int AllCoursesPercent { get; set; }

        public int TotalTutors { get; set; }
        public int AllTutorsPercent { get; set; }

        public int TotalEmployees { get; set; }
        public int AllEmployeesPercent { get; set; }

        /// <summary>Recent students shown in the dashboard table (populated by DashboardController)</summary>
        public List<StudentDashItem> RecentStudents { get; set; } = new();

        // Legacy — kept for backward-compat
        public List<Student> Students { get; set; } = new();
        public List<Tutor> Tutors { get; set; } = new();

        public List<string> ChartMonths { get; set; } = new();
        public List<int> ChartCounts { get; set; } = new();
    }

    public class Student
    {
        public int StuID { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string UserPhotoPath { get; set; }
    }

    public class Tutor
    {
        public int TutorID { get; set; }
        public string TutorName { get; set; }
        public string Email { get; set; }
        public string ProfilePicPath { get; set; }
    }

    public class MonthlyStudentChartDto
    {
        public string Month { get; set; }
        public int Count { get; set; }
    }
}
