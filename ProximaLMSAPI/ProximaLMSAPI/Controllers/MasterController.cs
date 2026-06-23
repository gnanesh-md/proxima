using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MasterController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public MasterController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ==============================
        // GET : api/master/students
        // ==============================
        [HttpGet("students")]
        public IActionResult GetStudentList()
        {
            try
            {
                var students = new List<object>();

                using (var connection = new MySqlConnection(
                    _configuration.GetConnectionString("ConnectionString")))
                {
                    using (var cmd = new MySqlCommand("sp_getstudentlist", connection))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        connection.Open();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                students.Add(new
                                {
                                    Id = reader["ID"],
                                    Name = reader["Name"],
                                    Email = reader["Email"],
                                    Gender = reader["Gender"],
                                    MobileNumber = reader["MobileNumber"],                                  
                                    IsActive = reader["IsActive"],
                                    CreatedDate = reader["CreatedDate"]
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    Success = true,
                    Count = students.Count,
                    Data = students
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpGet("students/monthly")]
        public IActionResult GetMonthlyStudentChart()
        {
            try
            {
                var result = new List<MonthlyStudentChartDto>();

                using var connection = new MySqlConnection(
                    _configuration.GetConnectionString("ConnectionString"));

                using var cmd = new MySqlCommand(
                    "sp_getstudentcountmonthwise", connection);

                cmd.CommandType = CommandType.StoredProcedure;

                connection.Open();
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                   // int year = Convert.ToInt32(reader["Year"]);
                    string month = Convert.ToString(reader["MonthName"]);

                    result.Add(new MonthlyStudentChartDto
                    {
                        Month =month,
                        Count = Convert.ToInt32(reader["TotalStudents"])
                    });
                }

                return Ok(new
                {
                    Success = true,
                    Data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpPost("student/status")]
        public IActionResult UpdateStudentStatus([FromBody] UpdateStudentStatusDto model)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("sp_updatestudentstatus", con);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("p_ID", model.Id);
                cmd.Parameters.AddWithValue("p_IsActive", model.IsActive);

                con.Open();
                cmd.ExecuteNonQuery();

                return Ok(new
                {
                    success = true,
                    message = "Status updated successfully"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        [HttpGet("courselist")]
        public IActionResult Getcourselist()
        {
            try
            {
                var students = new List<object>();

                using (var connection = new MySqlConnection(
                    _configuration.GetConnectionString("ConnectionString")))
                {
                    using (var cmd = new MySqlCommand("sp_getCourseList", connection))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        connection.Open();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                students.Add(new
                                {
                                    CourseID = reader["CourseID"],
                                    CourseTitle = reader["CourseTitle"],
                                    CourseLevel = reader["LevelName"],
                                   
                                   
                                    Instructor= reader["FullName"],
                                    IsActive = Convert.ToBoolean(reader["IsActive"]),
                                    CreatedDate = reader["CreatedDate"]
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    Success = true,
                    Count = students.Count,
                    Data = students
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpPost("course/status")]
        public IActionResult UpdateCourseStatus([FromBody] UpdateCourseStatusDto model)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("sp_updateCoursestatus", con);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("p_ID", model.Id);
                cmd.Parameters.AddWithValue("p_IsActive", model.IsActive);

                con.Open();
                cmd.ExecuteNonQuery();

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Status updated successfully"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }


        public class MonthlyStudentChartDto
        {
            public string Month { get; set; }
            public int Count { get; set; }
        }
        public class UpdateStudentStatusDto
        {
            public string Id { get; set; }
            public bool IsActive { get; set; }
        }
        public class UpdateCourseStatusDto
        {
            public int Id { get; set; }
            public bool IsActive { get; set; }
        }
        public class ApiResponse<T>
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public T Data { get; set; }
        }
    }
}
