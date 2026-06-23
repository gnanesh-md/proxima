// ============================================================
// ProximaLMS/Controllers/CategoryController.cs
// ------------------------------------------------------------
// Admin-only category master. Proxies to the API.
//
//   GET  /Category                  → admin list (tree)
//   GET  /Category/Save?id=N        → AJAX: fetch single for edit
//   POST /Category/Save             → AJAX: insert or update
//   POST /Category/ToggleStatus     → AJAX
//   POST /Category/Delete           → AJAX
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
    public class CategoryController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<CategoryController> _logger;

        public CategoryController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<CategoryController> logger)
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
        // GET /Category
        // ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
            {
                TempData["Error"] = "You don't have permission to manage categories.";
                return RedirectToAction("List", "Courses");
            }

            var vm = new CategoryListViewModel();

            try
            {
                var client = CreateClient();
                var resp   = await client.GetAsync("api/category/all?includeInactive=1");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    vm.Categories = JsonConvert.DeserializeObject<List<CategoryItem>>(json)
                                    ?? new List<CategoryItem>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading categories");
                TempData["Error"] = "Could not load categories.";
            }

            return View(vm);
        }


        // ─────────────────────────────────────────────────────────
        // GET /Category/GetOne?id=N   (AJAX → JSON for the edit modal)
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
                var resp   = await client.GetAsync($"api/category/{id}");
                var json   = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return Json(new { success = false, message = "Not found." });

                var data = JsonConvert.DeserializeObject<CategoryItem>(json);
                return Json(new { success = true, category = data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching category {Id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /Category/Save     (AJAX → JSON)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaveCategoryRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || string.IsNullOrWhiteSpace(req.CategoryName))
                return Json(new { success = false, message = "Category name is required." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new
                {
                    req.CategoryID,
                    CategoryName     = req.CategoryName.Trim(),
                    ParentCategoryID = req.ParentCategoryID,
                    req.Description,
                    SortOrder        = req.SortOrder,
                    IsActive         = req.IsActive,
                    ActorEmail       = email
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/category/save", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving category");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /Category/ToggleStatus     (AJAX → JSON)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleStatus([FromBody] IdRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.Id <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = CreateClient();
                var email  = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new { CategoryID = req.Id, ActorEmail = email };
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/category/toggle-status", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling category status");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────
        // POST /Category/Delete     (AJAX → JSON)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] IdRequest req)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized" });

            if (req == null || req.Id <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var client = CreateClient();
                var payload = new { CategoryID = req.Id };
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/category/delete", content);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting category");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }


    // ════════════════════════════════════════════════════════════
    //  VIEW MODELS  (kept in the same file for a clean drop-in;
    //  move to /Models if you prefer.)
    // ════════════════════════════════════════════════════════════

    public class CategoryListViewModel
    {
        public List<CategoryItem> Categories { get; set; } = new();
    }

    public class CategoryItem
    {
        public int       CategoryID         { get; set; }
        public string    CategoryName       { get; set; } = "";
        public int?      ParentCategoryID   { get; set; }
        public string?   ParentCategoryName { get; set; }
        public string?   Description        { get; set; }
        public int       SortOrder          { get; set; }
        public bool      IsActive           { get; set; }
        public string?   CreatedBy          { get; set; }
        public DateTime? CreatedDate        { get; set; }
        public string?   UpdatedBy          { get; set; }
        public DateTime? UpdatedDate        { get; set; }
        public int       ChildCount         { get; set; }
    }

    public class SaveCategoryRequest
    {
        public int     CategoryID       { get; set; }
        public string  CategoryName     { get; set; } = "";
        public int     ParentCategoryID { get; set; }
        public string? Description      { get; set; }
        public int     SortOrder        { get; set; }
        public bool    IsActive         { get; set; } = true;
    }

    public class IdRequest
    {
        public int Id { get; set; }
    }
}
