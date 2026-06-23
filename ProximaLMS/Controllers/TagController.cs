// ============================================================
// ProximaLMS/Controllers/TagController.cs
// ------------------------------------------------------------
// Admin-only tag master. Proxies to the API.
//
//   GET  /Tag                 → admin list
//   GET  /Tag/GetOne?id=N     → AJAX fetch for edit
//   POST /Tag/Save            → AJAX insert/update
//   POST /Tag/ToggleStatus    → AJAX
//   POST /Tag/Delete          → AJAX
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
    public class TagController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<TagController> _logger;

        public TagController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<TagController> logger)
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
        // GET /Tag
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "You don't have permission to manage tags.";
                return RedirectToAction("List", "Courses");
            }

            var vm = new TagListViewModel();

            try
            {
                var client = CreateClient();
                var resp   = await client.GetAsync("api/tag/all?includeInactive=1");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    vm.Tags = JsonConvert.DeserializeObject<List<TagItem>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tags");
                TempData["Error"] = "Could not load tags.";
            }

            return View(vm);
        }


        // ─────────────────────────────────────────────────────────
        // GET /Tag/GetOne?id=N
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
                var resp   = await client.GetAsync($"api/tag/{id}");
                var json   = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return Json(new { success = false, message = "Not found." });

                var data = JsonConvert.DeserializeObject<TagItem>(json);
                return Json(new { success = true, tag = data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tag {Id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /Tag/Save
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaveTagRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || string.IsNullOrWhiteSpace(req.TagName))
                return Json(new { success = false, message = "Tag name is required." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new
                {
                    req.TagID,
                    TagName    = req.TagName.Trim(),
                    Slug       = req.Slug,
                    ColorHex   = req.ColorHex,
                    SortOrder  = req.SortOrder,
                    IsActive   = req.IsActive,
                    ActorEmail = email
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/tag/save", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving tag");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /Tag/ToggleStatus
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleStatus([FromBody] TagIdRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.Id <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new { TagID = req.Id, ActorEmail = email };
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/tag/toggle-status", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling tag status");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /Tag/Delete
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] TagIdRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.Id <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = CreateClient();
                var payload = new { TagID = req.Id };
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/tag/delete", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting tag");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }


    // ════════════════════════════════════════════════════════════
    //  VIEW MODELS
    // ════════════════════════════════════════════════════════════
    public class TagListViewModel
    {
        public List<TagItem> Tags { get; set; } = new();
    }

    public class TagItem
    {
        public int       TagID       { get; set; }
        public string    TagName     { get; set; } = "";
        public string    Slug        { get; set; } = "";
        public string?   ColorHex    { get; set; }
        public int       SortOrder   { get; set; }
        public bool      IsActive    { get; set; }
        public int       UsageCount  { get; set; }
        public string?   CreatedBy   { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string?   UpdatedBy   { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class SaveTagRequest
    {
        public int     TagID     { get; set; }
        public string  TagName   { get; set; } = "";
        public string? Slug      { get; set; }
        public string? ColorHex  { get; set; }
        public int     SortOrder { get; set; }
        public bool    IsActive  { get; set; } = true;
    }

    public class TagIdRequest
    {
        public int Id { get; set; }
    }
}
