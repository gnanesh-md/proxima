// ============================================================
// ProximaLMS/Controllers/SkillLevelController.cs
// ------------------------------------------------------------
// Admin-only skill level master. Proxies to the API.
//
//   GET  /SkillLevel               → admin list
//   GET  /SkillLevel/GetOne?id=N   → AJAX fetch for edit
//   POST /SkillLevel/Save          → AJAX insert/update
//   POST /SkillLevel/ToggleStatus  → AJAX
//   POST /SkillLevel/Delete        → AJAX
// ============================================================
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ProximaLMS.Filters;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class SkillLevelController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<SkillLevelController> _logger;

        public SkillLevelController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<SkillLevelController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var token  = HttpContext.Session.GetString("JwtToken");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private bool IsAdmin() => (HttpContext.Session.GetInt32("RoleID") ?? 0) == 1;


        // ─────────────────────────────────────────────────────────
        // GET /SkillLevel
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "You don't have permission to manage skill levels.";
                return RedirectToAction("List", "Courses");
            }

            var vm = new SkillLevelListViewModel();

            try
            {
                var client = CreateClient();
                var resp   = await client.GetAsync("api/skilllevel/all?includeInactive=1");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    vm.Levels = JsonConvert.DeserializeObject<List<SkillLevelItem>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading skill levels");
                TempData["Error"] = "Could not load skill levels.";
            }

            return View(vm);
        }


        // ─────────────────────────────────────────────────────────
        // GET /SkillLevel/GetOne?id=N
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetOne(int id)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (id <= 0)
                return Json(new { success = false, message = "Invalid id." });

            try
            {
                var client = CreateClient();
                var resp   = await client.GetAsync($"api/skilllevel/{id}");
                var json   = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return Json(new { success = false, message = "Not found." });

                var data = JsonConvert.DeserializeObject<SkillLevelItem>(json);
                return Json(new { success = true, level = data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching skill level {Id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /SkillLevel/Save
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaveSkillLevelRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || string.IsNullOrWhiteSpace(req.LevelName))
                return Json(new { success = false, message = "Level name is required." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new
                {
                    req.LevelID,
                    LevelName   = req.LevelName.Trim(),
                    req.Description,
                    req.ColorHex,
                    req.SortOrder,
                    req.IsActive,
                    ActorEmail = email
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/skilllevel/save", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving skill level");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /SkillLevel/ToggleStatus
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleStatus([FromBody] LevelIdRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.Id <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new { LevelID = req.Id, ActorEmail = email };
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/skilllevel/toggle-status", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling skill level status");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /SkillLevel/Delete
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] LevelIdRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.Id <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = CreateClient();
                var payload = new { LevelID = req.Id };
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/skilllevel/delete", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting skill level");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }


    // ════════════════════════════════════════════════════════════
    //  VIEW MODELS
    // ════════════════════════════════════════════════════════════
    public class SkillLevelListViewModel
    {
        public List<SkillLevelItem> Levels { get; set; } = new();
    }

    public class SkillLevelItem
    {
        public int       LevelID     { get; set; }
        public string    LevelName   { get; set; } = "";
        public string?   Description { get; set; }
        public string?   ColorHex    { get; set; }
        public int       SortOrder   { get; set; }
        public bool      IsActive    { get; set; }
        public int       UsageCount  { get; set; }
        public string?   CreatedBy   { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string?   UpdatedBy   { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class SaveSkillLevelRequest
    {
        public int     LevelID     { get; set; }
        public string  LevelName   { get; set; } = "";
        public string? Description { get; set; }
        public string? ColorHex    { get; set; }
        public int     SortOrder   { get; set; }
        public bool    IsActive    { get; set; } = true;
    }

    public class LevelIdRequest
    {
        public int Id { get; set; }
    }
}
