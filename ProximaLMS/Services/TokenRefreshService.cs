using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProximaLMS.Models;

namespace ProximaLMS.Services
{
    public interface ITokenRefreshService
    {
        Task<bool> EnsureValidTokenAsync(HttpContext http);
    }

    public class TokenRefreshService : ITokenRefreshService
    {
        // Rotate this long BEFORE the access token expires.
        private static readonly TimeSpan SafetyBuffer = TimeSpan.FromMinutes(2);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<TokenRefreshService> _logger;

        public TokenRefreshService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<TokenRefreshService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<bool> EnsureValidTokenAsync(HttpContext http)
        {
            var session = http.Session;

            var accessToken = session.GetString(SessionKeys.JwtToken);
            var refreshToken = session.GetString(SessionKeys.RefreshToken);

            // ── No access token at all → not logged in. ───────────
            if (string.IsNullOrEmpty(accessToken))
                return false;

            // When does the access token expire?
            DateTime accessExpiresAt = ParseUtc(session.GetString(SessionKeys.AccessExpiresAt));

            // ── DEGRADED MODE: no refresh token in session. ───────
            // This usually means /api/authtoken/issue-refresh failed
            // at login time. We cannot rotate, but the access token
            // itself is still cryptographically valid. Honour it
            // until its natural expiry, then force re-login cleanly.
            if (string.IsNullOrEmpty(refreshToken))
            {
                if (accessExpiresAt == DateTime.MinValue)
                {
                    // Legacy session with no expiry recorded — trust the
                    // token. The API still validates the JWT lifetime on
                    // every call so this is safe.
                    return true;
                }

                return DateTime.UtcNow < accessExpiresAt;
            }

            // ── NORMAL MODE: still comfortably valid → nothing to do.
            if (accessExpiresAt != DateTime.MinValue &&
                DateTime.UtcNow < accessExpiresAt - SafetyBuffer)
                return true;

            // Refresh token itself expired → cannot continue.
            DateTime refreshExpiresAt = ParseUtc(session.GetString(SessionKeys.RefreshExpiresAt));
            if (refreshExpiresAt != DateTime.MinValue && DateTime.UtcNow >= refreshExpiresAt)
            {
                _logger.LogInformation("Refresh token expired — session cannot be renewed.");
                return false;
            }

            // ── Rotate ────────────────────────────────────────────
            try
            {
                var client = CreateClient();
                var resp = await client.PostAsJsonAsync("/api/authtoken/refresh",
                    new { RefreshToken = refreshToken });

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Token refresh rejected by API ({Status}).", resp.StatusCode);

                    // If the access token still has time, let the user
                    // continue with it instead of immediately booting
                    // them. They'll be forced out only when it expires.
                    if (accessExpiresAt != DateTime.MinValue && DateTime.UtcNow < accessExpiresAt)
                        return true;

                    return false;
                }

                var data = await resp.Content.ReadFromJsonAsync<RefreshTokenResponse>();
                if (data == null || !data.Success || string.IsNullOrEmpty(data.AccessToken))
                {
                    // Same fallback as above — don't kill a still-valid token.
                    return accessExpiresAt != DateTime.MinValue && DateTime.UtcNow < accessExpiresAt;
                }

                // ── Write the rotated pair back into session ──────
                session.SetString(SessionKeys.JwtToken, data.AccessToken);
                session.SetString(SessionKeys.RefreshToken, data.RefreshToken);
                session.SetString(SessionKeys.AccessExpiresAt, data.AccessExpiresAt.ToUniversalTime().ToString("o"));
                session.SetString(SessionKeys.RefreshExpiresAt, data.RefreshExpiresAt.ToUniversalTime().ToString("o"));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token refresh call failed (transient).");

                // Network error: trust the access token if it still has life.
                if (accessExpiresAt == DateTime.MinValue) return true;
                return DateTime.UtcNow < accessExpiresAt;
            }
        }

        private HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(_config["ApiBaseUrl"]!)
            };
        }

        private static DateTime ParseUtc(string? iso)
        {
            if (string.IsNullOrEmpty(iso)) return DateTime.MinValue;
            return DateTime.TryParse(
                iso, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dt)
                ? dt
                : DateTime.MinValue;
        }
    }
}