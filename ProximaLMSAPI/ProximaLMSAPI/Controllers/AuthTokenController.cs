// ============================================================
// ProximaLMSAPI/Controllers/AuthTokenController.cs
// ------------------------------------------------------------
// Refresh-token endpoints. Kept in a SEPARATE controller so it
// does not collide with your existing AuthController.
//
//   POST /api/authtoken/issue-refresh   (called right after verify-otp)
//   POST /api/authtoken/refresh         (called by the MVC app to rotate)
//   POST /api/authtoken/logout          (revokes every refresh token)
//
// Route prefix is "authtoken" — your existing /api/Auth/* routes
// are untouched.
// ============================================================
using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using ProximaLMSAPI.Services;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthTokenController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthTokenController> _logger;

        public AuthTokenController(
            IConfiguration config,
            ITokenService tokenService,
            ILogger<AuthTokenController> logger)
        {
            _config       = config;
            _tokenService = tokenService;
            _logger       = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        private string ClientIp()
            => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";


        // ════════════════════════════════════════════════════════
        // POST  api/authtoken/issue-refresh
        //
        // Called by the MVC app immediately after a successful
        // verify-otp. Creates the FIRST refresh token for the
        // session and returns it together with its expiry.
        //
        // Body:    { UserID, Email, RoleID }
        // Returns: { success, refreshToken, refreshExpiresAt,
        //            accessTokenMinutes }
        // ════════════════════════════════════════════════════════
        [HttpPost("issue-refresh")]
        public async Task<IActionResult> IssueRefresh([FromBody] IssueRefreshRequest req)
        {
            if (req == null || req.UserID <= 0 || string.IsNullOrWhiteSpace(req.Email))
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                string refreshToken = _tokenService.GenerateRefreshToken();
                DateTime expiresAt  = DateTime.UtcNow.AddDays(_tokenService.RefreshTokenDays);

                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_UserID",      req.UserID);
                p.Add("p_Email",       req.Email);
                p.Add("p_RoleID",      req.RoleID);
                p.Add("p_Token",       refreshToken);
                p.Add("p_ExpiresAt",   expiresAt);
                p.Add("p_CreatedByIp", ClientIp());
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_RefreshToken_Create", p,
                    commandType: CommandType.StoredProcedure);

                if (p.Get<int>("p_ResultCode") != 1)
                    return StatusCode(500, new { success = false, message = p.Get<string>("p_Message") });

                return Ok(new
                {
                    success            = true,
                    refreshToken       = refreshToken,
                    refreshExpiresAt   = expiresAt,                       // UTC
                    accessTokenMinutes = _tokenService.AccessTokenMinutes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "issue-refresh failed for user {UserId}", req.UserID);
                return StatusCode(500, new { success = false, message = "Could not issue refresh token." });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/authtoken/refresh
        //
        // Rotation endpoint. Takes the current refresh token,
        // revokes it, issues a NEW access token + NEW refresh
        // token. This is what keeps a user logged in past the
        // 15-minute access-token expiry.
        //
        // Body:    { RefreshToken }
        // Returns: { success, accessToken, accessExpiresAt,
        //            refreshToken, refreshExpiresAt, roleId }
        //
        // Security: if the supplied token is found but ALREADY
        // revoked, that means it was used twice — a sign of theft.
        // Every refresh token for that user is then revoked, and
        // the caller is forced to log in again.
        // ════════════════════════════════════════════════════════
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.RefreshToken))
                return BadRequest(new { success = false, message = "Refresh token is required." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                string newRefresh   = _tokenService.GenerateRefreshToken();
                DateTime newExpires  = DateTime.UtcNow.AddDays(_tokenService.RefreshTokenDays);

                var p = new DynamicParameters();
                p.Add("p_OldToken",     req.RefreshToken);
                p.Add("p_NewToken",     newRefresh);
                p.Add("p_NewExpiresAt", newExpires);
                p.Add("p_CreatedByIp",  ClientIp());
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                p.Add("p_UserID",     dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Email",      dbType: DbType.String, size: 150, direction: ParameterDirection.Output);
                p.Add("p_RoleID",     dbType: DbType.Int32,  direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_RefreshToken_Rotate", p,
                    commandType: CommandType.StoredProcedure);

                int    code   = p.Get<int>("p_ResultCode");
                int    userId = p.Get<int>("p_UserID");
                string email  = p.Get<string>("p_Email") ?? "";
                int    roleId = p.Get<int>("p_RoleID");

                // -2 = token reuse → revoke the whole chain for this user
                if (code == -2 && userId > 0)
                {
                    var rp = new DynamicParameters();
                    rp.Add("p_UserID", userId);
                    rp.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                    rp.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
                    await conn.ExecuteAsync("SP_RefreshToken_RevokeAllForUser", rp,
                        commandType: CommandType.StoredProcedure);

                    _logger.LogWarning("Refresh-token reuse detected for user {UserId} — all tokens revoked.", userId);
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Session is no longer valid. Please log in again."
                    });
                }

                if (code != 1)
                {
                    // -1 not found, -3 expired
                    return Unauthorized(new
                    {
                        success = false,
                        message = p.Get<string>("p_Message") ?? "Refresh token is invalid."
                    });
                }

                // success → mint a new access token
                string accessToken = _tokenService.GenerateAccessToken(userId, email, roleId);
                DateTime accessExp = DateTime.UtcNow.AddMinutes(_tokenService.AccessTokenMinutes);

                return Ok(new
                {
                    success          = true,
                    accessToken      = accessToken,
                    accessExpiresAt  = accessExp,    // UTC
                    refreshToken     = newRefresh,
                    refreshExpiresAt = newExpires,   // UTC
                    roleId           = roleId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "refresh failed");
                return StatusCode(500, new { success = false, message = "Could not refresh the session." });
            }
        }


        // ════════════════════════════════════════════════════════
        // POST  api/authtoken/logout
        //
        // Revokes every refresh token for the user so the session
        // cannot be silently extended after logout.
        //
        // Body: { UserID }
        // ════════════════════════════════════════════════════════
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest req)
        {
            if (req == null || req.UserID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_UserID", req.UserID);
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_RefreshToken_RevokeAllForUser", p,
                    commandType: CommandType.StoredProcedure);

                return Ok(new { success = true, message = "Logged out." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "logout failed for user {UserId}", req.UserID);
                return StatusCode(500, new { success = false, message = "Could not complete logout." });
            }
        }
    }


    // ── Request DTOs ──────────────────────────────────────────
    public class IssueRefreshRequest
    {
        public int    UserID { get; set; }
        public string Email  { get; set; } = "";
        public int    RoleID { get; set; }
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = "";
    }

    public class LogoutRequest
    {
        public int UserID { get; set; }
    }
}
