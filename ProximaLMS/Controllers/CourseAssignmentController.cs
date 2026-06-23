using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProximaLMS.Filters;
using ProximaLMS.Models;
using ProximaLMS.Services;
using System.Net.Http.Headers;
using System.Text;

namespace ProximaLMS.Controllers
{
    [RequireJwt]
    public class CourseAssignmentController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<CourseAssignmentController> _logger;
        private readonly IEmailService _email;

        public CourseAssignmentController(IHttpClientFactory httpClientFactory,
                                          IConfiguration config,
                                          ILogger<CourseAssignmentController> logger,
                                          IEmailService email)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
            _email = email;
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

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            var token = HttpContext.Session.GetString("JwtToken");
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private string CurrentUser =>
            HttpContext.Session.GetString("Email") ?? "Admin";

        private int CurrentRoleId =>
            HttpContext.Session.GetInt32("RoleID") ?? 0;

        // ─────────────────────────────────────────
        // GET /CourseAssignment/Index
        // Shows all students — Admin picks one to assign courses
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync("api/master/students");
                var json = await resp.Content.ReadAsStringAsync();

                var students = new List<StudentViewModel>();

                var token = JToken.Parse(json);
                if (token.Type == JTokenType.Array)
                    students = JsonConvert.DeserializeObject<List<StudentViewModel>>(json) ?? students;
                else
                    students = token["data"]?.ToObject<List<StudentViewModel>>() ?? students;

