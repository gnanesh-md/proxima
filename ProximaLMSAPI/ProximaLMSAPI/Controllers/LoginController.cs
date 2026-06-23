
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Security;
using ProximaLMSAPI.Services;
using System;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ITokenService _tokenService;
        private readonly IServiceScopeFactory _scopeFactory;

        public AuthController(
            IConfiguration configuration,
            IMemoryCache cache,
            ITokenService tokenService,
            IServiceScopeFactory scopeFactory)
        {
            _configuration = configuration;
            _cache = cache;
            _tokenService = tokenService;
            _scopeFactory = scopeFactory;
        }

        // ─────────────────────────────────────────
        // REGISTER  →  hashed with BCrypt, no Unsalt
        // ─────────────────────────────────────────
        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { Message = "Email and password are required." });

            try
            {
                string passwordHash = PasswordHasher.Hash(request.Password);

                using var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("SP_CreateandUpdateUser", connection)
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
                cmd.Parameters.AddWithValue("p_CreatedIP", HttpContext.Connection.RemoteIpAddress?.ToString());
                cmd.Parameters.AddWithValue("p_ModifiedBy", DBNull.Value);
                cmd.Parameters.AddWithValue("p_ModifiedIP", DBNull.Value);
                cmd.Parameters.AddWithValue("p_Salt", string.Empty);   // BCrypt embeds salt
                cmd.Parameters.AddWithValue("p_IsActive", 1);

                var resultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32)
                { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(resultCode);

                connection.Open();
                cmd.ExecuteNonQuery();

                int code = Convert.ToInt32(resultCode.Value);

                // On success: make sure the new student has a streak row
                // (prevents the Student Dashboard NULL-streak 500) and link
                // any referral code. Both are non-fatal / best-effort.
                if (code == 1)
                {
                    EnsureUserStreakRow(request.Email);

                    if (!string.IsNullOrWhiteSpace(request.ReferralCode))
                        TryLinkReferral(connection, request.Email, request.ReferralCode.Trim().ToUpper());
                }

                return code switch
                {
                    1 => Ok(new { Message = "User registered successfully" }),
                    -1 => BadRequest(new { Message = "Email or Mobile already exists" }),
                    _ => BadRequest(new { Message = "Registration failed" })
                };
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error: " + ex.Message });
            }
        }

        // Resolve the just-created user by email and link them to the
        // referrer via SP_Referral_Register. Swallows all errors so a
        // referral problem never blocks account creation.
        private void TryLinkReferral(MySqlConnection connection, string email, string referralCode)
        {
            try
            {
                int newUserId;
                using (var idCmd = new MySqlCommand(
                    "SELECT ID FROM TblUserMasters WHERE Email=@Email ORDER BY ID DESC LIMIT 1", connection))
                {
                    idCmd.Parameters.AddWithValue("@Email", email.Trim());
                    var idObj = idCmd.ExecuteScalar();
                    if (idObj == null || idObj == DBNull.Value) return;
                    newUserId = Convert.ToInt32(idObj);
                }

                using var refCmd = new MySqlCommand("SP_Referral_Register", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };
                refCmd.Parameters.AddWithValue("p_RefereeUserID", newUserId);
                refCmd.Parameters.AddWithValue("p_ReferralCode", referralCode);
                refCmd.Parameters.Add(new MySqlParameter("p_RefereeCouponID", MySqlDbType.Int32) { Direction = ParameterDirection.Output });
                refCmd.Parameters.Add(new MySqlParameter("p_ResultCode", MySqlDbType.Int32) { Direction = ParameterDirection.Output });
                refCmd.Parameters.Add(new MySqlParameter("p_Message", MySqlDbType.VarChar, 500) { Direction = ParameterDirection.Output });
                refCmd.ExecuteNonQuery();
            }
            catch
            {
                // intentionally ignored — referral linking is best-effort
            }
        }

        // Ensure the freshly-registered user has a TblUserStreak row so the
        // Student Dashboard never reads a NULL CurrentStreak — which otherwise
        // makes the Razor view throw casting null to int (a 500). Idempotent
        // (skips if a row already exists) and best-effort: a streak problem
        // must never block or fail account creation.
        private void EnsureUserStreakRow(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            try
            {
                using var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                connection.Open();
                using var cmd = new MySqlCommand(
                    @"INSERT INTO TblUserStreak (StudentID, CurrentStreak, LongestStreak, LastLoginDate)
                      SELECT u.ID, 0, 0, NULL
                        FROM TblUserMasters u
                       WHERE u.Email = @Email
                         AND NOT EXISTS (SELECT 1 FROM TblUserStreak s WHERE s.StudentID = u.ID)
                       ORDER BY u.ID DESC
                       LIMIT 1", connection);
                cmd.Parameters.AddWithValue("@Email", email.Trim());
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // best-effort — first login's gamification will also create it
            }
        }

        // ─────────────────────────────────────────
        // SEND REGISTRATION OTP (email only)  —  unchanged
        // ─────────────────────────────────────────
        [HttpPost("send-register-otp")]
        public async Task<IActionResult> SendRegisterOtp([FromBody] RegisterOtpRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Email))
                    return BadRequest(new { Message = "Email is required" });

                using (var conn = new MySqlConnection(_configuration.GetConnectionString("ConnectionString")))
                {
                    conn.Open();
                    using var checkCmd = new MySqlCommand(
                        "SELECT COUNT(*) FROM TblUserMasters WHERE Email=@Email OR MobileNumber=@Mobile", conn);
                    checkCmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                    checkCmd.Parameters.AddWithValue("@Mobile", request.MobileNumber ?? "");

                    long exists = Convert.ToInt64(checkCmd.ExecuteScalar());
                    if (exists > 0)
                        return Conflict(new { Message = "Email or mobile number is already registered" });
                }

                string otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                _cache.Set($"regotp:{request.Email.Trim().ToLower()}", otp, TimeSpan.FromMinutes(15));

                bool sent = await SendOtpEmailAsync(request.Email.Trim(), otp);
                if (!sent)
                    return StatusCode(500, new { Message = "Failed to send OTP email. Please try again." });

                return Ok(new { Message = "OTP sent to your email" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // VERIFY REGISTRATION OTP  —  unchanged
        // ─────────────────────────────────────────
        [HttpPost("verify-register-otp")]
        public IActionResult VerifyRegisterOtp([FromBody] VerifyRegisterOtpRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.OTP))
                return BadRequest(new { Message = "Email and OTP are required" });

            string key = $"regotp:{request.Email.Trim().ToLower()}";

            if (!_cache.TryGetValue(key, out string cachedOtp))
                return Unauthorized(new { Message = "OTP expired. Please request a new code." });

            if (cachedOtp != request.OTP.Trim())
                return Unauthorized(new { Message = "Invalid OTP. Please check and try again." });

            _cache.Remove(key);
            return Ok(new { Message = "OTP verified" });
        }

        // ─────────────────────────────────────────
        // LOGIN  →  verify password (BCrypt or legacy) → send OTP
        // ─────────────────────────────────────────
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { Message = "Email and password are required." });

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                var cmd = new MySqlCommand("Pr_CheckLoginWithOTP", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("p_Email", request.Email);

                var pResultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var pUserId = new MySqlParameter("p_UserId", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var pDBPassword = new MySqlParameter("p_DBPassword", MySqlDbType.VarChar, 2000) { Direction = ParameterDirection.Output };
                var pSalt = new MySqlParameter("p_Salt", MySqlDbType.VarChar, 200) { Direction = ParameterDirection.Output };
                var pOTP = new MySqlParameter("p_OTP", MySqlDbType.VarChar, 6) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(pResultCode);
                cmd.Parameters.Add(pUserId);
                cmd.Parameters.Add(pDBPassword);
                cmd.Parameters.Add(pSalt);
                cmd.Parameters.Add(pOTP);

                conn.Open();
                cmd.ExecuteNonQuery();

                if ((int)pResultCode.Value != 1)
                    return Unauthorized(new { Message = "User not found or inactive" });

                // ── PASSWORD VERIFICATION ───────────────────────────
                // Supports BOTH formats: existing SHA-256 hashes verify
                // through the legacy path and are silently upgraded to
                // BCrypt for next time.
                string storedHash = pDBPassword.Value?.ToString() ?? "";
                string salt = pSalt.Value?.ToString() ?? "";
                int userId = (int)pUserId.Value;

                var verify = PasswordHasher.Verify(request.Password, storedHash, salt);

                if (verify == PasswordVerifyResult.Failed)
                    return Unauthorized(new { Message = "Invalid credentials" });

                if (verify == PasswordVerifyResult.SuccessNeedsRehash)
                {
                    // Fire-and-forget upgrade. If it fails, the user can
                    // still log in next time — they'll just verify via
                    // the legacy path again. Never block on this.
                    try
                    {
                        string newHash = PasswordHasher.Hash(request.Password);
                        using var upConn = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                        using var upCmd = new MySqlCommand("SP_User_RehashPassword", upConn)
                        {
                            CommandType = CommandType.StoredProcedure
                        };
                        upCmd.Parameters.AddWithValue("p_UserId", userId);
                        upCmd.Parameters.AddWithValue("p_NewPassword", newHash);

                        var upResult = new MySqlParameter("p_ResultCode", MySqlDbType.Int32)
                        { Direction = ParameterDirection.Output };
                        upCmd.Parameters.Add(upResult);

                        upConn.Open();
                        upCmd.ExecuteNonQuery();
                    }
                    catch
                    {
                        // Non-fatal — login still succeeds. Logged elsewhere
                        // if you want; we intentionally don't surface it
                        // to the user.
                    }
                }

                string otp = pOTP.Value?.ToString() ?? string.Empty;

                // Fetch RoleID + MobileNumber so the MVC layer can check role
                // status BEFORE asking the user to type the OTP.
                int roleId = 0;
                string mobileNumber = string.Empty;
                bool userIsActive = true;

                using (var conn2 = new MySqlConnection(_configuration.GetConnectionString("ConnectionString")))
                using (var infoCmd = new MySqlCommand(
                    "SELECT RoleID, MobileNumber, IsActive FROM TblUserMasters WHERE ID=@Id LIMIT 1", conn2))
                {
                    infoCmd.Parameters.AddWithValue("@Id", userId);
                    conn2.Open();
                    using var rd = infoCmd.ExecuteReader();
                    if (rd.Read())
                    {
                        roleId = rd["RoleID"] != DBNull.Value ? Convert.ToInt32(rd["RoleID"]) : 0;
                        mobileNumber = rd["MobileNumber"]?.ToString() ?? string.Empty;
                        userIsActive = rd["IsActive"] != DBNull.Value && Convert.ToBoolean(rd["IsActive"]);
                    }
                }

                var (emailSent, smsSent) = await SendOtpBothAsync(request.Email, mobileNumber, otp);

                return Ok(new
                {
                    Message = BuildDeliveryMessage(emailSent, smsSent),
                    UserId = userId,
                    RoleID = roleId,
                    IsActive = userIsActive
#if DEBUG
                    ,
                    OTP = otp   // Visible only in DEBUG builds
#endif
                });
            }
            catch (MySqlException ex)
            {
                return StatusCode(500, new { Message = "Database error.", Error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // VERIFY OTP  →  issue JWT via ITokenService
        // ─────────────────────────────────────────
        [HttpPost("verify-otp")]
        public IActionResult VerifyOtp([FromBody] OtpRequest request)
        {
            if (request == null || request.UserId <= 0 || string.IsNullOrWhiteSpace(request.OTP))
                return BadRequest(new { Message = "Invalid OTP request" });

            try
            {
                using var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("Pr_VerifyOTP", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("p_UserId", request.UserId);
                cmd.Parameters.AddWithValue("p_OTP", request.OTP);

                var resultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var roleParam = new MySqlParameter("p_RoleID", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var emailParam = new MySqlParameter("p_Email", MySqlDbType.VarChar, 150) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(resultCode);
                cmd.Parameters.Add(roleParam);
                cmd.Parameters.Add(emailParam);

                connection.Open();
                cmd.ExecuteNonQuery();

                if (Convert.ToInt32(resultCode.Value) != 1)
                    return Unauthorized(new { Message = "Invalid or expired OTP" });

                int roleId = roleParam.Value != DBNull.Value ? Convert.ToInt32(roleParam.Value) : 0;
                string email = emailParam.Value?.ToString() ?? string.Empty;

                // ── Enrich response with RoleName + IsActive ──
                string roleName = string.Empty;
                bool userIsActive = true;
                bool roleIsActive = true;

                try
                {
                    using var conn2 = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                    conn2.Open();

                    using (var u = new MySqlCommand(
                        "SELECT IsActive FROM TblUserMasters WHERE ID=@Id LIMIT 1", conn2))
                    {
                        u.Parameters.AddWithValue("@Id", request.UserId);
                        var v = u.ExecuteScalar();
                        if (v != null && v != DBNull.Value)
                            userIsActive = Convert.ToBoolean(v);
                    }

                    if (roleId > 0)
                    {
                        using var r = new MySqlCommand(
                            "SELECT RoleName, IsActive FROM TblRoles WHERE RoleID=@Rid LIMIT 1", conn2);
                        r.Parameters.AddWithValue("@Rid", roleId);
                        using var rd = r.ExecuteReader();
                        if (rd.Read())
                        {
                            roleName = rd["RoleName"]?.ToString() ?? string.Empty;
                            roleIsActive = rd["IsActive"] != DBNull.Value && Convert.ToBoolean(rd["IsActive"]);
                        }
                    }
                }
                catch
                {
                    // Non-fatal: defaults are fail-open (active = true).
                }

                // ── ISSUE JWT VIA ITokenService ────────────────────
                // 15-minute access token with Issuer + Audience that
                // match Program.cs's TokenValidationParameters.
                string token = _tokenService.GenerateAccessToken(
                    request.UserId, email, roleId, roleName);

                // gamification: daily-login + 7-day-streak points & badges
                // (students only). Fire-and-forget in its own DI scope.
                FireLoginGamification(request.UserId, roleId);

                return Ok(new
                {
                    Message = "OTP verified successfully",
                    Token = token,
                    RoleID = roleId,
                    RoleName = roleName,
                    RoleIsActive = roleIsActive,
                    IsActive = userIsActive,
                    Email = email
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = ex.Message });
            }
        }


        // ─────────────────────────────────────────
        // Login gamification (students only): touch the streak, award
        // DAILY_LOGIN, award STREAK_7DAY at a 7-day run, evaluate badges.
        // Runs in its own DI scope; never blocks or breaks login.
        // ─────────────────────────────────────────
        private void FireLoginGamification(int userId, int roleId)
        {
            // RoleID 3 = Student (the gamification / leaderboard audience).
            if (userId <= 0 || roleId != 3) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var gamif = scope.ServiceProvider.GetRequiredService<IGamificationService>();
                    await gamif.HandleLoginAsync(userId);
                }
                catch
                {
                    // gamification must never affect the login flow
                }
            });
        }

        // ─────────────────────────────────────────
        // RESEND OTP (Email + SMS)  —  unchanged
        // ─────────────────────────────────────────
        [HttpPost("resend-otp")]
        public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest request)
        {
            try
            {
                using var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                connection.Open();

                int userId = 0;
                string email = string.Empty;
                string mobileNumber = string.Empty;

                using (var checkCmd = new MySqlCommand(
                    "SELECT ID, Email, MobileNumber FROM TblUserMasters WHERE Email=@Email AND IsActive=1", connection))
                {
                    checkCmd.Parameters.AddWithValue("@Email", request.Email);
                    using var reader = checkCmd.ExecuteReader();
                    if (!reader.Read())
                        return NotFound(new { Message = "User not found" });

                    userId = Convert.ToInt32(reader["ID"]);
                    email = reader["Email"].ToString();
                    mobileNumber = reader["MobileNumber"].ToString();
                }

                using var cmd = new MySqlCommand("Pr_UserLoginWithOTP", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("p_Email", request.Email);
                cmd.Parameters.AddWithValue("p_Password", DBNull.Value);

                var resultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var userIdOut = new MySqlParameter("p_UserId", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var otpOut = new MySqlParameter("p_OTP", MySqlDbType.VarChar, 6) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(resultCode);
                cmd.Parameters.Add(userIdOut);
                cmd.Parameters.Add(otpOut);

                cmd.ExecuteNonQuery();

                if (Convert.ToInt32(resultCode.Value) != 1)
                    return BadRequest(new { Message = "Failed to resend OTP" });

                string otp = otpOut.Value?.ToString();

                var (emailSent, smsSent) = await SendOtpBothAsync(email, mobileNumber, otp);

                return Ok(new
                {
                    Message = BuildDeliveryMessage(emailSent, smsSent),
                    UserId = userId
#if DEBUG
                    ,
                    OTP = otp
#endif
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // FORGOT PASSWORD — REQUEST OTP  —  unchanged
        // ─────────────────────────────────────────
        [HttpPost("request-otp")]
        public async Task<IActionResult> RequestForgotPasswordOtp([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                using var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("Pr_ForgotPasswordRequest", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("p_Email", request.Email);

                var resultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var userId = new MySqlParameter("p_UserId", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
                var otp = new MySqlParameter("p_OTP", MySqlDbType.VarChar, 6) { Direction = ParameterDirection.Output };

                cmd.Parameters.Add(resultCode);
                cmd.Parameters.Add(userId);
                cmd.Parameters.Add(otp);

                connection.Open();
                cmd.ExecuteNonQuery();

                if (Convert.ToInt32(resultCode.Value) != 1)
                    return NotFound(new { Message = "User not found or inactive" });

                string mobileNumber = string.Empty;
                using (var mobileCmd = new MySqlCommand(
                    "SELECT MobileNumber FROM TblUserMasters WHERE Email=@Email AND IsActive=1", connection))
                {
                    mobileCmd.Parameters.AddWithValue("@Email", request.Email);
                    mobileNumber = mobileCmd.ExecuteScalar()?.ToString() ?? string.Empty;
                }

                var (emailSent, smsSent) = await SendOtpBothAsync(
                    request.Email, mobileNumber, otp.Value.ToString());

                return Ok(new
                {
                    Message = BuildDeliveryMessage(emailSent, smsSent),
                    UserId = userId.Value
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // RESET PASSWORD  →  BCrypt, no Unsalt
        // ─────────────────────────────────────────
        [HttpPost("reset-password")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (request == null
                || request.UserId <= 0
                || string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest(new { Message = "User id and new password are required." });

            try
            {
                string hashedPassword = PasswordHasher.Hash(request.NewPassword);

                using var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString"));
                using var cmd = new MySqlCommand("Pr_ResetPassword", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("p_UserId", request.UserId);
                cmd.Parameters.AddWithValue("p_NewPassword", hashedPassword);
                cmd.Parameters.AddWithValue("p_Salt", string.Empty);   // BCrypt embeds salt

                var resultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32)
                { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(resultCode);

                connection.Open();
                cmd.ExecuteNonQuery();

                int code = Convert.ToInt32(resultCode.Value);

                return code == 1
                    ? Ok(new { Message = "Password reset successful" })
                    : NotFound(new { Message = "User not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error: " + ex.Message });
            }
        }


        [HttpPost("register-complete")]
        public async Task<IActionResult> RegisterComplete([FromBody] RegisterCompleteRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password)
                || string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.MobileNumber))
                return BadRequest(new { success = false, message = "All required fields must be filled." });

            try
            {
                // ── 1. Hash + insert via SP_CreateandUpdateUser ──
                string passwordHash = ProximaLMSAPI.Security.PasswordHasher.Hash(request.Password);

                int newUserId = 0;
                int roleId = 3;  // Student by default — adjust to your scheme.
                string roleName = "Student";

                using (var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString")))
                {
                    using var cmd = new MySqlCommand("SP_CreateandUpdateUser", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                    cmd.Parameters.AddWithValue("p_ID", 0);
                    cmd.Parameters.AddWithValue("p_Name", request.Name);
                    cmd.Parameters.AddWithValue("p_Gender", request.Gender ?? "");
                    cmd.Parameters.AddWithValue("p_MobileNumber", request.MobileNumber);
                    cmd.Parameters.AddWithValue("p_Email", request.Email);
                    cmd.Parameters.AddWithValue("p_Password", passwordHash);
                    cmd.Parameters.AddWithValue("p_CreatedBy", request.Name);
                    cmd.Parameters.AddWithValue("p_CreatedIP", HttpContext.Connection.RemoteIpAddress?.ToString());
                    cmd.Parameters.AddWithValue("p_ModifiedBy", DBNull.Value);
                    cmd.Parameters.AddWithValue("p_ModifiedIP", DBNull.Value);
                    cmd.Parameters.AddWithValue("p_Salt", string.Empty);   // BCrypt embeds salt
                    cmd.Parameters.AddWithValue("p_IsActive", 1);

                    var resultCode = new MySqlParameter("p_ResultCode", MySqlDbType.Int32)
                    { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(resultCode);

                    connection.Open();
                    cmd.ExecuteNonQuery();

                    int code = Convert.ToInt32(resultCode.Value);
                    if (code == -1)
                        return Conflict(new { success = false, message = "Email or Mobile already exists." });
                    if (code != 1)
                        return BadRequest(new { success = false, message = "Registration failed." });

                    // Fetch the new user's ID + RoleID + RoleName.
                    using var lookCmd = new MySqlCommand(
                        @"SELECT u.ID, u.RoleID, r.RoleName
                    FROM TblUserMasters u
               LEFT JOIN TblRoles r ON r.RoleID = u.RoleID
                   WHERE u.Email = @Email LIMIT 1", connection);
                    lookCmd.Parameters.AddWithValue("@Email", request.Email);
                    using var rd = lookCmd.ExecuteReader();
                    if (rd.Read())
                    {
                        newUserId = Convert.ToInt32(rd["ID"]);
                        roleId = rd["RoleID"] != DBNull.Value ? Convert.ToInt32(rd["RoleID"]) : roleId;
                        roleName = rd["RoleName"] != DBNull.Value ? rd["RoleName"].ToString()! : roleName;
                    }
                }

                // ── 2. Create empty TblUserProfile row (so My Profile page
                //       always has something to load) and store DOB if given.
                using (var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString")))
                {
                    connection.Open();
                    using var cmd = new MySqlCommand("SP_User_SaveProfile", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };
                    cmd.Parameters.AddWithValue("p_UserID", newUserId);
                    cmd.Parameters.AddWithValue("p_DateOfBirth", (object?)request.DateOfBirth ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("p_Bio", "");
                    cmd.Parameters.AddWithValue("p_ProfilePhoto", "");
                    cmd.Parameters.AddWithValue("p_Interests", "");
                    cmd.Parameters.AddWithValue("p_PreferredLanguage", "");
                    cmd.Parameters.AddWithValue("p_Location", "");
                    cmd.Parameters.AddWithValue("p_LinkedInUrl", "");
                    cmd.Parameters.AddWithValue("p_WebsiteUrl", "");
                    var rc = new MySqlParameter("p_ResultCode", MySqlDbType.Int32)
                    { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(rc);
                    cmd.ExecuteNonQuery();
                }

                // ── 2b. Create the streak row so the Student Dashboard
                //        never crashes on a NULL CurrentStreak. ──
                EnsureUserStreakRow(request.Email);

                // ── 3. Issue JWT + refresh token (auto-login) ──
                string accessToken = _tokenService.GenerateAccessToken(
                    newUserId, request.Email, roleId, roleName);

                string refreshToken = _tokenService.GenerateRefreshToken();
                DateTime refreshExpiresAt = DateTime.UtcNow.AddDays(_tokenService.RefreshTokenDays);

                // Persist the refresh token via the existing SP.
                using (var connection = new MySqlConnection(_configuration.GetConnectionString("ConnectionString")))
                {
                    connection.Open();
                    using var cmd = new MySqlCommand("SP_RefreshToken_Create", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };
                    cmd.Parameters.AddWithValue("p_UserID", newUserId);
                    cmd.Parameters.AddWithValue("p_Email", request.Email);
                    cmd.Parameters.AddWithValue("p_RoleID", roleId);
                    cmd.Parameters.AddWithValue("p_Token", refreshToken);
                    cmd.Parameters.AddWithValue("p_ExpiresAt", refreshExpiresAt);
                    cmd.Parameters.AddWithValue("p_CreatedByIp", HttpContext.Connection.RemoteIpAddress?.ToString());

                    var rc = new MySqlParameter("p_ResultCode", MySqlDbType.Int32)
                    { Direction = ParameterDirection.Output };
                    var msg = new MySqlParameter("p_Message", MySqlDbType.VarChar, 500)
                    { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(rc);
                    cmd.Parameters.Add(msg);
                    cmd.ExecuteNonQuery();
                }

                return Ok(new
                {
                    success = true,
                    userId = newUserId,
                    roleId = roleId,
                    roleName = roleName,
                    email = request.Email,
                    token = accessToken,
                    accessExpiresAt = DateTime.UtcNow.AddMinutes(_tokenService.AccessTokenMinutes),
                    refreshToken = refreshToken,
                    refreshExpiresAt = refreshExpiresAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Error: " + ex.Message });
            }
        }






        // ═══════════════════════════════════════════
        // OTP DELIVERY (unchanged from original)
        // ═══════════════════════════════════════════
        private async Task<(bool emailSent, bool smsSent)> SendOtpBothAsync(
            string email, string mobileNumber, string otp)
        {
            var emailTask = SendOtpEmailAsync(email, otp);
            var smsTask = SendOtpSmsAsync(mobileNumber, otp);

            await Task.WhenAll(emailTask, smsTask);
            return (emailTask.Result, smsTask.Result);
        }

        private static string BuildDeliveryMessage(bool emailSent, bool smsSent)
        {
            if (emailSent && smsSent) return "OTP sent to your email and mobile";
            if (emailSent) return "OTP sent to your email (SMS delivery failed)";
            if (smsSent) return "OTP sent to your mobile (Email delivery failed)";
            return "OTP generated but delivery failed on all channels";
        }

        private async Task<bool> SendOtpEmailAsync(string toEmail, string otp)
        {
            try
            {
                var s = _configuration.GetSection("EmailSettings");

                using var client = new SmtpClient(s["SmtpHost"], int.Parse(s["SmtpPort"] ?? "587"))
                {
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(s["Username"], s["Password"]),
                    EnableSsl = bool.Parse(s["EnableSsl"] ?? "true"),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(s["SenderEmail"], s["SenderName"] ?? "ProximaLMS"),
                    Subject = $"\U0001f510 {otp} \u2013 Your ProximaLMS Login OTP",
                    IsBodyHtml = true
                };

                message.To.Add(toEmail);

                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                    $"Your ProximaLMS OTP is: {otp}\nValid for 15 minutes. Do not share this code.",
                    null, "text/plain"));

                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                    BuildOtpEmailHtml(otp), null, "text/html"));

                await client.SendMailAsync(message);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email Error] {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SendOtpSmsAsync(string mobileNumber, string otp)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mobileNumber))
                    return false;

                var s = _configuration.GetSection("SmsSettings");

                string username = s["Username"] ?? "";
                string password = s["Password"] ?? "";
                string senderName = s["SenderName"] ?? "KALPRA";
                string routeType = s["RouteType"] ?? "1";
                string message = $"{otp} is your Kalpra Tech verification code and will be valid for the next 15 minutes - KALPRA.";

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    return false;

                string smsUrl = $"https://smsmaa.com/SMS_API/sendsms.php" +
                                $"?username={username}" +
                                $"&password={password}" +
                                $"&mobile={mobileNumber}" +
                                $"&sendername={senderName}" +
                                $"&message={Uri.EscapeDataString(message)}" +
                                $"&routetype={routeType}";

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await http.GetAsync(smsUrl);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SMS Error] {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════
        // OTP email HTML  (unchanged from original)
        // ═══════════════════════════════════════════
        private static string BuildOtpEmailHtml(string otp)
        {
            // (left intact — this template is large and visual; replace
            //  with the previous file's BuildOtpEmailHtml body verbatim
            //  if you want every pixel to stay identical.)
            return $@"<!doctype html><html><body style=""font-family:Arial,sans-serif;background:#0d0f14;padding:24px;color:#fff;"">
<div style=""max-width:480px;margin:auto;background:#1a1d24;border-radius:16px;padding:32px;text-align:center;"">
  <h2 style=""color:#fff;margin:0 0 16px;"">Your ProximaLMS OTP</h2>
  <div style=""font-size:32px;letter-spacing:6px;font-weight:700;color:#7B2CBF;padding:18px 0;"">{otp}</div>
  <p style=""color:rgba(255,255,255,0.65);font-size:14px;line-height:1.6;"">
    Valid for 15 minutes. Don't share this code with anyone.
  </p>
  <p style=""color:rgba(255,255,255,0.3);font-size:12px;margin-top:24px;"">
    &copy; 2026 ProximaLMS &middot; Hyderabad, India
  </p>
</div></body></html>";
        }

        // ═══════════════════════════════════════════
        // REQUEST MODELS  (unchanged)
        // ═══════════════════════════════════════════
        public class RegisterRequest
        {
            public string Name { get; set; }
            public string Gender { get; set; }
            public string MobileNumber { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
            public string? ReferralCode { get; set; }
        }

        public class LoginRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        public class OtpRequest
        {
            public int UserId { get; set; }
            public string OTP { get; set; }
            public string Email { get; set; }
        }

        public class ResendOtpRequest { public string Email { get; set; } }
        public class ForgotPasswordRequest { public string Email { get; set; } }

        public class ForgotPasswordVerifyRequest
        {
            public int UserId { get; set; }
            public string OTP { get; set; }
        }

        public class ResetPasswordRequest
        {
            public int UserId { get; set; }
            public string NewPassword { get; set; }
        }

        public class RegisterOtpRequest
        {
            public string Email { get; set; }
            public string MobileNumber { get; set; }
        }

        public class VerifyRegisterOtpRequest
        {
            public string Email { get; set; }
            public string OTP { get; set; }
        }
        // ────────────────────────────────────────────────────────────
        // ADD THIS DTO at the bottom of the class, alongside RegisterRequest
        // ────────────────────────────────────────────────────────────
        public class RegisterCompleteRequest
        {
            public string Name { get; set; } = "";
            public string? Gender { get; set; }
            public string MobileNumber { get; set; } = "";
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public DateTime? DateOfBirth { get; set; }
        }

    }
}