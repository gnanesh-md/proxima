using System.ComponentModel.DataAnnotations;

namespace ProximaLMS.Models
{
    public class StudentViewModel
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string Gender { get; set; }
        public string Email { get; set; }
        public string MobileNumber { get; set; }
        [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}")]
        public string CreatedDate { get; set; }
        public bool IsActive { get; set; }
    }
    public class CourseViewModel
    {
        public int CourseID { get; set; }
        public string CourseTitle { get; set; }
        public string CourseLevel { get; set; }
        public string Instructor { get; set; }     
        public bool IsActive { get; set; }
    }
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
    //public class MonthlyStudentChartDto
    //{
    //    public string Month { get; set; }
    //    public int Count { get; set; }
    //}
    public class UpdateCourseStatusDto
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
    }

}