                return View(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading students");
                TempData["Error"] = "Error loading students.";
                return View(new List<StudentViewModel>());
            }
        }

        // ─────────────────────────────────────────
        // GET /CourseAssignment/Assign/{studentId}
        // Shows all courses with checkboxes for a student
        // ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Assign(int id)
        {
            if (CurrentRoleId != 1)
                return RedirectToAction("List", "Courses");

            try
            {
                using var client = CreateClient();

                // Get student info
                var studentResp = await client.GetAsync($"api/courseassignment/student/{id}");
                var studentJson = await studentResp.Content.ReadAsStringAsync();
                var studentToken = JToken.Parse(studentJson);
                var studentData  = studentToken["data"] ?? studentToken;
                var student = studentData.ToObject<StudentViewModel>() ?? new StudentViewModel { ID = id };

                // Get all courses with IsAssigned flag for this student
                var coursesResp = await client.GetAsync($"api/courseassignment/courses/{id}");
                var coursesJson = await coursesResp.Content.ReadAsStringAsync();
                var courses = ParseList<CourseAssignmentItem>(coursesJson);

                var vm = new StudentCourseAssignViewModel
                {
                    StudentID   = id,
                    StudentName = student.Name ?? "",
                    StudentEmail= student.Email ?? "",
                    Courses     = courses
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading course assignment for student {Id}", id);
                TempData["Error"] = "Error loading courses.";
                return RedirectToAction("Index");
            }
        }

        // ─────────────────────────────────────────
        // POST /CourseAssignment/AssignCourse (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> AssignCourse([FromBody] AssignCourseRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new
                {
                    StudentID  = req.StudentID,
                    CourseID   = req.CourseID,
                    AssignedBy = CurrentUser
                };

                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/courseassignment/assign", content);
                var json = await resp.Content.ReadAsStringAsync();
                var result = ParseApiResponse(json);

                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST /CourseAssignment/RemoveCourse (AJAX)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> RemoveCourse([FromBody] AssignCourseRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new
                {
                    StudentID  = req.StudentID,
                    CourseID   = req.CourseID,
                    AssignedBy = CurrentUser
                };

                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/courseassignment/remove", content);
                var json = await resp.Content.ReadAsStringAsync();
                var result = ParseApiResponse(json);

                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // POST /CourseAssignment/SaveAll (AJAX — save all at once)
        // ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SaveAll([FromBody] SaveAllAssignmentsRequest req)
        {
            if (CurrentRoleId != 1)
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var payload = new
                {
                    StudentID   = req.StudentID,
                    CourseIDs   = req.CourseIDs,
                    AssignedBy  = CurrentUser
                };

                using var client = CreateClient();
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");

                var resp = await client.PostAsync("api/courseassignment/save-all", content);
                var json = await resp.Content.ReadAsStringAsync();
                var result = ParseApiResponse(json);

                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────
        private List<T> ParseList<T>(string json)
        {
            try
            {
                var token = JToken.Parse(json);
                if (token.Type == JTokenType.Array)
                    return token.ToObject<List<T>>() ?? new List<T>();
                return token["data"]?.ToObject<List<T>>()
                    ?? token["result"]?.ToObject<List<T>>()
                    ?? new List<T>();
            }
            catch { return new List<T>(); }
        }

        private (bool Success, string Message) ParseApiResponse(string json)
        {
            try
            {
                var token = JToken.Parse(json);
                bool success = token["success"]?.Value<bool>()
                            ?? token["Success"]?.Value<bool>()
                            ?? false;
                string msg   = token["message"]?.Value<string>()
                            ?? token["Message"]?.Value<string>()
                            ?? "";
                return (success, msg);
            }
            catch { return (false, "Unexpected response."); }
        }

        [HttpPost]
        public async Task<IActionResult> BulkAssign([FromBody] BulkAssignMvcRequest req)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return Json(new { success = false, message = "Session expired." });

            if (req == null
                || req.StudentIDs == null || req.StudentIDs.Count == 0
                || req.CourseIDs == null || req.CourseIDs.Count == 0)
                return Json(new { success = false, message = "Pick at least one student and one course." });

            try
            {
                var assignedBy = HttpContext.Session.GetString("Email") ?? "Admin";

                var payload = new
                {
                    StudentIDs = req.StudentIDs,
                    CourseIDs = req.CourseIDs,
                    AssignedBy = assignedBy,
                    DueDate = req.DueDate,
                    IsMandatory = req.IsMandatory,
                    Note = req.Note
                };

                var http = new StringContent(JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json");
                var resp = await Api().PostAsync("api/courseassignment/bulk-assign", http);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return Json(new { success = false, message = "Bulk assign failed.", details = json });

                // parse just enough to send emails — the JSON itself is returned to the UI
                int notified = 0;
                try
                {
                    var obj = JObject.Parse(json);
                    var newOnes = obj["newAssignments"] as JArray;
                    if (newOnes != null && newOnes.Count > 0 && req.SendEmails)
                    {
                        // Fire-and-forget — never block the response on SMTP latency.
                        // Same pattern as PaymentController's coupon/referral grants.
                        var portalUrl = _config["PortalUrl"] ?? Url.Action("Index", "Home", null, Request.Scheme) ?? "/";
                        var assigner = assignedBy;
                        var dueDate = req.DueDate;
                        var mandatory = req.IsMandatory;

                        _ = Task.Run(async () =>
                        {
                            foreach (var item in newOnes)
                            {
                                try
                                {
                                    var email = (string)item["studentEmail"];
                                    var title = (string)item["courseTitle"];
                                    if (string.IsNullOrWhiteSpace(email)) continue;

                                    var built = AssignmentEmailTemplate.CourseAssigned(
                                        email, title, dueDate, mandatory, assigner, portalUrl);

                                    await _email.SendAsync(email, built.Subject, built.PlainBody, built.HtmlBody);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Assignment-email send failed (non-fatal).");
                                }
                            }
                        });

                        notified = newOnes.Count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not parse bulk-assign response to send emails (non-fatal).");
                }

                // pass the API response through, plus how many emails we queued
                var passthrough = JObject.Parse(json);
                passthrough["emailsQueued"] = notified;
                return Content(passthrough.ToString(), "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk assign failed");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ═════════════════════════════════════════════════════════
        // GET /CourseAssignment/TeamCompletion
        // Manager's team completion report.
        // Manager id = logged-in user (Session UserID).
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> TeamCompletion()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var uid = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(token) || !int.TryParse(uid, out var managerId))
                return Json(new { success = false, message = "Session expired." });

            try
            {
                var resp = await Api().GetAsync($"api/courseassignment/team-completion/{managerId}");
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "team-completion fetch failed");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ═════════════════════════════════════════════════════════
        // GET /CourseAssignment/ForUser/{userId}
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> ForUser(int userId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token) || userId <= 0)
                return Json(new { success = false, message = "Invalid request." });

            try
            {
                var resp = await Api().GetAsync($"api/courseassignment/for-user/{userId}");
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "for-user fetch failed");
                return Json(new { success = false, message = ex.Message });
            }
        }


        // ═════════════════════════════════════════════════════════
        // GET /CourseAssignment/CanManage
        // Capability check for the logged-in user. Returns
        //   { canManage: bool, teamSize: int }
        // Used by the sidebar / panel to decide whether to show the
        // manager UI in pieces 3 and 4.
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> CanManage()
        {
            var uid = HttpContext.Session.GetString("UserID");
            if (!int.TryParse(uid, out var userId))
                return Json(new { canManage = false, teamSize = 0 });

            try
            {
                var resp = await Api().GetAsync($"api/managerteam/has-team/{userId}");
                if (!resp.IsSuccessStatusCode)
                    return Json(new { canManage = false, teamSize = 0 });

                var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                int size = obj["teamSize"]?.Value<int>() ?? 0;
                return Json(new { canManage = size > 0, teamSize = size });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CanManage check failed");
                return Json(new { canManage = false, teamSize = 0 });
            }
        }
        [HttpGet]
        public async Task<IActionResult> Bulk()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var vm = new BulkAssignPageViewModel
            {
                UserName = HttpContext.Session.GetString("Email") ?? "Manager"
            };

            // capability check — passed to the view so it can adapt copy
            try
            {
                var uid = HttpContext.Session.GetString("UserID");
                if (int.TryParse(uid, out var userId))
                {
                    var resp = await Api().GetAsync($"api/managerteam/has-team/{userId}");
                    if (resp.IsSuccessStatusCode)
                    {
                        var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        vm.TeamSize = obj["teamSize"]?.Value<int>() ?? 0;
                        vm.IsManager = vm.TeamSize > 0;
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "capability check failed"); }

            return View(vm);
        }


        // ── 2. Assignee list (JSON) ───────────────────────────────────────────────
        //      If the user manages a team → return team members.
        //      Otherwise (admin) → return all active students from api/student/list.
        [HttpGet]
        public async Task<IActionResult> AssigneeList()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var uid = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(token) || !int.TryParse(uid, out var userId))
                return Json(new List<object>());

            try
            {
                // is this user a manager?
                var capResp = await Api().GetAsync($"api/managerteam/has-team/{userId}");
                int teamSize = 0;
                if (capResp.IsSuccessStatusCode)
                {
                    var cap = JObject.Parse(await capResp.Content.ReadAsStringAsync());
                    teamSize = cap["teamSize"]?.Value<int>() ?? 0;
                }

                if (teamSize > 0)
                {
                    // team members only
                    var resp = await Api().GetAsync($"api/managerteam/members/{userId}");
                    var json = await resp.Content.ReadAsStringAsync();
                    return Content(json, "application/json");
                }
                else
                {
                    // admin path — all active students. api/master/students returns
                    // { success, count, data:[...] }; the picker expects a plain array.
                    var resp = await Api().GetAsync("api/master/students");
                    var json = await resp.Content.ReadAsStringAsync();
                    try
                    {
                        var arr = JObject.Parse(json)["data"] as JArray ?? new JArray();
                        return Content(arr.ToString(), "application/json");
                    }
                    catch
                    {
                        return Content(json, "application/json");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AssigneeList failed");
                return Json(new List<object>());
            }
        }

        // GET /CourseAssignment/Courses — JSON proxy for the bulk picker.
        // The page must NOT call the API host directly (relative /api/... hits
        // the MVC server). This forwards to the API course list.
        [HttpGet]
        public async Task<IActionResult> Courses()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token)) return Json(new List<object>());
            try
            {
                var resp = await Api().GetAsync("api/course/list");
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk course list failed");
                return Json(new List<object>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveAll([FromBody] JObject body)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return Json(new { success = false, message = "Session expired." });

            try
            {
                body["AssignedBy"] = HttpContext.Session.GetString("Email") ?? "Admin";

                var http = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var resp = await Api().PostAsync("api/courseassignment/save-all", http);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveAll failed");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Report()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Index", "Home");

            var vm = new BulkAssignPageViewModel
            {
                UserName = HttpContext.Session.GetString("Email") ?? "Manager"
            };

            // capability check — page copy adapts (manager vs admin)
            try
            {
                var uid = HttpContext.Session.GetString("UserID");
                if (int.TryParse(uid, out var userId))
                {
                    var resp = await Api().GetAsync($"api/managerteam/has-team/{userId}");
                    if (resp.IsSuccessStatusCode)
                    {
                        var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        vm.TeamSize = obj["teamSize"]?.Value<int>() ?? 0;
                        vm.IsManager = vm.TeamSize > 0;
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Report capability check failed"); }

            // Admins (RoleID 1) see completion across ALL assignments, even
            // without a manager team — so the report is never a dead end.
            var roleId = HttpContext.Session.GetInt32("RoleID") ?? 0;
            vm.IsAdminView = roleId == 1;

            return View(vm);
        }

        // GET /CourseAssignment/AllCompletion — JSON proxy, admin-wide completion.
        [HttpGet]
        public async Task<IActionResult> AllCompletion()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
                return Json(new { success = false, message = "Session expired." });
            try
            {
                var resp = await Api().GetAsync("api/courseassignment/all-completion");
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "all-completion fetch failed");
                return Json(new { success = false, message = ex.Message });
            }
        }


    }

    // ── VIEW MODELS ────────────────────────────────────────────



}
