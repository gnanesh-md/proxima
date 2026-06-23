// ============================================================
// ProximaLMS/Controllers/ExamController.cs   (MVC project)
// ------------------------------------------------------------
// Serves the exam builder / question-bank / take / result pages
// and proxies their AJAX calls to the ProximaLMSAPI exam endpoints
// (forwarding the session bearer token).
//
//   GET  /Exam/Builder?courseId=#          → builder page
//   GET  /Exam/Bank?courseId=#             → question bank page
//   GET  /Exam/Take?examId=#               → student take page
//   GET  /Exam/Result?attemptId=#          → student result page
//   GET  /Exam/List?courseId=#             → exams for a course (JSON)
//   GET  /Exam/Full?examId=#               → exam + questions (JSON)
//   POST /Exam/SaveExam | SaveQuestion | … → proxied JSON
//
// FIXES (Jun 2026):
//   #1  Builder() now returns View("ExamBuilder", vm) explicitly.
//       The view file is ExamBuilder.cshtml, but the action is named
//       Builder, so the default View(vm) was probing for Builder.cshtml
//       and 404-ing the view → "exam not opened". All page actions now
//       name their view explicitly so a file/action name mismatch can
//       never silently break the page again.
//   #2  Api() validates ApiBaseUrl (and normalises the trailing slash)
//       instead of throwing a cryptic ArgumentNullException deep inside
//       the proxy, which used to be swallowed and surface as a blank
//       failure on the client.
//   (existing) POST actions read the raw body via ReadBodyAsync() so the
//       configured serializer can never null out a [FromBody] JObject.
// ============================================================
using System;
using System.IO;
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
    public class ExamController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ExamController> _logger;

        public ExamController(IHttpClientFactory httpClientFactory,
                              IConfiguration config,
                              ILogger<ExamController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        // ── shared HttpClient with the session bearer token ──────────────────────────
        private HttpClient Api()
        {
            var token = HttpContext.Session.GetString("JwtToken");

            var baseUrl = _config["ApiBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException(
                    "ApiBaseUrl is not configured. Add it to the ProximaLMS (MVC) appsettings.json, " +
                    "e.g. \"ApiBaseUrl\": \"https://localhost:7001/\".");

            // HttpClient resolves relative paths against BaseAddress; that only works
            // reliably when the base ends in '/'. Relative paths below never start
            // with '/', so this is always correct.
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        // Read the raw JSON request body into a JObject, regardless of which JSON
        // serializer the MVC pipeline is configured with. Returns an empty JObject
        // (never null) for an empty or malformed body.
        private async Task<JObject> ReadBodyAsync()
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync();
            try
            {
                return string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);
            }
            catch
            {
                return new JObject();
            }
        }

        // GET proxy — always returns JSON to the client (even on failure), so the
        // page's JSON.parse never chokes on an HTML error page.
        private async Task<IActionResult> ForwardGet(string path)
        {
            try
            {
                var resp = await Api().GetAsync(path);
                var json = await resp.Content.ReadAsStringAsync();

                // A non-2xx with a JSON body (the API's own {Status,Message}) is
                // passed straight through so the page shows the real message.
                if (!resp.IsSuccessStatusCode && string.IsNullOrWhiteSpace(json))
                    return Json(new { Status = "Error", Message = DescribeHttp((int)resp.StatusCode, path) });

                return Content(string.IsNullOrWhiteSpace(json) ? "{}" : json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GET forward failed: {Path}", path);
                return Json(new { Status = "Error", Message = ex.Message });
            }
        }

        // Turn an empty-bodied error status into something actionable on screen,
        // instead of the old silent "{}" that rendered as a blank failure.
        private string DescribeHttp(int status, string path)
        {
            var baseUrl = _config["ApiBaseUrl"];
            return status switch
            {
                200 => $"(EXAMFIX-v3) API returned 200 but an EMPTY body for {path}. " +
                       "Seeing this means the new MVC ExamController IS running and the API returned nothing.",
                404 => $"(EXAMFIX-v3) API endpoint not found (404): {path}. The ProximaLMSAPI project is reachable at {baseUrl} " +
                       "but does not expose this route — rebuild and restart the API so the latest ExamController is running.",
                401 => "API rejected the request (401 Unauthorized). The session token is missing or expired — sign in again.",
                500 => $"API returned 500 with no body for {path}. The API likely threw before its own error handler " +
                       "(running in Production with no exception page). Check the API console/logs for the stack trace.",
                _ => $"API returned HTTP {status} (empty body) for {path}."
            };
        }

        private async Task<IActionResult> ForwardPost(string path, JObject body)
        {
            try
            {
                var http = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var resp = await Api().PostAsync(path, http);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode && string.IsNullOrWhiteSpace(json))
                    return Json(new { Status = "Error", Message = DescribeHttp((int)resp.StatusCode, path) });

                return Content(string.IsNullOrWhiteSpace(json) ? "{}" : json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POST forward failed: {Path}", path);
                return Json(new { Status = "Error", Message = ex.Message });
            }
        }

        // Variant used by the take flow (success/message-shaped errors).
        private async Task<IActionResult> ForwardPostJ(string path, JObject body)
        {
            try
            {
                var http = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var resp = await Api().PostAsync(path, http);
                var json = await resp.Content.ReadAsStringAsync();

                // ANY empty body (even a 200) would otherwise become "{}" and render
                // as the generic "Could not start exam." — surface the real status.
                if (string.IsNullOrWhiteSpace(json))
                    return Json(new { success = false, message = DescribeHttp((int)resp.StatusCode, path) });

                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "POST forward failed: {Path}", path);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Best-effort course title lookup shared by Builder/Bank.
        private async Task<string> TryGetCourseTitleAsync(int courseId, string fallback)
        {
            try
            {
                var resp = await Api().GetAsync($"api/CreateCourse/details/{courseId}");
                if (resp.IsSuccessStatusCode)
                {
                    var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    var title = obj["course"]?["courseTitle"]?.ToString()
                             ?? obj["course"]?["CourseTitle"]?.ToString();
                    if (!string.IsNullOrEmpty(title)) return title;
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "course title fetch failed for {Course}", courseId); }
            return fallback;
        }


        // ═════════════════════════════════════════════════════════
        // GET /Exam  (student "My Exams" landing)
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult Index()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token)) return RedirectToAction("Index", "Home");
            return View("MyExams");
        }

        // ═════════════════════════════════════════════════════════
        // GET /Exam/MyExams
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult MyExams()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token)) return RedirectToAction("Index", "Home");
            return View("MyExams");
        }

        // JSON proxy: enrolled courses for the student
        [HttpGet]
        public Task<IActionResult> StudentCourses()
        {
            var uid = HttpContext.Session.GetString("UserID") ?? "0";
            return ForwardGet($"api/courseassignment/student-courses/{uid}");
        }

        // ═════════════════════════════════════════════════════════
        // GET /Exam/Builder?courseId=#
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Builder(int courseId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token) || courseId <= 0)
                return RedirectToAction("Index", "Home");

            var vm = new ExamBuilderPageViewModel
            {
                CourseID = courseId,
                CourseTitle = await TryGetCourseTitleAsync(courseId, $"Course #{courseId}")
            };

            // explicit view name — file is ExamBuilder.cshtml, action is Builder
            return View("ExamBuilder", vm);
        }


        // ── JSON proxies ──────────────────────────────────────────
        [HttpGet]
        public Task<IActionResult> List(int courseId)
        {
            // Forward the student id so the API can return per-student
            // attempt counts (attemptsUsed / lastAttemptId / lastScore).
            var uid = HttpContext.Session.GetString("UserID");
            int sid = int.TryParse(uid, out var s) ? s : 0;
            return ForwardGet($"api/exam/by-course/{courseId}?studentId={sid}");
        }

        [HttpGet]
        public Task<IActionResult> Full(int examId) => ForwardGet($"api/exam/full/{examId}");

        [HttpPost]
        public async Task<IActionResult> SaveExam()
        {
            var body = await ReadBodyAsync();
            body["Actor"] = HttpContext.Session.GetString("Email") ?? "Admin";
            return await ForwardPost("api/exam/save", body);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStatus()
            => await ForwardPost("api/exam/toggle-status", await ReadBodyAsync());

        [HttpPost]
        public async Task<IActionResult> DeleteExam()
            => await ForwardPost("api/exam/delete", await ReadBodyAsync());

        [HttpPost]
        public async Task<IActionResult> SaveQuestion()
            => await ForwardPost("api/exam/question/save", await ReadBodyAsync());

        [HttpPost]
        public async Task<IActionResult> DeleteQuestion()
            => await ForwardPost("api/exam/question/delete", await ReadBodyAsync());

        [HttpPost]
        public async Task<IActionResult> ReorderQuestions()
            => await ForwardPost("api/exam/question/reorder", await ReadBodyAsync());


        // ═════════════════════════════════════════════════════════
        // GET /Exam/Bank?courseId=#
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Bank(int courseId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token) || courseId <= 0)
                return RedirectToAction("Index", "Home");

            var vm = new ExamBuilderPageViewModel
            {
                CourseID = courseId,
                CourseTitle = await TryGetCourseTitleAsync(courseId, $"Course #{courseId}")
            };

            return View("Bank", vm);
        }

        [HttpGet]
        public Task<IActionResult> BankList(int courseId, string difficulty = null, string type = null, string search = null)
        {
            var qs = $"courseId={courseId}"
                   + (string.IsNullOrEmpty(difficulty) ? "" : $"&difficulty={Uri.EscapeDataString(difficulty)}")
                   + (string.IsNullOrEmpty(type) ? "" : $"&type={Uri.EscapeDataString(type)}")
                   + (string.IsNullOrEmpty(search) ? "" : $"&search={Uri.EscapeDataString(search)}");
            return ForwardGet($"api/exam/bank/list?{qs}");
        }

        [HttpGet]
        public Task<IActionResult> BankFull(int bankQuestionId) =>
            ForwardGet($"api/exam/bank/full/{bankQuestionId}");

        [HttpPost]
        public async Task<IActionResult> BankSave()
        {
            var body = await ReadBodyAsync();
            body["Actor"] = HttpContext.Session.GetString("Email") ?? "Admin";
            return await ForwardPost("api/exam/bank/save", body);
        }

        [HttpPost]
        public async Task<IActionResult> BankDelete()
            => await ForwardPost("api/exam/bank/delete", await ReadBodyAsync());

        [HttpPost]
        public async Task<IActionResult> BankLink()
            => await ForwardPost("api/exam/bank/link", await ReadBodyAsync());

        [HttpPost]
        public async Task<IActionResult> BankUnlink()
            => await ForwardPost("api/exam/bank/unlink", await ReadBodyAsync());

        [HttpGet]
        public Task<IActionResult> AllQuestions(int examId) => ForwardGet($"api/exam/all-questions/{examId}");


        // ═════════════════════════════════════════════════════════
        // GET /Exam/Take?examId=#
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Take(int examId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token) || examId <= 0)
                return RedirectToAction("Index", "Home");

            var vm = new ExamBuilderPageViewModel  // reused (CourseID + CourseTitle)
            {
                CourseID = examId,                  // doubles as ExamID on this page
                CourseTitle = "Exam"
            };

            try
            {
                var resp = await Api().GetAsync($"api/exam/full/{examId}");
                if (resp.IsSuccessStatusCode)
                {
                    var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    vm.CourseTitle = obj["exam"]?["title"]?.ToString() ?? "Exam";
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Take title fetch failed"); }

            return View("Take", vm);
        }


        // ── start: forces StudentID from session, never trusts the body ─────────────
        [HttpPost]
        public async Task<IActionResult> StartTake()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var uid = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(token) || !int.TryParse(uid, out var studentId))
                return Json(new { success = false, message = "Session expired. Please sign in again." });

            var body = await ReadBodyAsync();   // never null
            body["StudentID"] = studentId;      // ← server-set, can't be spoofed
            return await ForwardPostJ("api/exam/take/start", body);
        }

        [HttpPost]
        public async Task<IActionResult> SaveAnswer()
            => await ForwardPostJ("api/exam/attempt/answer", await ReadBodyAsync());

        [HttpPost]
        public async Task<IActionResult> SubmitAttempt()
            => await ForwardPostJ("api/exam/attempt/submit", await ReadBodyAsync());

        [HttpGet]
        public Task<IActionResult> AttemptState(int attemptId) =>
            ForwardGet($"api/exam/attempt/{attemptId}/state");

        [HttpGet]
        public Task<IActionResult> AttemptResult(int attemptId) =>
            ForwardGet($"api/exam/attempt/{attemptId}/result");


        // ═════════════════════════════════════════════════════════
        // GET /Exam/Result?attemptId=#
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Result(int attemptId)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var uid = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(token) || attemptId <= 0)
                return RedirectToAction("Index", "Home");

            var vm = new ExamBuilderPageViewModel  // reused (CourseID + CourseTitle)
            {
                CourseID = attemptId,    // doubles as AttemptID on this page
                CourseTitle = "Exam Result"
            };

            // Ownership check: a student must only see their own results.
            try
            {
                var resp = await Api().GetAsync($"api/exam/attempt/{attemptId}/header");
                if (resp.IsSuccessStatusCode)
                {
                    var obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                    var ownerId = obj["studentID"]?.Value<int>() ?? obj["StudentID"]?.Value<int>();
                    if (int.TryParse(uid, out var currentUid)
                        && ownerId.HasValue && ownerId.Value != currentUid)
                    {
                        TempData["Error"] = "That result isn't yours.";
                        return RedirectToAction("Index", "Home");
                    }
                    vm.CourseTitle = obj["examTitle"]?.ToString() ?? obj["ExamTitle"]?.ToString() ?? "Exam Result";
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Result ownership check failed"); }

            return View("Result", vm);
        }
    }
}