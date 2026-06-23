// ============================================================
// ProximaLMS/Controllers/AssignmentsController.cs
// ------------------------------------------------------------
// Backs the Student Dashboard "Upcoming Deadlines" card, which
// calls GET /Assignments/UpcomingDeadlines?limit=5 on load.
// Proxies to the API reports endpoint using the logged-in
// student's JWT + id. Always returns a JSON array so the
// dashboard never breaks on errors.
// ============================================================
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace ProximaLMS.Controllers
{
    public class AssignmentsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public AssignmentsController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        // GET /Assignments/UpcomingDeadlines?limit=5
        [HttpGet]
        public async Task<IActionResult> UpcomingDeadlines(int limit = 5)
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");

            if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out int uid) || uid <= 0)
                return Json(Array.Empty<object>());

            if (limit <= 0 || limit > 50) limit = 5;

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
                if (!string.IsNullOrEmpty(token))
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync(
                    $"api/Reports/student/{uid}/upcoming-deadlines?limit={limit}");

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    return Content(json, "application/json");
                }
            }
            catch { /* fall through to empty list */ }

            return Json(Array.Empty<object>());
        }
    }
}
