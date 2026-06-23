// ============================================================
// ProximaLMSAPI/Controllers/WishlistController.cs
// ------------------------------------------------------------
// Student course wishlist.
// Endpoints:
//   POST /api/wishlist/toggle        → add / remove a course
//   GET  /api/wishlist/{studentId}   → full wishlisted course list
//   GET  /api/wishlist/ids/{id}      → just the wishlisted CourseIDs
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WishlistController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<WishlistController> _logger;

        public WishlistController(IConfiguration config, ILogger<WishlistController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private IDbConnection CreateConn()
            => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ════════════════════════════════════════════════════════
        // POST  api/wishlist/toggle
        // Body: { StudentID, CourseID }
        // Returns: { success, wishlisted, message }
        // ════════════════════════════════════════════════════════
        [HttpPost("toggle")]
        public async Task<IActionResult> Toggle([FromBody] WishlistToggleApiRequest req)
        {
            if (req == null || req.StudentID <= 0 || req.CourseID <= 0)
                return BadRequest(new { success = false, message = "Invalid request." });

            try
            {
                using var conn = CreateConn();
                conn.Open();

                var p = new DynamicParameters();
                p.Add("p_StudentID", req.StudentID);
                p.Add("p_CourseID", req.CourseID);
                p.Add("p_Wishlisted", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("p_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_Wishlist_Toggle", p,
                    commandType: CommandType.StoredProcedure);

                int code = p.Get<int>("p_ResultCode");
                int wishlisted = p.Get<int>("p_Wishlisted");
                string msg = p.Get<string>("p_Message") ?? "";

                return code == 1
                    ? Ok(new { success = true, wishlisted = wishlisted == 1, message = msg })
                    : BadRequest(new { success = false, message = msg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling wishlist");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/wishlist/{studentId}
        // Full list of wishlisted courses (card shape + IsAssigned).
        // ════════════════════════════════════════════════════════
        [HttpGet("{studentId:int}")]
        public async Task<IActionResult> GetForStudent(int studentId)
        {
            if (studentId <= 0)
                return BadRequest(new { success = false, message = "Invalid student." });

            try
            {
                using var conn = CreateConn();
                var rows = await conn.QueryAsync(
                    "SP_Wishlist_GetForStudent",
                    new { p_StudentID = studentId },
                    commandType: CommandType.StoredProcedure);

                return Ok(rows.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading wishlist");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


        // ════════════════════════════════════════════════════════
        // GET  api/wishlist/ids/{studentId}
        // Lightweight — just the CourseIDs the student wishlisted.
        // Used by the Browse page to mark hearts.
        // ════════════════════════════════════════════════════════
        [HttpGet("ids/{studentId:int}")]
        public async Task<IActionResult> GetIds(int studentId)
        {
            if (studentId <= 0)
                return Ok(Array.Empty<int>());

            try
            {
                using var conn = CreateConn();
                var ids = await conn.QueryAsync<int>(
                    "SELECT CourseID FROM TblCourseWishlist WHERE StudentID = @s;",
                    new { s = studentId });

                return Ok(ids.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading wishlist ids");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }


    // ── Request DTO ───────────────────────────────────────────
    public class WishlistToggleApiRequest
    {
        public int StudentID { get; set; }
        public int CourseID { get; set; }
    }
}