// ============================================================
// ProximaLMSAPI/Controllers/CouponController.cs
// ------------------------------------------------------------
// Module 05 - Coupon engine.
// Endpoints (all POST unless noted):
//   GET  /api/coupon/list                -> all admin coupons
//   GET  /api/coupon/{id}                -> single coupon
//   POST /api/coupon/save                -> insert / update
//   POST /api/coupon/toggle-status       -> enable / disable
//   POST /api/coupon/delete              -> delete (blocked if used)
//   POST /api/coupon/validate            -> validate a code at checkout
//   POST /api/coupon/record-usage        -> log a redemption after payment
//   GET  /api/coupon/usage-report        -> redemption analytics
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CouponController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<CouponController> _logger;

        public CouponController(IConfiguration config, ILogger<CouponController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));

        // ════════════════════════════════════════════════════════
        // GET  api/coupon/list
        // ════════════════════════════════════════════════════════
        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Coupon_GetAll",
                    commandType: CommandType.StoredProcedure);
                return Ok(new { success = true, data = rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon list failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // GET  api/coupon/{id}
        // ════════════════════════════════════════════════════════
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                using var conn = CreateConn();
                var row = await conn.QuerySingleOrDefaultAsync(
                    "SP_Coupon_GetById",
                    new { p_CouponID = id },
                    commandType: CommandType.StoredProcedure);

                if (row == null)
                    return NotFound(new { success = false, message = "Coupon not found." });

                return Ok(new { success = true, data = row });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon get-by-id failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/coupon/save
        // ════════════════════════════════════════════════════════
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] CouponSaveApiRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Code))
                return BadRequest(new { success = false, message = "Coupon code is required." });

            var type = (req.DiscountType ?? "").Trim().ToUpper();
            if (type != "PERCENT" && type != "FLAT")
                return BadRequest(new { success = false, message = "Discount type must be PERCENT or FLAT." });

            if (req.DiscountValue <= 0)
                return BadRequest(new { success = false, message = "Discount value must be greater than zero." });

            if (type == "PERCENT" && req.DiscountValue > 100)
                return BadRequest(new { success = false, message = "A percentage discount cannot exceed 100." });

            if (req.EndDate <= req.StartDate)
                return BadRequest(new { success = false, message = "End date must be after the start date." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CouponID",          req.CouponID);
                p.Add("p_Code",              req.Code.Trim().ToUpper());
                p.Add("p_DiscountType",      type);
                p.Add("p_DiscountValue",     req.DiscountValue);
                p.Add("p_MaxDiscountAmount", req.MaxDiscountAmount);
                p.Add("p_MinOrderAmount",    req.MinOrderAmount);
                p.Add("p_CourseID",          req.CourseID);          // null = global
                p.Add("p_StartDate",         req.StartDate);
                p.Add("p_EndDate",           req.EndDate);
                p.Add("p_MaxUses",           req.MaxUses);
                p.Add("p_MaxUsesPerUser",    req.MaxUsesPerUser <= 0 ? 1 : req.MaxUsesPerUser);
                p.Add("p_IsActive",          req.IsActive ? 1 : 0);
                p.Add("p_ActionBy",          string.IsNullOrWhiteSpace(req.ActionBy) ? "Admin" : req.ActionBy);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Coupon_Save", p, commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon save failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/coupon/toggle-status
        // ════════════════════════════════════════════════════════
        [HttpPost("toggle-status")]
        public async Task<IActionResult> ToggleStatus([FromBody] CouponToggleApiRequest req)
        {
            if (req == null || req.CouponID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CouponID", req.CouponID);
                p.Add("p_IsActive", req.IsActive ? 1 : 0);
                p.Add("p_ActionBy", string.IsNullOrWhiteSpace(req.ActionBy) ? "Admin" : req.ActionBy);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Coupon_ToggleStatus", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon toggle failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/coupon/delete
        // ════════════════════════════════════════════════════════
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] CouponDeleteApiRequest req)
        {
            if (req == null || req.CouponID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CouponID", req.CouponID);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Coupon_Delete", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon delete failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/coupon/validate
        // Body: { Code, UserID, CourseID, OrderAmount }
        // Returns the computed discount when the code is valid.
        // ════════════════════════════════════════════════════════
        [HttpPost("validate")]
        public async Task<IActionResult> Validate([FromBody] CouponValidateApiRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Code)
                || req.UserID <= 0 || req.CourseID <= 0 || req.OrderAmount <= 0)
                return BadRequest(new { success = false, message = "Invalid coupon request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_Code",        req.Code.Trim().ToUpper());
                p.Add("p_UserID",      req.UserID);
                p.Add("p_CourseID",    req.CourseID);
                p.Add("p_OrderAmount", req.OrderAmount);
                p.Add("p_CouponID",       dbType: DbType.Int32,   direction: ParameterDirection.Output);
                p.Add("p_DiscountType",   dbType: DbType.String,  size: 10,  direction: ParameterDirection.Output);
                p.Add("p_DiscountValue",  dbType: DbType.Decimal, direction: ParameterDirection.Output);
                p.Add("p_DiscountAmount", dbType: DbType.Decimal, direction: ParameterDirection.Output);
                p.Add("p_FinalAmount",    dbType: DbType.Decimal, direction: ParameterDirection.Output);
                p.Add("p_ResultCode",     dbType: DbType.Int32,   direction: ParameterDirection.Output);
                p.Add("p_Message",        dbType: DbType.String,  size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Coupon_Validate", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                if (code != 1)
                    return Ok(new { success = false, message = msg });

                return Ok(new
                {
                    success        = true,
                    message        = msg,
                    couponId       = p.Get<int>("p_CouponID"),
                    discountType   = p.Get<string>("p_DiscountType"),
                    discountValue  = p.Get<decimal>("p_DiscountValue"),
                    discountAmount = p.Get<decimal>("p_DiscountAmount"),
                    finalAmount    = p.Get<decimal>("p_FinalAmount")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon validate failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // POST  api/coupon/record-usage
        // Call this AFTER a verified payment that used a coupon.
        // ════════════════════════════════════════════════════════
        [HttpPost("record-usage")]
        public async Task<IActionResult> RecordUsage([FromBody] CouponUsageApiRequest req)
        {
            if (req == null || req.CouponID <= 0 || req.UserID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_CouponID",        req.CouponID);
                p.Add("p_UserID",          req.UserID);
                p.Add("p_CourseID",        req.CourseID);
                p.Add("p_OrderID",         req.OrderID);
                p.Add("p_OrderAmount",     req.OrderAmount);
                p.Add("p_DiscountApplied", req.DiscountApplied);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Coupon_RecordUsage", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon record-usage failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════
        // GET  api/coupon/usage-report
        // ════════════════════════════════════════════════════════
        [HttpGet("usage-report")]
        public async Task<IActionResult> UsageReport()
        {
            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Coupon_UsageReport",
                    commandType: CommandType.StoredProcedure);
                return Ok(new { success = true, data = rows });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coupon usage-report failed");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    // ── Request DTOs ──────────────────────────────────────────
    public class CouponSaveApiRequest
    {
        public int CouponID { get; set; }
        public string Code { get; set; } = "";
        public string DiscountType { get; set; } = "PERCENT";
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public decimal MinOrderAmount { get; set; }
        public int? CourseID { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int MaxUses { get; set; }
        public int MaxUsesPerUser { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public string ActionBy { get; set; } = "Admin";
    }

    public class CouponToggleApiRequest
    {
        public int CouponID { get; set; }
        public bool IsActive { get; set; }
        public string ActionBy { get; set; } = "Admin";
    }

    public class CouponDeleteApiRequest
    {
        public int CouponID { get; set; }
    }

    public class CouponValidateApiRequest
    {
        public string Code { get; set; } = "";
        public int UserID { get; set; }
        public int CourseID { get; set; }
        public decimal OrderAmount { get; set; }
    }

    public class CouponUsageApiRequest
    {
        public int CouponID { get; set; }
        public int UserID { get; set; }
        public int? CourseID { get; set; }
        public string? OrderID { get; set; }
        public decimal OrderAmount { get; set; }
        public decimal DiscountApplied { get; set; }
    }
}
