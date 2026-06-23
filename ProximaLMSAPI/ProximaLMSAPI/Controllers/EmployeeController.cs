using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Models;
using ProximaLMSAPI.Security;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public EmployeeController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private IDbConnection CreateConnection()
            => new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));

        // ─────────────────────────────────────────
        // REGISTER  →  BCrypt hashed
        // ─────────────────────────────────────────
        [HttpPost("register")]
        public IActionResult Register([FromBody] EmployeeApiRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { Message = "Email and password are required." });

            try
            {
                string passwordHash = PasswordHasher.Hash(request.Password);

                using var connection = new MySqlConnection(
                    _configuration.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("SP_employeeregistration", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("p_ID", 0);
                cmd.Parameters.AddWithValue("p_Name", request.Name);
                cmd.Parameters.AddWithValue("p_Gender", request.Gender);
                cmd.Parameters.AddWithValue("p_MobileNumber", request.MobileNumber);
                cmd.Parameters.AddWithValue("p_Email", request.Email);
                cmd.Parameters.AddWithValue("p_Password", passwordHash);
                cmd.Parameters.AddWithValue("p_CreatedBy", request.Name);
                cmd.Parameters.AddWithValue("p_CreatedIP", Request.HttpContext.Connection.RemoteIpAddress?.ToString());
                cmd.Parameters.AddWithValue("p_ModifiedBy", DBNull.Value);
                cmd.Parameters.AddWithValue("p_ModifiedIP", DBNull.Value);
                cmd.Parameters.AddWithValue("p_Salt", string.Empty);   // BCrypt embeds salt
                cmd.Parameters.AddWithValue("p_IsActive", 1);
                // RoleID now flows through from the create form. SP_employeeregistration
                // must declare a matching IN p_RoleID parameter (see note in chat).
                cmd.Parameters.AddWithValue("p_RoleID", (object)request.RoleID ?? DBNull.Value);

                var resultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32)
                { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(resultCode);

                connection.Open();
                cmd.ExecuteNonQuery();

                int code = Convert.ToInt32(resultCode.Value);

                return code switch
                {
                    1 => Ok(new { Message = "Employee registered successfully" }),
                    -1 => BadRequest(new { Message = "Email or Mobile already exists" }),
                    _ => BadRequest(new { Message = "Registration failed" })
                };
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Internal Server Error", Details = ex.Message });
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetAllEmployees()
        {
            using var connection = CreateConnection();

            var result = await connection.QueryAsync<EmployeeDto>(
                "USP_Employee_GetAll",
                commandType: CommandType.StoredProcedure);

            return Ok(result);
        }

        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] EmployeeStatusModel model)
        {
            using var connection = CreateConnection();

            await connection.ExecuteAsync(
                "UPDATE TblUserMasters SET IsActive=@IsActive WHERE ID=@ID",
                model);

            return Ok(new { success = true });
        }

        // ─────────────────────────────────────────────────────────
        // DTOs
        // ─────────────────────────────────────────────────────────
        public class EmployeeApiRequest
        {
            public string Name { get; set; }
            public string Gender { get; set; }
            public string MobileNumber { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
            public int? RoleID { get; set; }
        }

        public class EmployeeDto
        {
            public string Name { get; set; }
            public string Gender { get; set; }
            public string MobileNumber { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
            public bool IsActive { get; set; }
            public string CreatedBy { get; set; }
            public string CreatedIP { get; set; }
            public string ModifiedBy { get; set; }
            public string ModifiedIP { get; set; }
            public string CreatedDate { get; set; }
            public string ModifiedDate { get; set; }
            public string RoleID { get; set; }
            public int ID { get; set; }
        }
    }
}
