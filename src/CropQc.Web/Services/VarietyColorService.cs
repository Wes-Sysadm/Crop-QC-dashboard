using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IVarietyColorService
{
    Task<VarietyColorsAdminViewModel> GetAdminPageAsync(bool canManage, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsAsync(IEnumerable<string> varietyKeys, CancellationToken cancellationToken);
    Task<string?> SaveAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> ResetAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed record VarietyIdentity(string Key, string Name);
public sealed record VarietyColorResolved(string VarietyKey, string VarietyName, string HexColor, bool IsConfigured);

public sealed partial class VarietyColorService(CropQcDbContext dbContext) : IVarietyColorService
{
    private static readonly Regex HexColorRegex = HexColorPattern();
    private static readonly IReadOnlyDictionary<string, VarietyAlias> CanonicalVarietyAliases =
        new[]
        {
            new VarietyAlias("GSMT", "Granny Smith", 2),
            new VarietyAlias("Grannysmith", "Granny Smith", 1),
            new VarietyAlias("Pink", "Pink Lady", 2),
            new VarietyAlias("Pink Lady", "Pink Lady", 0),
            new VarietyAlias("Red", "Red Delicious", 2),
            new VarietyAlias("Red Delicious", "Red Delicious", 0)
        }.ToDictionary(x => AliasLookupKey(x.Alias), StringComparer.OrdinalIgnoreCase);

    public async Task<VarietyColorsAdminViewModel> GetAdminPageAsync(bool canManage, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await ConsolidateAliasConfigurationsAsync(cancellationToken);
        var identities = await GetKnownVarietiesAsync(cancellationToken);
        var configs = await GetResolvedConfigurationRowsAsync(cancellationToken);

        var rows = identities
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Key)
            .Select(identity =>
            {
                configs.TryGetValue(identity.Key, out var config);
                var fallback = FallbackColor(identity.Key);
                return new VarietyColorRowViewModel
                {
                    VarietyKey = identity.Key,
                    VarietyName = config?.VarietyName ?? identity.Name,
                    HexColor = config?.HexColor ?? fallback,
                    FallbackColor = fallback,
                    IsConfigured = config is not null,
                    HistoricalProfileCount = 1
                };
            })
            .ToList();

        return new VarietyColorsAdminViewModel
        {
            Varieties = rows,
            CanManage = canManage
        };
    }

    public async Task<IReadOnlyDictionary<string, VarietyColorResolved>> GetResolvedColorsAsync(IEnumerable<string> varietyKeys, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var keys = varietyKeys
            .Select(x => NormalizeIdentity(x, x).Key)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<string, VarietyColorResolved>(StringComparer.OrdinalIgnoreCase);
        }

        var configs = await GetResolvedConfigurationRowsAsync(cancellationToken);
        var names = (await GetKnownVarietiesAsync(cancellationToken))
            .ToDictionary(x => x.Key, x => x.Name, StringComparer.OrdinalIgnoreCase);

        return keys.ToDictionary(
            key => key,
            key =>
            {
                configs.TryGetValue(key, out var config);
                names.TryGetValue(key, out var name);
                var identity = NormalizeIdentity(config?.VarietyName ?? name ?? key, key);
                return new VarietyColorResolved(
                    key,
                    name ?? identity.Name,
                    config?.HexColor ?? FallbackColor(key),
                    config is not null);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> SaveAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await ConsolidateAliasConfigurationsAsync(cancellationToken);
        var identity = NormalizeIdentity(form.VarietyName, form.VarietyKey);
        var color = NormalizeHex(form.HexColor);
        if (identity.Key.Length == 0)
        {
            return "Variety is required.";
        }

        if (!IsValidHexColor(color))
        {
            return "Enter a valid hex color such as #2F80ED.";
        }

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == changedByEmail, cancellationToken);
        var existing = await dbContext.VarietyColorConfigurations.SingleOrDefaultAsync(x => x.VarietyKey == identity.Key, cancellationToken);
        var before = existing is null ? null : JsonSerializer.Serialize(new { existing.VarietyKey, existing.VarietyName, existing.HexColor });
        if (existing is null)
        {
            existing = new VarietyColorConfiguration
            {
                VarietyKey = identity.Key,
                VarietyName = identity.Name,
                HexColor = color,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedByUserId = user?.Id
            };
            dbContext.VarietyColorConfigurations.Add(existing);
        }
        else
        {
            existing.VarietyName = identity.Name;
            existing.HexColor = color;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedByUserId = user?.Id;
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = before is null ? "create" : "update",
            EntityName = nameof(VarietyColorConfiguration),
            EntityKey = identity.Key,
            UserId = user?.Id,
            BeforeValuesJson = before,
            AfterValuesJson = JsonSerializer.Serialize(new { existing.VarietyKey, existing.VarietyName, existing.HexColor }),
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ResetAsync(VarietyColorForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await ConsolidateAliasConfigurationsAsync(cancellationToken);
        var identity = NormalizeIdentity(form.VarietyName, form.VarietyKey);
        if (identity.Key.Length == 0)
        {
            return "Variety is required.";
        }

        var existing = await dbContext.VarietyColorConfigurations.SingleOrDefaultAsync(x => x.VarietyKey == identity.Key, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == changedByEmail, cancellationToken);
        var before = JsonSerializer.Serialize(new { existing.VarietyKey, existing.VarietyName, existing.HexColor });
        dbContext.VarietyColorConfigurations.Remove(existing);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "reset-to-default",
            EntityName = nameof(VarietyColorConfiguration),
            EntityKey = identity.Key,
            UserId = user?.Id,
            BeforeValuesJson = before,
            AfterValuesJson = JsonSerializer.Serialize(new { VarietyKey = identity.Key, FallbackColor = FallbackColor(identity.Key) }),
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public static VarietyIdentity IdentityFromProfile(FruitProfile profile)
    {
        var name = profile.Name.Trim();
        if (profile.IsOrganic && name.StartsWith("Organic ", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Organic ".Length..].Trim();
        }

        return NormalizeIdentity(name, name);
    }

    public static VarietyIdentity NormalizeIdentity(string? displayName, string? fallbackKey = null)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? (fallbackKey ?? "").Trim() : displayName.Trim();
        if (name.StartsWith("Organic ", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Organic ".Length..].Trim();
        }

        if (TryGetCanonicalAlias(name, out var canonicalAlias)
            || TryGetCanonicalAlias(fallbackKey, out canonicalAlias))
        {
            name = canonicalAlias.CanonicalName;
        }

        var key = NormalizeVarietyKey(name.Length == 0 ? fallbackKey : name);
        return new VarietyIdentity(key, name.Length == 0 ? key : name);
    }

    public static string NormalizeVarietyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var chars = value.Trim().ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return Regex.Replace(new string(chars), "_+", "_").Trim('_');
    }

    public static string FallbackColor(string varietyKey)
    {
        var palette = new[]
        {
            "#2F80ED", "#27AE60", "#F2994A", "#9B51E0", "#EB5757", "#00A5B5",
            "#7B61FF", "#219653", "#D97706", "#33658A", "#6C9A8B", "#A23E48"
        };
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(varietyKey.ToUpperInvariant()));
        return palette[hash[0] % palette.Length];
    }

    public static string NormalizeHex(string? value)
    {
        var color = (value ?? "").Trim();
        if (color.Length == 6 && color.All(Uri.IsHexDigit))
        {
            color = "#" + color;
        }

        return color.ToUpperInvariant();
    }

    public static bool IsValidHexColor(string? value) =>
        value is not null && HexColorRegex.IsMatch(value);

    private async Task<IReadOnlyList<VarietyIdentity>> GetKnownVarietiesAsync(CancellationToken cancellationToken)
    {
        var profiles = await dbContext.FruitProfiles.AsNoTracking().ToListAsync(cancellationToken);
        var identities = profiles.Select(IdentityFromProfile).ToList();
        var adjustmentVarieties = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.VarietyCode != null && x.VarietyCode != "")
            .Select(x => x.VarietyCode!)
            .Distinct()
            .ToListAsync(cancellationToken);
        identities.AddRange(adjustmentVarieties.Select(x => NormalizeIdentity(x, x)));

        return identities
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(y => y.Name.Length).First())
            .ToList();
    }

    private async Task<Dictionary<string, VarietyColorConfiguration>> GetResolvedConfigurationRowsAsync(CancellationToken cancellationToken)
    {
        var configs = await dbContext.VarietyColorConfigurations.AsNoTracking().ToListAsync(cancellationToken);
        return configs
            .GroupBy(x => NormalizeConfigurationIdentity(x).Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => PickPreferredConfiguration(x.ToList()),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task ConsolidateAliasConfigurationsAsync(CancellationToken cancellationToken)
    {
        var configs = await dbContext.VarietyColorConfigurations.ToListAsync(cancellationToken);
        var groups = configs
            .Where(HasKnownAlias)
            .GroupBy(x => NormalizeConfigurationIdentity(x).Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 || group.Any(row =>
            {
                var identity = NormalizeConfigurationIdentity(row);
                return !row.VarietyKey.Equals(identity.Key, StringComparison.OrdinalIgnoreCase)
                    || !row.VarietyName.Equals(identity.Name, StringComparison.Ordinal);
            }))
            .ToList();

        if (groups.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var group in groups)
        {
            var rows = group.ToList();
            var identity = NormalizeConfigurationIdentity(rows[0]);
            var winner = PickPreferredConfiguration(rows);
            var before = JsonSerializer.Serialize(rows
                .OrderBy(x => x.VarietyKey)
                .ThenBy(x => x.VarietyName)
                .Select(x => new { x.VarietyKey, x.VarietyName, x.HexColor, x.CreatedAt, x.UpdatedAt }));

            foreach (var row in rows.Where(x => x.Id != winner.Id))
            {
                dbContext.VarietyColorConfigurations.Remove(row);
            }

            winner.VarietyKey = identity.Key;
            winner.VarietyName = identity.Name;
            winner.UpdatedAt = now;

            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "consolidate-variety-alias",
                EntityName = nameof(VarietyColorConfiguration),
                EntityKey = identity.Key,
                BeforeValuesJson = before,
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    CanonicalVarietyKey = identity.Key,
                    CanonicalVarietyName = identity.Name,
                    ResultingColor = winner.HexColor,
                    MigrationActor = "system:variety-alias-consolidation"
                }),
                SourceApplication = "CropQc.Web",
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static VarietyColorConfiguration PickPreferredConfiguration(IReadOnlyList<VarietyColorConfiguration> rows) =>
        rows
            .OrderByDescending(IsCanonicalConfiguration)
            .ThenByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenBy(x => AliasPriority(x.VarietyName))
            .ThenBy(x => AliasPriority(x.VarietyKey))
            .ThenBy(x => x.VarietyName)
            .ThenBy(x => x.VarietyKey)
            .First();

    private static VarietyIdentity NormalizeConfigurationIdentity(VarietyColorConfiguration configuration)
    {
        if (TryGetCanonicalAlias(configuration.VarietyName, out var alias)
            || TryGetCanonicalAlias(configuration.VarietyKey, out alias))
        {
            return NormalizeIdentity(alias.CanonicalName, alias.CanonicalName);
        }

        return new VarietyIdentity(configuration.VarietyKey, configuration.VarietyName);
    }

    private static bool HasKnownAlias(VarietyColorConfiguration configuration) =>
        TryGetCanonicalAlias(configuration.VarietyName, out _)
        || TryGetCanonicalAlias(configuration.VarietyKey, out _);

    private static bool IsCanonicalConfiguration(VarietyColorConfiguration configuration)
    {
        var identity = NormalizeConfigurationIdentity(configuration);
        return configuration.VarietyKey.Equals(identity.Key, StringComparison.OrdinalIgnoreCase)
            || configuration.VarietyName.Trim().Equals(identity.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int AliasPriority(string? value) =>
        TryGetCanonicalAlias(value, out var alias) ? alias.Priority : 100;

    private static bool TryGetCanonicalAlias(string? value, out VarietyAlias alias)
    {
        alias = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return CanonicalVarietyAliases.TryGetValue(AliasLookupKey(value), out alias);
    }

    private static string AliasLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private readonly record struct VarietyAlias(string Alias, string CanonicalName, int Priority);

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "VarietyColorConfigurations" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "VarietyKey" character varying(100) NOT NULL,
                    "VarietyName" character varying(150) NOT NULL,
                    "HexColor" character varying(7) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "UpdatedByUserId" integer NULL,
                    CONSTRAINT "PK_VarietyColorConfigurations" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_VarietyColorConfigurations_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_VarietyColorConfigurations_VarietyKey" ON "VarietyColorConfigurations" ("VarietyKey");
                """, cancellationToken);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[VarietyColorConfigurations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [VarietyColorConfigurations] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [VarietyKey] nvarchar(100) NOT NULL,
                        [VarietyName] nvarchar(150) NOT NULL,
                        [HexColor] nvarchar(7) NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        [UpdatedByUserId] int NULL,
                        CONSTRAINT [PK_VarietyColorConfigurations] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_VarietyColorConfigurations_Users_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
                    );
                    CREATE UNIQUE INDEX [IX_VarietyColorConfigurations_VarietyKey] ON [VarietyColorConfigurations] ([VarietyKey]);
                END
                """, cancellationToken);
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorPattern();
}
