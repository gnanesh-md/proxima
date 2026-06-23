using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Models;
using System.Data;
using Dapper;

namespace ProximaLMSAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TutorController : ControllerBase
    {
        private readonly IConfiguration _config;

        public TutorController(IConfiguration config)
        {
            _config = config;
        }

        private IDbConnection CreateConnection()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // =============================
        // ✅ SAVE (Insert / Update)
        // =============================
        [HttpPost("save")]
        public async Task<IActionResult> SaveTutor([FromBody] TutorDto model)
        {
            using var connection = CreateConnection();

            var parameters = new DynamicParameters();
            parameters.Add("p_TutorID", model.TutorID);
            parameters.Add("p_TutorCode", model.TutorCode);
            parameters.Add("p_FullName", model.FullName);
            parameters.Add("p_Gender", model.Gender);
            parameters.Add("p_DateOfBirth", model.DateOfBirth);
            parameters.Add("p_Email", model.Email);
            parameters.Add("p_MobileNumber", model.MobileNumber);
            parameters.Add("p_AlternateMobile", model.AlternateMobile);
            parameters.Add("p_Qualification", model.Qualification);
            parameters.Add("p_ExperienceYears", model.ExperienceYears);
            parameters.Add("p_ExpertiseAreas", model.ExpertiseAreas);
            parameters.Add("p_ProfileSummary", model.ProfileSummary);
            parameters.Add("p_ProfilePhoto", model.ProfilePhoto);
            parameters.Add("p_ResumeFile", model.ResumeFile);
            parameters.Add("p_AddressLine1", model.AddressLine1);
            parameters.Add("p_AddressLine2", model.AddressLine2);
            parameters.Add("p_City", model.City);
            parameters.Add("p_State", model.State);
            parameters.Add("p_Country", model.Country);
            parameters.Add("p_Pincode", model.Pincode);
            parameters.Add("p_BankAccountNumber", model.BankAccountNumber);
            parameters.Add("p_IFSCCode", model.IFSCCode);
            parameters.Add("p_BankName", model.BankName);
            parameters.Add("p_UPIID", model.UPIID);
            parameters.Add("p_LoginUserID", model.LoginUserID);
            parameters.Add("p_IsActive", model.IsActive);
            parameters.Add("p_CreatedBy", model.CreatedBy);

            var tutorId = await connection.QueryFirstOrDefaultAsync<int>(
                "SP_Tutor_Save",
                parameters,
                commandType: CommandType.StoredProcedure
            );

            return Ok(new
            {
                success = true,
                tutorId,
                message = model.TutorID == 0
                    ? "Tutor created successfully"
                    : "Tutor updated successfully"
            });
        }

        // =============================
        // ✅ GET ALL
        // =============================
        [HttpGet("list")]
        public async Task<IActionResult> GetAllTutors()
        {
            using var connection = CreateConnection();

            var result = await connection.QueryAsync<TutorDto>(
                "USP_Tutor_GetAll",
                commandType: CommandType.StoredProcedure
            );

            return Ok(result);
        }

        // =============================
        // ✅ GET BY ID
        // =============================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTutorById(int id)
        {
            using var connection = CreateConnection();

            var parameters = new DynamicParameters();
            parameters.Add("p_TutorID", id);

            var result = await connection.QueryFirstOrDefaultAsync<TutorDto>(
                "USP_Tutor_GetByID",
                parameters,
                commandType: CommandType.StoredProcedure
            );

            if (result == null)
                return NotFound(new { message = "Tutor not found" });

            return Ok(result);
        }

        // =============================
        // ✅ TOGGLE STATUS
        // =============================
        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] ToggleStatusModel model)
        {
            using var connection = CreateConnection();

            await connection.ExecuteAsync(
                "UPDATE TblTutorRegistration SET IsActive=@IsActive WHERE TutorID=@TutorID",
                model);

            return Ok(new { success = true });
        }
    }
}
