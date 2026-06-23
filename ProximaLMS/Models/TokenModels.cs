// ============================================================
// ProximaLMS/Models/TokenModels.cs
// ------------------------------------------------------------
// DTOs + a shared session helper for JWT refresh on the MVC side.
// ============================================================
using System;
using System.Text.Json.Serialization;

namespace ProximaLMS.Models
{
    /// <summary>Response from POST /api/authtoken/issue-refresh.</summary>
    public class IssueRefreshResponse
    {
        [JsonPropertyName("success")]            public bool     Success            { get; set; }
        [JsonPropertyName("refreshToken")]       public string   RefreshToken       { get; set; } = "";
        [JsonPropertyName("refreshExpiresAt")]   public DateTime RefreshExpiresAt   { get; set; }
        [JsonPropertyName("accessTokenMinutes")] public int      AccessTokenMinutes { get; set; }
        [JsonPropertyName("message")]            public string?  Message            { get; set; }
    }

    /// <summary>Response from POST /api/authtoken/refresh.</summary>
    public class RefreshTokenResponse
    {
        [JsonPropertyName("success")]          public bool     Success          { get; set; }
        [JsonPropertyName("accessToken")]      public string   AccessToken      { get; set; } = "";
        [JsonPropertyName("accessExpiresAt")]  public DateTime AccessExpiresAt  { get; set; }
        [JsonPropertyName("refreshToken")]     public string   RefreshToken     { get; set; } = "";
        [JsonPropertyName("refreshExpiresAt")] public DateTime RefreshExpiresAt { get; set; }
        [JsonPropertyName("roleId")]           public int      RoleId           { get; set; }
        [JsonPropertyName("message")]          public string?  Message          { get; set; }
    }

    /// <summary>
    /// Centralises every session key the auth/token system touches,
    /// so there is one place to read/write/clear them.
    /// </summary>
    public static class SessionKeys
    {
        public const string JwtToken         = "JwtToken";
        public const string RefreshToken     = "RefreshToken";
        public const string AccessExpiresAt  = "AccessExpiresAt";   // ISO-8601 UTC string
        public const string RefreshExpiresAt = "RefreshExpiresAt";  // ISO-8601 UTC string
        public const string UserID           = "UserID";
        public const string Email            = "Email";
        public const string RoleID           = "RoleID";
        public const string RoleName         = "RoleName";
        public const string Permissions      = "Permissions";
    }
}
