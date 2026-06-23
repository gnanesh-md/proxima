// ============================================================
// ProximaLMSAPI/Services/TokenService.cs
// ------------------------------------------------------------
// Central token generation for the API.
//
// IMPORTANT — single source of truth:
//   Your existing AuthController almost certainly generates the
//   JWT access token inline (in the verify-otp handler). To make
//   refresh work, BOTH the login path and the refresh path must
//   produce IDENTICAL tokens (same signing key, issuer, audience,
//   and claim names). The cleanest fix is to delete the inline
//   JWT code in AuthController and call TokenService.GenerateAccessToken
//   from there too. See 02_Module_README.txt step 3.
//
// Config (API appsettings.json) — see README for the block:
//   Jwt:Key                  256-bit secret (>= 32 chars)
//   Jwt:Issuer
//   Jwt:Audience
//   Jwt:AccessTokenMinutes   default 15
//   Jwt:RefreshTokenDays     default 7
// ============================================================
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ProximaLMSAPI.Services
{
    public interface ITokenService
    {
        /// <summary>Builds a signed 15-minute JWT access token.</summary>
        string GenerateAccessToken(int userId, string email, int roleId, string roleName = "");

        /// <summary>Creates a cryptographically-random opaque refresh token.</summary>
        string GenerateRefreshToken();

        int AccessTokenMinutes { get; }
        int RefreshTokenDays   { get; }
    }

    public class TokenService : ITokenService
    {
        private readonly string _key;
        private readonly string _issuer;
        private readonly string _audience;

        public int AccessTokenMinutes { get; }
        public int RefreshTokenDays   { get; }

        public TokenService(IConfiguration config)
        {
            _key      = config["Jwt:Key"]
                        ?? throw new InvalidOperationException("Jwt:Key is not configured.");
            _issuer   = config["Jwt:Issuer"]   ?? "ProximaLMS";
            _audience = config["Jwt:Audience"] ?? "ProximaLMSUsers";

            AccessTokenMinutes = int.TryParse(config["Jwt:AccessTokenMinutes"], out var m) ? m : 15;
            RefreshTokenDays   = int.TryParse(config["Jwt:RefreshTokenDays"],   out var d) ? d : 7;
        }

        public string GenerateAccessToken(int userId, string email, int roleId, string roleName = "")
        {
            // NOTE: keep these claim TYPE names identical to whatever your
            // existing AuthController used, or the API's JWT validation /
            // any claim reads (User.FindFirst(...)) will break.
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier,   userId.ToString()),
                new Claim(ClaimTypes.Email,            email ?? ""),
                new Claim(ClaimTypes.Role,             roleId.ToString()),
                new Claim("RoleID",                    roleId.ToString()),
                new Claim("RoleName",                  roleName ?? ""),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
            var creds      = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer:             _issuer,
                audience:           _audience,
                claims:             claims,
                notBefore:          DateTime.UtcNow,
                expires:            DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            // 64 random bytes → URL-safe base64 → opaque, unguessable token.
            var bytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                          .Replace("+", "-")
                          .Replace("/", "_")
                          .Replace("=", "");
        }
    }
}
