using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using ProximaLMS.Models;

namespace ProximaLMS.Controllers
{
    public class AccountController : Controller
    {
        private readonly string _apiBaseUrl;

        public AccountController(IConfiguration configuration)
        {
            // ✅ FIXED KEY
            _apiBaseUrl = configuration["ApiBaseUrl"];

            if (string.IsNullOrWhiteSpace(_apiBaseUrl))
                throw new InvalidOperationException(
                    "ApiBaseUrl is missing in appsettings.json");
        }

        // 🔹 Common HttpClient
        private HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(_apiBaseUrl)
            };
        }

        // ============================
        // STEP 0 : LOAD PAGE
        // ============================
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // ============================
        // STEP 1 : SEND OTP
        // ============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var client = CreateClient();

            var response = await client.PostAsJsonAsync(
                "/api/Auth/request-otp",
                new { Email = model.Email });

            if (!response.IsSuccessStatusCode)
            {
                TempData["ForgotError"] = "Email not found.";
                return View(model);
            }

            var result = await response.Content.ReadFromJsonAsync<OtpResponse>();

            TempData["UserId"] = result?.UserId;
            TempData["Email"] = model.Email;

            ViewBag.ShowOtpPanel = true;
            return View(model);
        }

        // ============================
        // STEP 2 : VERIFY OTP
        // ============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(string otpInput)
        {
            var userId = TempData.Peek("UserId")?.ToString();
            var email = TempData.Peek("Email")?.ToString();

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
            {
                TempData["ForgotError"] = "Session expired. Please try again.";
                return RedirectToAction("ForgotPassword");
            }

            if (string.IsNullOrWhiteSpace(otpInput))
            {
                TempData["ForgotError"] = "Please enter OTP.";
                ViewBag.ShowOtpPanel = true;
                return View("ForgotPassword");
            }

            using var client = CreateClient();

            var response = await client.PostAsJsonAsync(
                "/api/Auth/verify-otp",
                new
                {
                    UserId = int.Parse(userId),
                    OTP = otpInput,
                    Email = email
                });

            if (!response.IsSuccessStatusCode)
            {
                TempData["ForgotError"] = "Invalid or expired OTP.";
                ViewBag.ShowOtpPanel = true;
                return View("ForgotPassword");
            }

            ViewBag.ShowResetPanel = true;
            return View("ForgotPassword");
        }

        // ============================
        // STEP 3 : RESEND OTP
        // ============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp()
        {
            var email = TempData.Peek("Email")?.ToString();
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("ForgotPassword");

            using var client = CreateClient();

            var response = await client.PostAsJsonAsync(
                "/api/Auth/resend-otp",
                new { Email = email });

            if (response.IsSuccessStatusCode)
                TempData["ForgotSuccess"] = "OTP resent successfully.";
            else
                TempData["ForgotError"] = "Unable to resend OTP.";

            ViewBag.ShowOtpPanel = true;
            return View("ForgotPassword");
        }

        // ============================
        // STEP 4 : RESET PASSWORD
        // ============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string NewPassword, string ConfirmPassword)
        {
            if (string.IsNullOrWhiteSpace(NewPassword) ||
                NewPassword != ConfirmPassword)
            {
                TempData["ForgotError"] = "Passwords do not match.";
                ViewBag.ShowResetPanel = true;
                return View("ForgotPassword");
            }

            var userId = TempData.Peek("UserId")?.ToString();
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("ForgotPassword");

            using var client = CreateClient();

            var response = await client.PostAsJsonAsync(
                "/api/Auth/reset-password",
                new
                {
                    UserId = int.Parse(userId),
                    NewPassword
                });

            if (response.IsSuccessStatusCode)
            {
                TempData["ForgotSuccess"] = "Password reset successful. Please login.";
                ViewBag.ShowResetPanel = false;
                ViewBag.RedirectToLogin = true;

                return View("ForgotPassword");
            }


            TempData["ForgotError"] = "Password reset failed.";
            ViewBag.ShowResetPanel = true;
            return View("ForgotPassword");
        }

        // ============================
        // CHANGE PASSWORD (logged-in user)
        // ============================
        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserID")))
                return RedirectToAction("Index", "Login");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string CurrentPassword, string NewPassword, string ConfirmPassword)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out int uid))
                return RedirectToAction("Index", "Login");

            if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword))
            {
                TempData["CpError"] = "All fields are required.";
                return View();
            }
            if (NewPassword != ConfirmPassword)
            {
                TempData["CpError"] = "New password and confirmation do not match.";
                return View();
            }
            if (NewPassword.Length < 8)
            {
                TempData["CpError"] = "New password must be at least 8 characters.";
                return View();
            }

            try
            {
                using var client = CreateClient();
                var response = await client.PostAsJsonAsync(
                    "/api/Profile/change-password",
                    new { UserID = uid, CurrentPassword, NewPassword });

                if (response.IsSuccessStatusCode)
                {
                    TempData["CpSuccess"] = "Password changed successfully.";
                    return View();
                }

                // surface the API's message (current password wrong, etc.)
                string msg = "Could not change password.";
                try
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        var jo = Newtonsoft.Json.Linq.JObject.Parse(body);
                        msg = (jo["message"] ?? jo["Message"])?.ToString() ?? msg;
                    }
                }
                catch { /* non-JSON body — keep generic */ }

                TempData["CpError"] = msg;
                return View();
            }
            catch (Exception ex)
            {
                TempData["CpError"] = "Connection error: " + ex.Message;
                return View();
            }
        }

        // ============================
        // LOGOUT
        // ============================
        public async Task<IActionResult> Logout()
        {
            // Revoke every refresh token server-side so the session
            // cannot be silently extended after logout.
            var userId = HttpContext.Session.GetString("UserID");
            if (!string.IsNullOrEmpty(userId) && int.TryParse(userId, out int uid))
            {
                try
                {
                    using var client = CreateClient();
                    await client.PostAsJsonAsync("/api/authtoken/logout", new { UserID = uid });
                }
                catch
                {
                    // Non-fatal — local session is cleared regardless.
                }
            }

            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync();
            return RedirectToAction("Index", "Login");
        }


    }
}