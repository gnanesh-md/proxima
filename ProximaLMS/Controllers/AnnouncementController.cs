// ============================================================
// ProximaLMS/Controllers/AnnouncementController.cs
// ------------------------------------------------------------
// Serves the per-course announcements page and proxies its AJAX
// calls to the API (forwarding the session bearer token).
//
//   GET  /Announcement/Manage?courseId=#  → page
//   GET  /Announcement/List?courseId=#     → JSON
//   POST /Announcement/Save                → create/update (Actor injected)
//   POST /Announcement/Delete              → soft delete
//   POST /Announcement/TogglePin           → pin/unpin
// ============================================================
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using ProximaLMS.Filters;
using ProximaLMS.Models;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class AnnouncementController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<AnnouncementController> _logger;

        public AnnouncementController(IHttpClientFactory httpClientFactory,
                                      IConfiguration config,
                                      ILogger<AnnouncementController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        private HttpClient Api()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private async Task<IActionResult> ForwardGet(string path)
        {
            try
            {
                var resp = await Api().GetAsync(path);
                return Content(await resp.Content.ReadAsStringAsync(), "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET forward failed {Path}", path);
                return Json(new { Status = "Error", Message = ex.Message });
            }
        }

        private async Task<IActionResult> ForwardPost(string path, JObject body)
        {
            try
            {
                var http = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var resp = await Api().PostAsync(path, http);
                return Content(await resp.Content.ReadAsStringAsync(), "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POST forward failed {Path}", path);
                return Json(new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Manage(int courseId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token) || courseId <= 0)
                return RedirectToAction("Index", "Home");

            var vm = new ExamBuilderPageViewModel  // reused: just { CourseID, CourseTitle }
            {
                CourseID = courseId,
                CourseTitle = $"Course #{courseId}"
            };

            try
            {
                var resp = await Api().GetAsync($"api/course/details/{courseId}");
                if (resp.IsSuccessStatusCode)
                {
                    var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    var title = obj["course"]?["courseTitle"]?.ToString()
                                ?? obj["course"]?["CourseTitle"]?.ToString();
                    if (!string.IsNullOrEmpty(title)) vm.CourseTitle = title;
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "title fetch failed"); }

            return View(vm);
        }

        [HttpGet]
        public Task<IActionResult> List(int courseId) => ForwardGet($"api/announcement/by-course/{courseId}");

        [HttpPost]
        public Task<IActionResult> Save([FromBody] JObject body)
        {
            body["Actor"] = HttpContext.Session.GetString("Email") ?? "Admin";
            return ForwardPost("api/announcement/save", body);
        }

        [HttpPost]
        public Task<IActionResult> Delete([FromBody] JObject body) => ForwardPost("api/announcement/delete", body);

        [HttpPost]
        public Task<IActionResult> TogglePin([FromBody] JObject body) => ForwardPost("api/announcement/toggle-pin", body);
    }
}
