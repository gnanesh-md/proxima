// ============================================================
// ProximaLMSAPI/Controllers/SystemSettingsController.cs
// ------------------------------------------------------------
// Reads/writes TblSystemSettings. Handles encryption transparently
// for keys flagged IsEncrypted = 1 (e.g. Razorpay live secret).
//
// Endpoints:
//   GET  api/SystemSettings/group/{group}       → all keys in group, secrets masked
//   POST api/SystemSettings/save                → single key/value upsert
//   POST api/SystemSettings/save-bulk           → many at once (used by the UI)
//   GET  api/SystemSettings/reveal/{key}        → returns decrypted value (admin-only path)
// ============================================================
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace ProximaLMSAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemSettingsController : ControllerBase
    {
        private readonly IConfiguration _config;
        public SystemSettingsController(IConfiguration config) => _config = config;
        private IDbConnection Conn() => new MySqlConnection(_config.GetConnectionString("ConnectionString"));


        // ─────────────────────────────────────────────────────
        // GET api/SystemSettings/group/{group}
        // Returns all rows in the group. Encrypted values are
        // returned as "********" — UI uses /reveal to fetch.
        // ─────────────────────────────────────────────────────
        [HttpGet("group/{group}")]
        public async Task<IActionResult> ByGroup(string group)
        {
            using var conn = Conn();
            var rows = (await conn.QueryAsync(
                "SP_SystemSetting_GetByGroup",
                new { p_Group = group },
                commandType: CommandType.StoredProcedure)).ToList();

            var output = rows.Select(r =>
            {
                bool encrypted = Convert.ToInt32(r.IsEncrypted) == 1;
                string? value = (string?)r.SettingValue;
                return new
                {
                    settingKey   = (string)r.SettingKey,
                    settingValue = encrypted && !string.IsNullOrEmpty(value) ? "********" : value,
                    settingGroup = (string)r.SettingGroup,
                    dataType     = (string)r.DataType,
                    isEncrypted  = encrypted,
                    description  = (string?)r.Description,
                    modifiedDate = r.ModifiedDate
                };
            });

            return Ok(new { success = true, data = output });
        }


        // ─────────────────────────────────────────────────────
        // POST api/SystemSettings/save
        // Body: { Key, Value, ModifiedBy }
        // ─────────────────────────────────────────────────────
        [HttpPost("save")]
        public async Task<IActionResult> Save([FromBody] SettingDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Key))
                return BadRequest(new { success = false, message = "Key is required." });

            using var conn = Conn();

            // Decide whether to encrypt before storing.
            var meta = await conn.QuerySingleOrDefaultAsync(
                "SP_SystemSetting_Get",
                new { p_Key = dto.Key },
                commandType: CommandType.StoredProcedure);

            if (meta == null)
                return NotFound(new { success = false, message = "Unknown setting key." });

            string? rawValue = dto.Value;
            bool encrypted  = Convert.ToInt32(meta.IsEncrypted) == 1;

            // Special case: UI sends "********" when the user didn't change
            // the masked value. Don't overwrite the stored ciphertext.
            if (encrypted && rawValue == "********")
                return Ok(new { success = true, message = "Unchanged." });

            string toStore = encrypted && !string.IsNullOrEmpty(rawValue)
                ? AesGcm.EncryptString(rawValue, GetKey())
                : (rawValue ?? "");

            var p = new DynamicParameters();
            p.Add("p_Key",        dto.Key);
            p.Add("p_Value",      toStore);
            p.Add("p_ModifiedBy", dto.ModifiedBy ?? "admin");
            p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);

            await conn.ExecuteAsync("SP_SystemSetting_Save", p, commandType: CommandType.StoredProcedure);
            return Ok(new { success = p.Get<int>("p_ResultCode") > 0 });
        }


        // ─────────────────────────────────────────────────────
        // POST api/SystemSettings/save-bulk
        // Body: { ModifiedBy, Items: [{ Key, Value }, ...] }
        // ─────────────────────────────────────────────────────
        [HttpPost("save-bulk")]
        public async Task<IActionResult> SaveBulk([FromBody] BulkSettingDto req)
        {
            if (req?.Items == null || req.Items.Count == 0)
                return BadRequest(new { success = false, message = "No items to save." });

            using var conn = Conn();
            conn.Open();

            int saved = 0;
            foreach (var item in req.Items)
            {
                var meta = await conn.QuerySingleOrDefaultAsync(
                    "SP_SystemSetting_Get",
                    new { p_Key = item.Key },
                    commandType: CommandType.StoredProcedure);
                if (meta == null) continue;

                bool encrypted = Convert.ToInt32(meta.IsEncrypted) == 1;
                if (encrypted && item.Value == "********") continue;

                string toStore = encrypted && !string.IsNullOrEmpty(item.Value)
                    ? AesGcm.EncryptString(item.Value, GetKey())
                    : (item.Value ?? "");

                var p = new DynamicParameters();
                p.Add("p_Key",        item.Key);
                p.Add("p_Value",      toStore);
                p.Add("p_ModifiedBy", req.ModifiedBy ?? "admin");
                p.Add("p_ResultCode", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await conn.ExecuteAsync("SP_SystemSetting_Save", p,
                    commandType: CommandType.StoredProcedure);
                saved++;
            }

            return Ok(new { success = true, saved });
        }


        // ─────────────────────────────────────────────────────
        // GET api/SystemSettings/reveal/{key}
        // Returns decrypted value. Should be behind an admin role
        // check in production (delegate to the gateway/MVC).
        // ─────────────────────────────────────────────────────
        [HttpGet("reveal/{key}")]
        public async Task<IActionResult> Reveal(string key)
        {
            using var conn = Conn();
            var row = await conn.QuerySingleOrDefaultAsync(
                "SP_SystemSetting_Get",
                new { p_Key = key },
                commandType: CommandType.StoredProcedure);

            if (row == null)
                return NotFound(new { success = false, message = "Unknown key." });

            string stored = (string?)row.SettingValue ?? "";
            bool encrypted = Convert.ToInt32(row.IsEncrypted) == 1;

            string plain = encrypted && !string.IsNullOrEmpty(stored)
                ? AesGcm.DecryptString(stored, GetKey())
                : stored;

            return Ok(new { success = true, key, value = plain });
        }


        // ── Encryption key — derived from JWT key for now. ──
        // For production, set a separate Settings:EncryptionKey.
        private byte[] GetKey()
        {
            string source = _config["Settings:EncryptionKey"]
                          ?? _config["Jwt:Key"]
                          ?? "default-fallback-key-please-change";

            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(source));
        }

        public class SettingDto
        {
            public string  Key        { get; set; } = "";
            public string? Value      { get; set; }
            public string? ModifiedBy { get; set; }
        }

        public class BulkSettingDto
        {
            public string?        ModifiedBy { get; set; }
            public List<SettingDto> Items    { get; set; } = new();
        }
    }


    // ════════════════════════════════════════════════════════════
    // AES-GCM helper. Format on the wire/disk:
    //   base64( nonce[12] || ciphertext || tag[16] )
    // ════════════════════════════════════════════════════════════
    internal static class AesGcm
    {
        public static string EncryptString(string plain, byte[] key)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] data  = Encoding.UTF8.GetBytes(plain);
            byte[] cipher = new byte[data.Length];
            byte[] tag    = new byte[16];

            using var aes = new System.Security.Cryptography.AesGcm(key, 16);
            aes.Encrypt(nonce, data, cipher, tag);

            byte[] all = new byte[nonce.Length + cipher.Length + tag.Length];
            Buffer.BlockCopy(nonce,  0, all, 0,                  nonce.Length);
            Buffer.BlockCopy(cipher, 0, all, nonce.Length,       cipher.Length);
            Buffer.BlockCopy(tag,    0, all, nonce.Length + cipher.Length, tag.Length);
            return Convert.ToBase64String(all);
        }

        public static string DecryptString(string b64, byte[] key)
        {
            try
            {
                byte[] all = Convert.FromBase64String(b64);
                if (all.Length < 12 + 16) return "";

                byte[] nonce = new byte[12];
                byte[] tag   = new byte[16];
                byte[] cipher = new byte[all.Length - 12 - 16];

                Buffer.BlockCopy(all, 0,                     nonce,  0, 12);
                Buffer.BlockCopy(all, 12,                    cipher, 0, cipher.Length);
                Buffer.BlockCopy(all, 12 + cipher.Length,    tag,    0, 16);

                byte[] plain = new byte[cipher.Length];
                using var aes = new System.Security.Cryptography.AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }
    }
}
