using System.Text;

namespace ProximaLMSAPI.Services
{
    public interface ISmsSender
    {
        Task<bool> SendAsync(string mobileNumber, string message);
    }

    public class SmsSender : ISmsSender
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<SmsSender> _logger;

        public SmsSender(IConfiguration config, IHttpClientFactory http, ILogger<SmsSender> logger)
        {
            _config = config; _http = http; _logger = logger;
        }

        public async Task<bool> SendAsync(string mobileNumber, string message)
        {
            if (string.IsNullOrWhiteSpace(mobileNumber)) return false;
            var s = _config.GetSection("SmsSettings");
            var provider = (s["Provider"] ?? "smsmaa").Trim().ToLowerInvariant();

            try
            {
                return provider == "msg91"
                    ? await SendMsg91(s, mobileNumber, message)
                    : await SendSmsmaa(s, mobileNumber, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMS send failed via {Provider} to {Mobile}", provider, mobileNumber);
                return false;
            }
        }

        // ── MSG91 (sendhttp) ─────────────────────────────────
        private async Task<bool> SendMsg91(IConfigurationSection s, string mobile, string message)
        {
            string authKey = s["AuthKey"] ?? "";
            string sender  = s["SenderId"] ?? "PROXMA";
            string route   = s["MSG91Route"] ?? "4";
            string cc      = s["CountryCode"] ?? "91";
            if (string.IsNullOrWhiteSpace(authKey)) return false;

            string url = "https://api.msg91.com/api/sendhttp.php" +
                         $"?authkey={Uri.EscapeDataString(authKey)}" +
                         $"&mobiles={Uri.EscapeDataString(mobile)}" +
                         $"&message={Uri.EscapeDataString(message)}" +
                         $"&sender={Uri.EscapeDataString(sender)}" +
                         $"&route={route}&country={cc}";

            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.GetAsync(url);
            return resp.IsSuccessStatusCode;
        }

        // ── smsmaa (existing) ────────────────────────────────
        private async Task<bool> SendSmsmaa(IConfigurationSection s, string mobile, string message)
        {
            string username = s["Username"] ?? "";
            string password = s["Password"] ?? "";
            string sender   = s["SenderName"] ?? "KALPRA";
            string route    = s["RouteType"] ?? "1";
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;

            string url = "https://smsmaa.com/SMS_API/sendsms.php" +
                         $"?username={Uri.EscapeDataString(username)}" +
                         $"&password={Uri.EscapeDataString(password)}" +
                         $"&mobile={Uri.EscapeDataString(mobile)}" +
                         $"&sendername={Uri.EscapeDataString(sender)}" +
                         $"&message={Uri.EscapeDataString(message)}" +
                         $"&routetype={route}";

            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.GetAsync(url);
            return resp.IsSuccessStatusCode;
        }
    }
}
