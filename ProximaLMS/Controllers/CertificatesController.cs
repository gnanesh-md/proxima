using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace ProximaLMS.Controllers
{
    public class CertificatesController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public CertificatesController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        private (System.Net.Http.HttpClient client, string? userId) Api()
        {
            var token = HttpContext.Session.GetString("JwtToken");
            var userId = HttpContext.Session.GetString("UserID");
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, userId);
        }

        // ── Student: My Certificates ─────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var (client, userId) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            var list = new JArray();
            try
            {
                var resp = await client.GetAsync($"api/Certificate/student/{userId}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    list = JObject.Parse(json)["data"] as JArray ?? new JArray();
                }
            }
            catch { /* show empty state */ }

            ViewBag.Certificates = list;
            return View();
        }

        // ── Student: download (proxies the API PDF stream) ───
        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            var (client, userId) = Api();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Index", "Login");

            var resp = await client.GetAsync($"api/Certificate/{id}/download");
            if (!resp.IsSuccessStatusCode) return NotFound();

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var name = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? $"certificate-{id}.pdf";
            return File(bytes, "application/pdf", name);
        }

        // ── Student: email me a copy ─────────────────────────
        [HttpPost]
        public async Task<IActionResult> Email(int id)
        {
            var (client, _) = Api();
            var resp = await client.PostAsync($"api/Certificate/{id}/email", null);
            return Json(new { success = resp.IsSuccessStatusCode });
        }

        // ── PUBLIC: verify by token (QR target) ──────────────
        [HttpGet("/verify/{token}")]
        [AllowAnonymousFallback]      // see note: ensure no global auth filter blocks this
        public async Task<IActionResult> Verify(string token)
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_config["ApiBaseUrl"]);

            var model = new JObject { ["valid"] = false };
            try
            {
                var resp = await client.GetAsync($"api/Certificate/verify/{token}");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    model = JObject.Parse(json);
                }
            }
            catch { /* model stays invalid */ }

            ViewBag.Result = model;
            return View();
        }
    }

    // Marker attribute only — if you use a global [Authorize]/RequireJwt filter,
    // exclude the Verify route from it. Safe no-op otherwise.
    public class AllowAnonymousFallbackAttribute : Attribute { }
}
