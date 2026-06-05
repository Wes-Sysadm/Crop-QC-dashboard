using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CropQc.Web.Services;

public interface IGoogleCredentialStore
{
    Task SaveFromAuthenticationPropertiesAsync(User user, AuthenticationProperties properties, CancellationToken cancellationToken);
    Task<GoogleAccessTokenResult> GetAccessTokenAsync(User user, CancellationToken cancellationToken);
}

public sealed record GoogleAccessTokenResult(string? AccessToken, string? Error, bool ReconnectRequired)
{
    public static GoogleAccessTokenResult Success(string accessToken) => new(accessToken, null, false);
    public static GoogleAccessTokenResult Reconnect(string error) => new(null, error, true);
}

public sealed class GoogleCredentialStore(
    CropQcDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    GoogleAuthenticationOptions authOptions,
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleCredentialStore> logger) : IGoogleCredentialStore
{
    private const string ProviderName = "Google";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("CropQc.GoogleOAuthTokens.v1");

    public async Task SaveFromAuthenticationPropertiesAsync(User user, AuthenticationProperties properties, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var accessToken = properties.GetTokenValue("access_token");
        var refreshToken = properties.GetTokenValue("refresh_token");
        var expiresAtRaw = properties.GetTokenValue("expires_at");
        DateTimeOffset? expiresAt = DateTimeOffset.TryParse(expiresAtRaw, out var parsedExpiresAt) ? parsedExpiresAt : null;
        var scope = properties.GetTokenValue("scope") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(accessToken))
        {
            scope = GmailScopes.Send;
        }
        var now = DateTimeOffset.UtcNow;

        var credential = await dbContext.UserGoogleCredentials
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.Provider == ProviderName, cancellationToken);

        if (credential is null)
        {
            credential = new UserGoogleCredential
            {
                UserId = user.Id,
                Provider = ProviderName,
                CreatedAt = now
            };
            dbContext.UserGoogleCredentials.Add(credential);
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            credential.AccessTokenEncrypted = protector.Protect(accessToken);
        }

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            credential.RefreshTokenEncrypted = protector.Protect(refreshToken);
        }

        credential.Scope = scope;
        credential.ExpiresAt = expiresAt;
        credential.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<GoogleAccessTokenResult> GetAccessTokenAsync(User user, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var credential = await dbContext.UserGoogleCredentials
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.Provider == ProviderName, cancellationToken);

        if (credential is null || !HasGmailScope(credential.Scope))
        {
            return GoogleAccessTokenResult.Reconnect("Gmail permission is required. Please reconnect Google/Gmail.");
        }

        if (!string.IsNullOrWhiteSpace(credential.AccessTokenEncrypted)
            && credential.ExpiresAt is not null
            && credential.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            credential.LastUsedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return GoogleAccessTokenResult.Success(Unprotect(credential.AccessTokenEncrypted));
        }

        if (string.IsNullOrWhiteSpace(credential.RefreshTokenEncrypted))
        {
            return GoogleAccessTokenResult.Reconnect("Gmail permission is required. Please reconnect Google/Gmail.");
        }

        var refreshToken = Unprotect(credential.RefreshTokenEncrypted);
        var refreshed = await RefreshAccessTokenAsync(refreshToken, cancellationToken);
        if (refreshed.AccessToken is null)
        {
            return GoogleAccessTokenResult.Reconnect(refreshed.Error ?? "Gmail permission is required. Please reconnect Google/Gmail.");
        }

        credential.AccessTokenEncrypted = protector.Protect(refreshed.AccessToken);
        credential.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
        credential.Scope = string.IsNullOrWhiteSpace(refreshed.Scope) ? credential.Scope : refreshed.Scope;
        credential.LastUsedAt = DateTimeOffset.UtcNow;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return GoogleAccessTokenResult.Success(refreshed.AccessToken);
    }

    private async Task<GoogleTokenRefreshResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (!authOptions.IsGoogleConfigured)
        {
            return new(null, 0, null, "Google OAuth client is not configured.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = authOptions.ClientId!,
            ["client_secret"] = authOptions.ClientSecret!,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        });

        try
        {
            var client = httpClientFactory.CreateClient("GoogleOAuth");
            using var response = await client.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google access token refresh failed with status {StatusCode}.", response.StatusCode);
                return new(null, 0, null, "Gmail permission is required. Please reconnect Google/Gmail.");
            }

            var body = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken);
            if (string.IsNullOrWhiteSpace(body?.AccessToken))
            {
                return new(null, 0, null, "Gmail permission is required. Please reconnect Google/Gmail.");
            }

            return new(body.AccessToken, body.ExpiresIn <= 0 ? 3600 : body.ExpiresIn, body.Scope, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google access token refresh failed.");
            return new(null, 0, null, "Gmail token refresh failed. Please reconnect Google/Gmail.");
        }
    }

    private string Unprotect(string encrypted)
    {
        try
        {
            return protector.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stored Google token could not be decrypted.");
            throw new InvalidOperationException("Gmail permission is required. Please reconnect Google/Gmail.");
        }
    }

    private static bool HasGmailScope(string scope) =>
        scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, GmailScopes.Send, StringComparison.OrdinalIgnoreCase));

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "UserGoogleCredentials" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "UserId" integer NOT NULL,
                    "Provider" character varying(50) NOT NULL,
                    "AccessTokenEncrypted" character varying(4000) NULL,
                    "RefreshTokenEncrypted" character varying(4000) NULL,
                    "Scope" character varying(1000) NOT NULL DEFAULT '',
                    "ExpiresAt" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "LastUsedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_UserGoogleCredentials" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_UserGoogleCredentials_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserGoogleCredentials_UserId_Provider" ON "UserGoogleCredentials" ("UserId", "Provider");
                """, cancellationToken);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[UserGoogleCredentials]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [UserGoogleCredentials] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] int NOT NULL,
                        [Provider] nvarchar(50) NOT NULL,
                        [AccessTokenEncrypted] nvarchar(4000) NULL,
                        [RefreshTokenEncrypted] nvarchar(4000) NULL,
                        [Scope] nvarchar(1000) NOT NULL CONSTRAINT [DF_UserGoogleCredentials_Scope] DEFAULT N'',
                        [ExpiresAt] datetimeoffset NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        [LastUsedAt] datetimeoffset NULL,
                        CONSTRAINT [PK_UserGoogleCredentials] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_UserGoogleCredentials_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserGoogleCredentials_UserId_Provider' AND object_id = OBJECT_ID(N'[UserGoogleCredentials]')) CREATE UNIQUE INDEX [IX_UserGoogleCredentials_UserId_Provider] ON [UserGoogleCredentials] ([UserId], [Provider]);
                """, cancellationToken);
        }
    }

    private sealed record GoogleTokenRefreshResult(string? AccessToken, int ExpiresIn, string? Scope, string? Error);

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
