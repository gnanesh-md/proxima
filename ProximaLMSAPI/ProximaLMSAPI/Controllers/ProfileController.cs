
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Security;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            IConfiguration config,
            IWebHostEnvironment env,
            ILogger<ProfileController> logger)
        {
            _config = config;
            _env = env;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // GET  api/profile/{userId}
        // ════════════════════════════════════════════════════════
        [HttpGet("{userId:int}")]
        public async Task<IActionResult> Get(int userId)
        {
            if (userId <= 0)
                return BadRequest(new { success = false, message = "Invalid user id." });

            try
            {
                using var conn = CreateConn();
                var row = await conn.QuerySingleOrDefaultAsync(
                    "SP_User_GetProfile",
                    new { p_UserID = userId },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "User not found." });

                return Ok(new { success = true, data = row });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Profile get failed for user {Uid}", userId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST api/profile/save
        // Body: { UserID, DateOfBirth, Bio, ProfilePhoto, Interests, ... }
        // Used by step 4 of the wizard AND the My Profile page.
        // ════════════════════════════════════════════════════════
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SaveProfileApiRequest req)
        {
            if (req == null || req.UserID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_UserID", req.UserID);
                p.Add("p_DateOfBirth", req.DateOfBirth);
                p.Add("p_Bio", req.Bio ?? "");
                p.Add("p_ProfilePhoto", req.ProfilePhoto ?? "");
                p.Add("p_Interests", req.Interests ?? "");
                p.Add("p_PreferredLanguage", req.PreferredLanguage ?? "");
                p.Add("p_Location", req.Location ?? "");
                p.Add("p_LinkedInUrl", req.LinkedInUrl ?? "");
                p.Add("p_WebsiteUrl", req.WebsiteUrl ?? "");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_User_SaveProfile", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                return code == 1
                    ? Ok(new { success = true, message = "Profile saved." })
                    : BadRequest(new { success = false, message = "Could not save profile." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Profile save failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST api/profile/upload-photo
        // multipart/form-data:  file = <image>, userId = <int>
        // ════════════════════════════════════════════════════════
        [HttpPost("upload-photo")]
        [RequestSizeLimit(3 * 1024 * 1024)]            // 3 MB
        public async Task<IActionResult> UploadPhoto(
            [FromForm] IFormFile file,
            [FromForm] int userId)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded." });

            if (file.Length > 2 * 1024 * 1024)
                return BadRequest(new { success = false, message = "Image must be under 2 MB." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return BadRequest(new { success = false, message = "Only JPEG, PNG or WebP images are allowed." });

            try
            {
                // wwwroot/uploads/profiles/{userId}_{guid}.{ext}
                string webRoot = _env.WebRootPath
                                 ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                string targetDir = Path.Combine(webRoot, "uploads", "profiles");
                Directory.CreateDirectory(targetDir);

                string fileName = $"{userId}_{Guid.NewGuid():N}{ext}";
                string fullPath = Path.Combine(targetDir, fileName);

                await using (var fs = new FileStream(fullPath, FileMode.CreateNew))
                {
                    await file.CopyToAsync(fs);
                }

                string relativePath = $"/uploads/profiles/{fileName}";
                return Ok(new { success = true, path = relativePath });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Profile photo upload failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST api/profile/deactivate
        // Body: { UserID, CurrentPassword, Reason }
        // Verifies password (BCrypt or legacy SHA-256) THEN deactivates.
        // ════════════════════════════════════════════════════════
        [HttpPost("deactivate")]
        public async Task<IActionResult> Deactivate([FromBody] DeactivateApiRequest req)
        {
            if (req == null
                || req.UserID <= 0
                || string.IsNullOrWhiteSpace(req.CurrentPassword))
                return BadRequest(new { success = false, message = "User id and current password are required." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                // ── 1. fetch stored hash + salt ──
                var row = await conn.QuerySingleOrDefaultAsync(
                    "SP_User_GetPasswordHash",
                    new { p_UserID = req.UserID },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "User not found." });

                string storedHash = (string)(row.PasswordHash ?? "");
                string salt = (string)(row.Salt ?? "");
                bool isActive = Convert.ToBoolean(row.IsActive);

                if (!isActive)
                    return BadRequest(new { success = false, message = "Account is already deactivated." });

                // ── 2. verify password ──
                var verify = PasswordHasher.Verify(req.CurrentPassword, storedHash, salt);
                if (verify == PasswordVerifyResult.Failed)
                    return Unauthorized(new { success = false, message = "Password incorrect." });

                // ── 3. call deactivation SP ──
                var p = new DynamicParameters();
                p.Add("p_UserID", req.UserID);
                p.Add("p_Reason", string.IsNullOrWhiteSpace(req.Reason) ? "Not specified" : req.Reason);
                p.Add("p_DeactivatedBy", "self");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 255, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_User_Deactivate", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code switch
                {
                    1 => Ok(new { success = true, message = msg }),
                    -2 => BadRequest(new { success = false, message = msg }),   // last admin
                    -3 => BadRequest(new { success = false, message = msg }),   // already deactivated
                    _ => BadRequest(new { success = false, message = string.IsNullOrEmpty(msg) ? "Could not deactivate." : msg })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Account deactivation failed for user {Uid}", req.UserID);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST api/profile/change-password
        // Body: { UserID, CurrentPassword, NewPassword }
        // Verifies the CURRENT password (BCrypt or legacy SHA-256),
        // then stores a fresh BCrypt hash via Pr_ResetPassword.
        // ════════════════════════════════════════════════════════
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordApiRequest req)
        {
            if (req == null
                || req.UserID <= 0
                || string.IsNullOrWhiteSpace(req.CurrentPassword)
                || string.IsNullOrWhiteSpace(req.NewPassword))
                return BadRequest(new { success = false, message = "User id, current and new password are required." });

            if (req.NewPassword.Length < 8)
                return BadRequest(new { success = false, message = "New password must be at least 8 characters." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                // ── 1. fetch stored hash + salt ──
                var row = await conn.QuerySingleOrDefaultAsync(
                    "SP_User_GetPasswordHash",
                    new { p_UserID = req.UserID },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "User not found." });

                string storedHash = (string)(row.PasswordHash ?? "");
                string salt = (string)(row.Salt ?? "");

                // ── 2. verify the CURRENT password ──
                var verify = PasswordHasher.Verify(req.CurrentPassword, storedHash, salt);
                if (verify == PasswordVerifyResult.Failed)
                    return Unauthorized(new { success = false, message = "Current password is incorrect." });

                // ── 3. hash + persist the NEW password (reuses Pr_ResetPassword) ──
                string newHash = PasswordHasher.Hash(req.NewPassword);

                var p = new DynamicParameters();
                p.Add("p_UserId", req.UserID);
                p.Add("p_NewPassword", newHash);
                p.Add("p_Salt", string.Empty);   // BCrypt embeds its own salt
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("Pr_ResetPassword", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                return code == 1
                    ? Ok(new { success = true, message = "Password changed successfully." })
                    : BadRequest(new { success = false, message = "Could not update password." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Change password failed for user {Uid}", req.UserID);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ── DTOs ──────────────────────────────────────────────
        public class SaveProfileApiRequest
        {
            public int UserID { get; set; }
            public DateTime? DateOfBirth { get; set; }
            public string? Bio { get; set; }
            public string? ProfilePhoto { get; set; }
            public string? Interests { get; set; }
            public string? PreferredLanguage { get; set; }
            public string? Location { get; set; }
            public string? LinkedInUrl { get; set; }
            public string? WebsiteUrl { get; set; }
        }

        public class DeactivateApiRequest
        {
            public int UserID { get; set; }
            public string CurrentPassword { get; set; } = "";
            public string? Reason { get; set; }
        }

        public class ChangePasswordApiRequest
        {
            public int UserID { get; set; }
            public string CurrentPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
        }
    }
}