// ============================================================
// ProximaLMSAPI/Models/EncryptHelper.cs
// ------------------------------------------------------------
// DEPRECATED — kept only so any pre-existing caller still
// compiles. New code MUST call ProximaLMSAPI.Security.PasswordHasher
// directly.
//
// What changed:
//   • HashPassword now delegates to BCrypt (PasswordHasher.Hash).
//   • VerifyPassword detects BCrypt vs legacy SHA-256 and verifies
//     either. NOTE: this overload has no salt parameter, so legacy
//     verification will only work for hashes that were originally
//     computed without a per-user salt. For the salted path,
//     call PasswordHasher.Verify(plain, hash, salt) directly.
//   • Encode / Decode (base64) kept unchanged because they may be
//     used for non-password data (URL tokens, payload obfuscation).
//     Do NOT use them for passwords.
// ============================================================
using System;
using System.Text;
using ProximaLMSAPI.Security;

namespace ProximaLMSAPI.Models
{
    [Obsolete("Use ProximaLMSAPI.Security.PasswordHasher for password operations.")]
    public class EncryptHelper
    {
        /// <summary>Base64 Encode. Not for passwords — use PasswordHasher.Hash().</summary>
        public static string Encode(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }

        /// <summary>Base64 Decode. Not for passwords.</summary>
        public static string Decode(string encodedText)
        {
            if (string.IsNullOrEmpty(encodedText)) return string.Empty;
            return Encoding.UTF8.GetString(Convert.FromBase64String(encodedText));
        }

        /// <summary>
        /// DEPRECATED. Hash a password. Returns a BCrypt hash now (60 chars).
        /// Prefer <see cref="ProximaLMSAPI.Security.PasswordHasher.Hash"/>.
        /// </summary>
        [Obsolete("Use PasswordHasher.Hash instead.")]
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;
            return PasswordHasher.Hash(password);
        }

        /// <summary>
        /// DEPRECATED. Verifies BCrypt OR unsalted legacy SHA-256.
        /// For salted SHA-256 verification (typical user records),
        /// use <see cref="ProximaLMSAPI.Security.PasswordHasher.Verify"/>
        /// passing the per-user salt.
        /// </summary>
        [Obsolete("Use PasswordHasher.Verify(plain, hash, salt) instead.")]
        public static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
                return false;

            // Routes through PasswordHasher; passes empty salt so it falls
            // through to the legacy *unsalted* SHA-256 path if the hash
            // is not BCrypt. Returns true on Success or SuccessNeedsRehash.
            var result = PasswordHasher.Verify(password, storedHash, string.Empty);
            return result == PasswordVerifyResult.Success
                || result == PasswordVerifyResult.SuccessNeedsRehash;
        }
    }
}
