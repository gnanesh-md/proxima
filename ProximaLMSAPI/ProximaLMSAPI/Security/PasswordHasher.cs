// ============================================================
// ProximaLMSAPI/Security/PasswordHasher.cs
// ------------------------------------------------------------
// Central password hashing.
//   • New hashes  →  BCrypt (work factor 12)
//   • Verification supports both BCrypt AND legacy salted SHA-256,
//     so existing users can keep logging in. A successful legacy
//     verify returns SuccessNeedsRehash so the caller can transparently
//     upgrade the stored hash to BCrypt on that login.
//
// Requires NuGet package: BCrypt.Net-Next
//   dotnet add package BCrypt.Net-Next
// ============================================================
using Org.BouncyCastle.Crypto.Generators;
using System;
using System.Security.Cryptography;
using System.Text;

namespace ProximaLMSAPI.Security
{
    public enum PasswordVerifyResult
    {
        Failed = 0,
        Success = 1,
        SuccessNeedsRehash = 2   // legacy SHA-256 verified — upgrade to BCrypt
    }

    public static class PasswordHasher
    {
        // ~250 ms per verify on modern x86_64 hardware (Q2 2026).
        // Increase as hardware gets faster. Existing hashes are not
        // affected when this changes — only new hashes use the new factor.
        private const int BCryptWorkFactor = 12;

        // ─────────────────────────────────────────────────────────
        // Hash a NEW password. Always BCrypt.
        // ─────────────────────────────────────────────────────────
        public static string Hash(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                throw new ArgumentException("Password is required.", nameof(plain));

            return BCrypt.Net.BCrypt.HashPassword(plain, BCryptWorkFactor);
        }

        // ─────────────────────────────────────────────────────────
        // Verify a candidate against a stored hash. Supports both
        // BCrypt (new) and salted SHA-256 (legacy).
        // ─────────────────────────────────────────────────────────
        public static PasswordVerifyResult Verify(
            string plain,
            string storedHash,
            string legacySalt)
        {
            if (string.IsNullOrEmpty(plain) || string.IsNullOrEmpty(storedHash))
                return PasswordVerifyResult.Failed;

            // BCrypt hashes always start with $2a$, $2b$, or $2y$.
            if (storedHash.StartsWith("$2", StringComparison.Ordinal))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(plain, storedHash)
                        ? PasswordVerifyResult.Success
                        : PasswordVerifyResult.Failed;
                }
                catch (BCrypt.Net.SaltParseException)
                {
                    // Stored hash looked like BCrypt but isn't valid.
                    // Treat as failure rather than crashing the request.
                    return PasswordVerifyResult.Failed;
                }
            }

            // Legacy path — SHA-256 over (password + salt), base64.
            // Constant-time compare to prevent timing side-channels.
            string legacyHash = LegacySha256(plain, legacySalt ?? string.Empty);
            return FixedTimeEquals(legacyHash, storedHash)
                ? PasswordVerifyResult.SuccessNeedsRehash
                : PasswordVerifyResult.Failed;
        }

        // ─────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────
        private static string LegacySha256(string plain, string salt)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(plain + salt);
            return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var ab = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (ab.Length != bb.Length) return false;
            return CryptographicOperations.FixedTimeEquals(ab, bb);
        }
    }
}
