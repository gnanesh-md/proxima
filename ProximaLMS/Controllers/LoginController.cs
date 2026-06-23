// ============================================================
// ProximaLMS/Controllers/LoginController.cs   (MVC frontend)
// ------------------------------------------------------------
// ⚠ DEFENSE-IN-DEPTH FIX (third pass):
//   The OTP-then-bounced-to-login bug had THREE layers:
//
//     LAYER 1 (root cause):
//       The API's Program.cs never registered ITokenService,
//       so AuthTokenController throws on construction and
//       /api/authtoken/issue-refresh returns 500.
//       → Fixed in ProximaLMSAPI/Program.cs.
//
//     LAYER 2:
//       TokenRefreshService bailed with `return false` whenever
//       the RefreshToken was missing — even if the access token
//       was still fresh. RequireJwt then cleared the session
//       and redirected to /Home/Index.
//       → Fixed in TokenRefreshService.cs.
//
//     LAYER 3 (this file):
//       Even after Layers 1+2 are fixed, the TokenRefreshService's
//       date-based fallback needs an AccessExpiresAt timestamp in
//       session. The previous code only wrote AccessExpiresAt when
//       issue-refresh SUCCEEDED. We now write it UNCONDITIONALLY
//       (best-effort, using the JWT's actual exp claim with a
//       safe default of +15 minutes if anything goes wrong).
//
//   Also keeps the previous Session-based handoff for the OTP
//   panel (instead of TempData) as defence against future view-
//   side TempData reads accidentally consuming the pending state.
// ============================================================
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using ProximaLMS.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProximaLMS.Controllers
{
    public class LoginController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        // Session keys used ONLY between credential-post and verify-otp-post.
        private const string SK_PENDING_USERID = "PendingLoginUserId";
        private const string SK_PENDING_EMAIL = "PendingLoginEmail";
        private const string SK_PENDING_OTP = "PendingLoginOTP";
        private const string SK_PENDING_OTP_AT = "PendingLoginOtpGeneratedAt";
        private const string SK_PENDING_REMEMBER = "PendingLoginRememberMe";

        public LoginController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        private HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(_config["ApiBaseUrl"])
            };
        }

        private void ClearPendingLoginSession()
        {
            HttpContext.Session.Remove(SK_PENDING_USERID);
            HttpContext.Session.Remove(SK_PENDING_EMAIL);
            HttpContext.Session.Remove(SK_PENDING_OTP);
            HttpContext.Session.Remove(SK_PENDING_OTP_AT);
            HttpContext.Session.Remove(SK_PENDING_REMEMBER);
        }

        // ─────────────────────────────────────────
        // GET /Login
        // ─────────────────────────────────────────
        [HttpGet]
        public IActionResult Index()
        {
            // Clear any half-finished OTP attempt so the modal does
            // not pop up unexpectedly on a fresh visit.
            ClearPendingLoginSession();
            return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
        }

        // ─────────────────────────────────────────
        // POST /Login   (credentials → OTP trigger)
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(LoginRegisterViewModel model)
        {
            ModelState.Clear();

            if (!TryValidateModel(model.Login, prefix: "Login"))
                return View("~/Views/Home/Index.cshtml", model);

            using var client = CreateClient();

            var response = await client.PostAsJsonAsync("/api/Auth/login", new
            {
                Email = model.Login.Email,
                Password = model.Login.Password
            });

            if (!response.IsSuccessStatusCode)
            {
                TempData["LoginError"] = "Invalid email or password.";
                return RedirectToAction("Index");
            }

            var result = await response.Content.ReadFromJsonAsync<LoginApiResponse>();

            // Block login if role disabled (before OTP burns a code).
            if (result != null && result.RoleID > 0)
            {
                bool roleActive = await CheckRoleIsActive(client, result.RoleID);
                if (!roleActive)
                {
                    TempData["LoginError"] = "Your account role has been disabled. Please contact the administrator.";
                    return RedirectToAction("Index");
                }
            }

            // RememberMe cookie (so the email pre-fills next visit).
            if (model.Login.RememberMe)
                Response.Cookies.Append("rememberMeEmail", model.Login.Email,
                    new CookieOptions { Expires = DateTime.Now.AddDays(30) });
            else
                Response.Cookies.Delete("rememberMeEmail");

            // ── Persist pending login state in SESSION (not TempData). ──
            HttpContext.Session.SetString(SK_PENDING_USERID, (result?.UserId ?? 0).ToString());
            HttpContext.Session.SetString(SK_PENDING_EMAIL, model.Login.Email ?? "");
            HttpContext.Session.SetString(SK_PENDING_OTP, result?.OTP ?? "");
            HttpContext.Session.SetString(SK_PENDING_OTP_AT, DateTime.UtcNow.ToString("o"));
            HttpContext.Session.SetString(SK_PENDING_REMEMBER, model.Login.RememberMe ? "1" : "0");

            TempData["OtpGeneratedAt"] = DateTime.Now.ToString();
            ViewBag.ShowOtpPanel = true;
            return View("~/Views/Home/Index.cshtml", model);
        }

        // ─────────────────────────────────────────
        // POST /Login/VerifyOtp
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(string otpInput)
        {
            var userId = HttpContext.Session.GetString(SK_PENDING_USERID);
            var email = HttpContext.Session.GetString(SK_PENDING_EMAIL);

            if (string.IsNullOrEmpty(userId) || userId == "0" || string.IsNullOrEmpty(email))
            {
                ClearPendingLoginSession();
                TempData["LoginError"] = "Session expired. Please login again.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(otpInput))
            {
                TempData["OtpError"] = "Please enter OTP.";
                ViewBag.ShowOtpPanel = true;
                return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
            }

            using var client = CreateClient();

            var response = await client.PostAsJsonAsync("/api/Auth/verify-otp", new
            {
                UserId = int.Parse(userId),
                OTP = otpInput,
                Email = email
            });

            if (!response.IsSuccessStatusCode)
            {
                TempData["OtpError"] = "Invalid or expired OTP.";
                ViewBag.ShowOtpPanel = true;
                return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
            }

            var result = await response.Content.ReadFromJsonAsync<VerifyOtpResponse>();

            if (result == null || string.IsNullOrEmpty(result.Token))
            {
                TempData["OtpError"] = "Verification failed. Please try again.";
                ViewBag.ShowOtpPanel = true;
                return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
            }

            // Role still active?
            bool roleStillActive = await CheckRoleIsActive(client, result.RoleID);
            if (!roleStillActive)
            {
                ClearPendingLoginSession();
                TempData["LoginError"] = "Your account role has been disabled. Please contact the administrator.";
                return RedirectToAction("Index");
            }

            if (result.IsActive == false)
            {
                ClearPendingLoginSession();
                TempData["LoginError"] = "Your account has been deactivated. Please contact the administrator.";
                return RedirectToAction("Index");
            }

            // ── Establish post-login session ──────────────────────
            HttpContext.Session.SetString("JwtToken", result.Token);
            HttpContext.Session.SetString("UserID", userId);
            HttpContext.Session.SetString("Email", email);
            HttpContext.Session.SetInt32("RoleID", result.RoleID);
            HttpContext.Session.SetString("RoleName", result.RoleName ?? "");

            // ── ALWAYS seed AccessExpiresAt (defensive). ──────────
            // Read the JWT's actual exp claim where possible — the
            // API currently generates 2-hour tokens but we don't
            // assume that. If parsing fails, fall back to +15 min.
            DateTime accessExpiresAt = ReadJwtExpiry(result.Token)
                                     ?? DateTime.UtcNow.AddMinutes(15);
            HttpContext.Session.SetString("AccessExpiresAt",
                accessExpiresAt.ToUniversalTime().ToString("o"));

            // ── Issue a refresh token. Failure is non-fatal because
            //    TokenRefreshService now falls back to the access
            //    token's own expiry when RefreshToken is missing.
            try
            {
                var refreshResp = await client.PostAsJsonAsync("/api/authtoken/issue-refresh", new
                {
                    UserID = int.Parse(userId),
                    Email = email,
                    RoleID = result.RoleID
                });

                if (refreshResp.IsSuccessStatusCode)
                {
                    var refresh = await refreshResp.Content.ReadFromJsonAsync<IssueRefreshResponse>();
                    if (refresh != null && refresh.Success && !string.IsNullOrEmpty(refresh.RefreshToken))
                    {
                        int accessMins = refresh.AccessTokenMinutes > 0 ? refresh.AccessTokenMinutes : 15;

                        HttpContext.Session.SetString("RefreshToken", refresh.RefreshToken);
                        // Override the defensive AccessExpiresAt with the
                        // canonical value reported by the API.
                        HttpContext.Session.SetString("AccessExpiresAt",
                            DateTime.UtcNow.AddMinutes(accessMins).ToString("o"));
                        HttpContext.Session.SetString("RefreshExpiresAt",
                            refresh.RefreshExpiresAt.ToUniversalTime().ToString("o"));
                    }
                }
            }
            catch
            {
                // Non-fatal: TokenRefreshService now tolerates this.
            }

            // Permissions for the sidebar.
            await LoadPermissionsIntoSession(client, result.RoleID);

            // Pending state no longer needed.
            ClearPendingLoginSession();

            return result.RoleID switch
            {
                1 => RedirectToAction("Index", "Dashboard"),
                2 => RedirectToAction("Index", "Instructor"),
                3 => RedirectToAction("Index", "StudentDashboard"),
                _ => RedirectToAction("Index", "StudentDashboard")
            };
        }

        // ─────────────────────────────────────────
        // POST /Login/ResendOtp
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp()
        {
            var email = HttpContext.Session.GetString(SK_PENDING_EMAIL)
                     ?? HttpContext.Session.GetString("Email");

            if (string.IsNullOrEmpty(email))
            {
                TempData["LoginError"] = "Session expired. Please login again.";
                return RedirectToAction("Index", "Login");
            }

            using var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/Auth/resend-otp", new { Email = email });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ResendOtpResponse>();
                if (result != null)
                {
                    HttpContext.Session.SetString(SK_PENDING_USERID, result.UserId.ToString());
                    HttpContext.Session.SetString(SK_PENDING_EMAIL, email);
                    HttpContext.Session.SetString(SK_PENDING_OTP, result.OTP ?? "");
                    HttpContext.Session.SetString(SK_PENDING_OTP_AT, DateTime.UtcNow.ToString("o"));

                    TempData["OtpGeneratedAt"] = DateTime.Now.ToString();
                    TempData["OtpMessage"] = "OTP resent successfully! Please check your email.";
                    ViewBag.ShowOtpPanel = true;
                }
            }
            else
            {
                TempData["OtpError"] = "Failed to resend OTP.";
                ViewBag.ShowOtpPanel = true;
            }

            return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
        }

        // ─────────────────────────────────────────
        // POST /Login/Register  →  STEP 1: send OTP
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(LoginRegisterViewModel model)
        {
            ModelState.Clear();

            if (!TryValidateModel(model.Register, prefix: "Register"))
            {
                ViewBag.OpenRegisterModal = true;
                return View("~/Views/Home/Index.cshtml", model);
            }

            var pending = new RegisterApiRequest
            {
                Name = model.Register.FullName,
                Gender = model.Register.Gender,
                MobileNumber = $"{model.Register.CountryCode}{model.Register.PhoneNumber}",
                Email = model.Register.Email,
                Password = model.Register.Password,
                ReferralCode = string.IsNullOrWhiteSpace(model.Register.ReferralCode)
                    ? null : model.Register.ReferralCode.Trim().ToUpper()
            };

            using var client = CreateClient();

            var otpResponse = await client.PostAsJsonAsync("/api/Auth/send-register-otp", new
            {
                Email = pending.Email,
                MobileNumber = pending.MobileNumber
            });

            if (!otpResponse.IsSuccessStatusCode)
            {
                var err = await otpResponse.Content.ReadFromJsonAsync<ApiResponse>();
                TempData["RegisterError"] = err?.Message
                    ?? "Could not send verification code. Email or mobile may already be registered.";
                ViewBag.OpenRegisterModal = true;
                return View("~/Views/Home/Index.cshtml", model);
            }

            HttpContext.Session.SetString("PendingRegistration", JsonSerializer.Serialize(pending));
            HttpContext.Session.SetString("PendingRegEmail", pending.Email);

            TempData["RegOtpMessage"] = "Verification code sent to your email.";
            ViewBag.ShowRegisterOtpPanel = true;
            return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
        }

        // ─────────────────────────────────────────
        // POST /Login/VerifyRegisterOtp  →  STEP 2
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyRegisterOtp(string otpInput)
        {
            var pendingJson = HttpContext.Session.GetString("PendingRegistration");
            var email = HttpContext.Session.GetString("PendingRegEmail");

            if (string.IsNullOrEmpty(pendingJson) || string.IsNullOrEmpty(email))
            {
                TempData["RegisterError"] = "Session expired. Please register again.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(otpInput))
            {
                TempData["RegOtpError"] = "Please enter the 6-digit code.";
                ViewBag.ShowRegisterOtpPanel = true;
                return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
            }

            using var client = CreateClient();

            var verifyResp = await client.PostAsJsonAsync("/api/Auth/verify-register-otp", new
            {
                Email = email,
                OTP = otpInput
            });

            if (!verifyResp.IsSuccessStatusCode)
            {
                var err = await verifyResp.Content.ReadFromJsonAsync<ApiResponse>();
                TempData["RegOtpError"] = err?.Message ?? "Invalid or expired code.";
                ViewBag.ShowRegisterOtpPanel = true;
                return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
            }

            var pending = JsonSerializer.Deserialize<RegisterApiRequest>(pendingJson);
            var regResp = await client.PostAsJsonAsync("/api/Auth/register", pending);
            var regResult = await regResp.Content.ReadFromJsonAsync<ApiResponse>();

            HttpContext.Session.Remove("PendingRegistration");
            HttpContext.Session.Remove("PendingRegEmail");

            if (regResp.IsSuccessStatusCode)
            {
                TempData["RegisterSuccess"] = regResult?.Message ?? "Registration successful! You can now login.";
                return RedirectToAction("Index");
            }

            TempData["RegisterError"] = regResult?.Message ?? "Registration failed. Please try again.";
            return RedirectToAction("Index");
        }

        // ─────────────────────────────────────────
        // POST /Login/ResendRegisterOtp
        // ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendRegisterOtp()
        {
            var pendingJson = HttpContext.Session.GetString("PendingRegistration");
            var email = HttpContext.Session.GetString("PendingRegEmail");

            if (string.IsNullOrEmpty(pendingJson) || string.IsNullOrEmpty(email))
            {
                TempData["RegisterError"] = "Session expired. Please register again.";
                return RedirectToAction("Index");
            }

            var pending = JsonSerializer.Deserialize<RegisterApiRequest>(pendingJson);

            using var client = CreateClient();
            var resp = await client.PostAsJsonAsync("/api/Auth/send-register-otp", new
            {
                Email = pending.Email,
                MobileNumber = pending.MobileNumber
            });

            if (resp.IsSuccessStatusCode)
                TempData["RegOtpMessage"] = "A new code has been sent to your email.";
            else
                TempData["RegOtpError"] = "Could not resend the code. Please try again.";

            ViewBag.ShowRegisterOtpPanel = true;
            return View("~/Views/Home/Index.cshtml", new LoginRegisterViewModel());
        }

        // ─────────────────────────────────────────
        // GET /Login/ForgotPassword
        // ─────────────────────────────────────────
        public IActionResult ForgotPassword() => View();

        // ─────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────

        private async Task<bool> CheckRoleIsActive(HttpClient client, int roleId)
        {
            try
            {
                var resp = await client.GetAsync($"/api/role/{roleId}");
                if (!resp.IsSuccessStatusCode) return true;

                var json = await resp.Content.ReadAsStringAsync();
                var token = JToken.Parse(json);
                var data = token["data"] ?? token["result"] ?? token;

                return data["IsActive"]?.Value<bool>()
                    ?? data["isActive"]?.Value<bool>()
                    ?? true;
            }
            catch
            {
                return true; // fail-open
            }
        }

        private async Task LoadPermissionsIntoSession(HttpClient client, int roleId)
        {
            try
            {
                var resp = await client.GetAsync($"/api/role/{roleId}/permissions");
                if (!resp.IsSuccessStatusCode) return;

                var json = await resp.Content.ReadAsStringAsync();
                var token = JToken.Parse(json);

                JArray? arr = null;
                if (token.Type == JTokenType.Array)
                    arr = (JArray)token;
                else
                    arr = token["data"] as JArray ?? token["result"] as JArray;

                if (arr == null) return;

                var perms = arr.Select(s => new
                {
                    ScreenCode = s["ScreenCode"]?.Value<string>() ?? "",
                    CanView = s["CanView"]?.Value<bool>() ?? false,
                    CanCreate = s["CanCreate"]?.Value<bool>() ?? false,
                    CanEdit = s["CanEdit"]?.Value<bool>() ?? false,
                    CanDelete = s["CanDelete"]?.Value<bool>() ?? false
                }).Where(p => !string.IsNullOrEmpty(p.ScreenCode)).ToList();

                var permJson = JsonSerializer.Serialize(perms);
                HttpContext.Session.SetString("Permissions", permJson);
                HttpContext.Session.SetString("PermissionsLastRefresh", DateTime.UtcNow.ToString("o"));
            }
            catch
            {
                // No permissions in session = restricted access (safe default).
            }
        }

        /// <summary>
        /// Read the `exp` claim out of a JWT without validating it.
        /// We're only using it for our own session bookkeeping; the
        /// API still validates signature + lifetime on every call.
        /// </summary>
        private static DateTime? ReadJwtExpiry(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token)) return null;
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(token)) return null;
                var jwt = handler.ReadJwtToken(token);
                return jwt.ValidTo == DateTime.MinValue ? null : jwt.ValidTo;
            }
            catch
            {
                return null;
            }
        }
    }
}