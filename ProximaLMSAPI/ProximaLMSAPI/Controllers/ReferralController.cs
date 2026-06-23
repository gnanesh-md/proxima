// ============================================================
// ProximaLMSAPI/Controllers/ReferralController.cs
// ------------------------------------------------------------
// Module 05 - Referral engine.
// Endpoints:
//   POST /api/referral/my-code        -> get / create a user's referral code
//   POST /api/referral/validate       -> resolve a referral code to its owner
//   POST /api/referral/register       -> link a new user to a referrer
//   POST /api/referral/grant-rewards  -> reward the referrer (call on 1st purchase)
//   POST /api/referral/my-stats       -> a referrer's stats + referral list
//   GET  /api/referral/leaderboard    -> top referrers
//   GET  /api/referral/policy         -> read referral policy
//   POST /api/referral/policy/save    -> update referral policy
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReferralController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ReferralController> _logger;

        public ReferralController(IConfiguration config, ILogger<ReferralController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ════════════════════════════════════════════════════════
        // POST  api/referral/my-code      Body: { UserID }
        // ════════════════════════════════════════════════════════
        [HttpPost("my-code")]
        public async Task<IActionResult> MyCode([FromBody] ReferralUserApiRequest req)
        {
            if (req == null || req.UserID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_UserID", req.UserID);
                p.Add("p_ReferralCode", dbType: DbType.String, size: 20, direction: ParameterDirection.Output);
                p.Add("p_ResultCode",   dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",      dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Referral_GetOrCreateCode", p,
                    commandType: CommandType.StoredProcedure);

                return Ok(new
                {
                    success      = p.Get<int>("p_ResultCode") == 1,
                    referralCode = p.Get<string>("p_ReferralCode"),
                    message      = p.Get<string>("p_Message")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral my-code failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/referral/validate      Body: { Code }
        // ════════════════════════════════════════════════════════
        [HttpPost("validate")]
        public async Task<IActionResult> Validate([FromBody] ReferralCodeApiRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Code))
                return BadRequest(new { success = false, message = "Referral code is required." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_Code", req.Code.Trim().ToUpper());
                p.Add("p_ReferrerUserID", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_ResultCode",     dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",        dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Referral_Validate", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                return Ok(new
                {
                    success        = code == 1,
                    referrerUserId = p.Get<int?>("p_ReferrerUserID") ?? 0,
                    message        = p.Get<string>("p_Message")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral validate failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/referral/register   Body: { RefereeUserID, ReferralCode }
        // Link a new user to a referrer and issue a welcome coupon.
        // ════════════════════════════════════════════════════════
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] ReferralRegisterApiRequest req)
        {
            if (req == null || req.RefereeUserID <= 0 || string.IsNullOrWhiteSpace(req.ReferralCode))
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_RefereeUserID", req.RefereeUserID);
                p.Add("p_ReferralCode",  req.ReferralCode.Trim().ToUpper());
                p.Add("p_RefereeCouponID", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_ResultCode",      dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",         dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Referral_Register", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg, refereeCouponId = p.Get<int?>("p_RefereeCouponID") ?? 0 })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral register failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/referral/grant-rewards   Body: { UserID }  (the referee)
        // Call from the payment-success path on a user's first purchase.
        // ════════════════════════════════════════════════════════
        [HttpPost("grant-rewards")]
        public async Task<IActionResult> GrantRewards([FromBody] ReferralUserApiRequest req)
        {
            if (req == null || req.UserID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_RefereeUserID", req.UserID);
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Referral_GrantRewards", p,
                    commandType: CommandType.StoredProcedure);

                // ResultCode 0 here just means "nothing pending" - not an error.
                return Ok(new
                {
                    success = p.Get<int>("p_ResultCode") == 1,
                    message = p.Get<string>("p_Message")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral grant-rewards failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/referral/my-stats    Body: { UserID }
        // Returns the summary + the list of people the user referred.
        // ════════════════════════════════════════════════════════
        [HttpPost("my-stats")]
        public async Task<IActionResult> MyStats([FromBody] ReferralUserApiRequest req)
        {
            if (req == null || req.UserID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();

                var summary = await conn.QuerySingleOrDefaultAsync(
                    "SP_Referral_MyStats",
                    new { p_UserID = req.UserID },
                    commandType: CommandType.StoredProcedure);

                var referrals = await conn.QueryAsync(
                    "SP_Referral_MyReferralList",
                    new { p_UserID = req.UserID },
                    commandType: CommandType.StoredProcedure);

                return Ok(new { success = true, summary, referrals });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral my-stats failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // GET  api/referral/leaderboard?topN=50
        // ════════════════════════════════════════════════════════
        [HttpGet("leaderboard")]
        public async Task<IActionResult> Leaderboard([FromQuery] int topN = 50)
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Referral_Leaderboard",
                    new { p_TopN = topN <= 0 ? 50 : topN },
                    commandType: CommandType.StoredProcedure);
                return Ok(new { success = true, data = rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral leaderboard failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // GET  api/referral/policy
        // ════════════════════════════════════════════════════════
        [HttpGet("policy")]
        public async Task<IActionResult> GetPolicy()
        {
            try
            {
                using var conn = CreateConn();
                var row = await conn.QuerySingleOrDefaultAsync(
                    "SP_ReferralPolicy_Get",
                    commandType: CommandType.StoredProcedure);
                return Ok(new { success = true, data = row });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral get-policy failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/referral/policy/save
        // ════════════════════════════════════════════════════════
        [HttpPost("policy/save")]
        public async Task<IActionResult> SavePolicy([FromBody] ReferralPolicyApiRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            var type = (req.RefereeDiscountType ?? "").Trim().ToUpper();
            if (type != "PERCENT" && type != "FLAT")
                return BadRequest(new { success = false, message = "Referee discount type must be PERCENT or FLAT." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_ReferrerPoints",            req.ReferrerPoints);
                p.Add("p_RefereeDiscountType",       type);
                p.Add("p_RefereeDiscountValue",      req.RefereeDiscountValue);
                p.Add("p_RefereeMaxDiscount",        req.RefereeMaxDiscount);
                p.Add("p_RefereeCouponValidityDays", req.RefereeCouponValidityDays <= 0 ? 30 : req.RefereeCouponValidityDays);
                p.Add("p_IsActive",                  req.IsActive ? 1 : 0);
                p.Add("p_ActionBy",                  string.IsNullOrWhiteSpace(req.ActionBy) ? "Admin" : req.ActionBy);
                p.Add("p_ResultCode", dbType: DbType.Int32,  direction: ParameterDirection.Output);
                p.Add("p_Message",    dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_ReferralPolicy_Save", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral save-policy failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    // ── Request DTOs ──────────────────────────────────────────
    public class ReferralUserApiRequest
    {
        public int UserID { get; set; }
    }

    public class ReferralCodeApiRequest
    {
        public string Code { get; set; } = "";
    }

    public class ReferralRegisterApiRequest
    {
        public int RefereeUserID { get; set; }
        public string ReferralCode { get; set; } = "";
    }

    public class ReferralPolicyApiRequest
    {
        public int ReferrerPoints { get; set; }
        public string RefereeDiscountType { get; set; } = "FLAT";
        public decimal RefereeDiscountValue { get; set; }
        public decimal? RefereeMaxDiscount { get; set; }
        public int RefereeCouponValidityDays { get; set; } = 30;
        public bool IsActive { get; set; } = true;
        public string ActionBy { get; set; } = "Admin";
    }
}
