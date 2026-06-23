// ============================================================
// ProximaLMS/Controllers/InstructorController.cs
// ------------------------------------------------------------
// Tutor-scoped Instructor Panel (Piece 1: shell + dashboard).
//
// Access model — IDENTITY based, not RoleID number based:
//   the panel is available to whoever's login (Session "UserID")
//   is linked to an active row in TblTutorRegistration. This keeps
//   it correct no matter what numeric RoleID "Tutor" ends up being.
//
// Actions:
//   GET  /Instructor                       → Dashboard (stats + my courses)
//   GET  /Instructor/Students/{id}         → roster + progress for one course
//   POST /Instructor/TogglePublish         → AJAX publish/unpublish (guarded)
//
// All course operations are ownership-guarded server-side (API + SP).
// ============================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ProximaLMS.Filters;
using ProximaLMS.Models;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class InstructorController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<InstructorController> _logger;

        public InstructorController(IHttpClientFactory httpClientFactory,
                                    IConfiguration config,
                                    ILogger<InstructorController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        // ── shared HttpClient with the session bearer token ──
        private HttpClient Api()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        // ── resolve the logged-in user to a tutor profile (or null) ──
        private async Task<InstructorIdentity> ResolveTutorAsync(HttpClient client)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (!int.TryParse(userId, out var uid) || uid <= 0)
                return null;

            try
            {
                var resp = await client.GetAsync($"api/instructor/resolve/{uid}");
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<InstructorIdentity>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve tutor for user {User}", uid);
                return null;
            }
        }

        // ═════════════════════════════════════════════════════════
        // GET /Instructor
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var client = Api();
            var tutor = await ResolveTutorAsync(client);
            if (tutor == null)
            {
                // Admins aren't linked to a tutor profile — send them to the
                // admin dashboard rather than the instructor-only area.
                var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
                if (roleId == 1)
                    return RedirectToAction("Index", "Dashboard");

                TempData["Error"] = "This area is for instructors. Your login isn't linked to a tutor profile.";
                return RedirectToAction("Index", "Home");
            }

            var vm = new InstructorDashboardViewModel { Tutor = tutor };

            // stats
            try
            {
                var resp = await client.GetAsync($"api/instructor/stats/{tutor.TutorID}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    vm.Stats = JsonConvert.DeserializeObject<InstructorStats>(json) ?? new InstructorStats();
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "stats load failed"); }

            // courses
            try
            {
                var resp = await client.GetAsync($"api/instructor/courses/{tutor.TutorID}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    vm.Courses = JsonConvert.DeserializeObject<List<InstructorCourseCard>>(json)
                                 ?? new List<InstructorCourseCard>();
                }
                else
                {
                    TempData["Error"] = $"Failed to load your courses: {resp.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "courses load failed");
                TempData["Error"] = "Error loading courses: " + ex.Message;
            }

            // View file is Dashboard.cshtml (there is no Index.cshtml).
            return View("Dashboard", vm);
        }

        // ═════════════════════════════════════════════════════════
        // POST /Instructor/TogglePublish     (AJAX, JSON)
        // Body: { CourseID, IsActive }
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> TogglePublish([FromBody] TogglePublishRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return Json(new { success = false, message = "Session expired." });

            if (req == null || req.CourseID <= 0)
                return Json(new { success = false, message = "Invalid request." });

            var client = Api();
            var tutor = await ResolveTutorAsync(client);
            if (tutor == null)
                return Json(new { success = false, message = "Not an instructor." });

            try
            {
                var payload = new
                {
                    CourseID = req.CourseID,
                    TutorID = tutor.TutorID,   // forced from the session — clients can't spoof ownership
                    IsActive = req.IsActive
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/instructor/course/publish", content);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return Json(new { success = false, message = "Could not update. " + json });

                return Json(new
                {
                    success = true,
                    isActive = req.IsActive,
                    message = req.IsActive ? "Course published." : "Course moved to draft."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TogglePublish failed for course {Course}", req.CourseID);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ═════════════════════════════════════════════════════════
        // GET /Instructor/Students/{id}
        // Roster for one owned course, with each student's progress %
        // merged from the existing SP_Progress_GetSummary endpoint.
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Students(int id)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var client = Api();
            var tutor = await ResolveTutorAsync(client);
            if (tutor == null)
            {
                TempData["Error"] = "This area is for instructors.";
                return RedirectToAction("Index", "Home");
            }

            // 1. ownership + header
            var vm = new InstructorStudentsViewModel { CourseID = id };
            try
            {
                var ownResp = await client.GetAsync($"api/instructor/course/{id}/owned/{tutor.TutorID}");
                if (!ownResp.IsSuccessStatusCode)
                {
                    TempData["Error"] = "That course isn't one of yours.";
                    return RedirectToAction("Index");
                }
                var ownJson = await ownResp.Content.ReadAsStringAsync();
                dynamic header = JsonConvert.DeserializeObject<dynamic>(ownJson);
                vm.CourseTitle = (string)(header?.courseTitle ?? header?.CourseTitle ?? "Course");
                vm.IsActive = (bool)(header?.isActive ?? header?.IsActive ?? false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ownership check failed");
                TempData["Error"] = "Could not verify the course.";
                return RedirectToAction("Index");
            }

            // 2. roster
            try
            {
                var resp = await client.GetAsync($"api/instructor/course/{id}/students/{tutor.TutorID}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    vm.Students = JsonConvert.DeserializeObject<List<InstructorStudentRow>>(json)
                                  ?? new List<InstructorStudentRow>();
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "roster load failed"); }

            // 3. merge progress per student from the existing summary endpoint
            foreach (var s in vm.Students)
            {
                try
                {
                    var resp = await client.GetAsync($"api/progress/summary/{s.StudentID}");
                    if (!resp.IsSuccessStatusCode) continue;

                    var json = await resp.Content.ReadAsStringAsync();
                    var rows = JsonConvert.DeserializeObject<List<ProgressSummaryRow>>(json)
                               ?? new List<ProgressSummaryRow>();

                    var match = rows.FirstOrDefault(r => r.CourseID == id);
                    if (match != null)
                    {
                        s.CompletedContents = match.CompletedContents;
                        s.TotalContents = match.TotalContents;
                        s.ProgressPercent = match.TotalContents > 0
                            ? (int)Math.Min(100, Math.Round(match.CompletedContents * 100.0 / match.TotalContents))
                            : 0;
                    }
                }
                catch { /* non-fatal: leave at 0 */ }
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Revenue()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var client = Api();
            var tutor = await ResolveTutorAsync(client);
            if (tutor == null)
            {
                TempData["Error"] = "This area is for instructors.";
                return RedirectToAction("Index", "Home");
            }

            var vm = new InstructorRevenueViewModel { TutorName = tutor.FullName };

            try
            {
                var resp = await client.GetAsync($"api/instructor/revenue/{tutor.TutorID}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    vm.Courses = Newtonsoft.Json.JsonConvert
                        .DeserializeObject<List<CourseRevenueRow>>(json) ?? new List<CourseRevenueRow>();
                }
                else
                {
                    TempData["Error"] = $"Failed to load revenue: {resp.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "revenue load failed");
                TempData["Error"] = "Error loading revenue: " + ex.Message;
            }

            vm.TotalGross = vm.Courses.Sum(c => c.GrossAmount);
            vm.TotalDiscount = vm.Courses.Sum(c => c.TotalDiscount);
            vm.TotalNet = vm.Courses.Sum(c => c.NetRevenue);
            vm.TotalOrders = vm.Courses.Sum(c => c.PaidOrders);

            return View(vm);
        }

        // ═════════════════════════════════════════════════════════
        // GET /Instructor/CourseOrders?courseId=#   (AJAX, JSON)
        // Paid-order detail for the revenue drill-down. Ownership-guarded
        // at the SP level (tutor id forced from the session).
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> CourseOrders(int courseId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token) || courseId <= 0)
                return Json(new { Status = "Error", Message = "Invalid request." });

            var client = Api();
            var tutor = await ResolveTutorAsync(client);
            if (tutor == null)
                return Json(new { Status = "Error", Message = "Not an instructor." });

            try
            {
                var resp = await client.GetAsync($"api/instructor/revenue/{tutor.TutorID}/course/{courseId}");
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "course orders load failed for {Course}", courseId);
                return Json(new { Status = "Error", Message = ex.Message });
            }
        }

        // GET /Instructor/EnrollmentTrend  (AJAX JSON for dashboard chart)
        [HttpGet]
        public async Task<IActionResult> EnrollmentTrend()
        {
            var client = Api();
            var tutor = await ResolveTutorAsync(client);
            if (tutor == null) return Json(new object[0]);
            try
            {
                var resp = await client.GetAsync($"api/instructor/enrollment-trend/{tutor.TutorID}");
                if (resp.IsSuccessStatusCode) return Content(await resp.Content.ReadAsStringAsync(), "application/json");
            }
            catch { }
            // Fallback: empty 6-month skeleton
            var months = Enumerable.Range(0, 6)
                .Select(i => DateTime.Now.AddMonths(-5 + i).ToString("MMM"))
                .Select(m => new { month = m, count = 0 })
                .ToList();
            return Json(months);
        }

        // GET /Instructor/RecentReviews?limit=N  (AJAX JSON for dashboard)
        [HttpGet]
        public async Task<IActionResult> RecentReviews(int limit = 5)
        {
            var client = Api();
            var tutor = await ResolveTutorAsync(client);
            if (tutor == null) return Json(new object[0]);
            try
            {
                var resp = await client.GetAsync($"api/review/list?tutorId={tutor.TutorID}&pageSize={limit}&page=1");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var root = Newtonsoft.Json.Linq.JToken.Parse(json);
                    if (root is Newtonsoft.Json.Linq.JArray) return Content(json, "application/json");
                    var data = root["data"] ?? root["Data"] ?? root["reviews"] ?? root;
                    return Content(data.ToString(), "application/json");
                }
            }
            catch { }
            return Json(new object[0]);
        }

        public class TogglePublishRequest
        {
            public int CourseID { get; set; }
            public bool IsActive { get; set; }
        }
    }
}
