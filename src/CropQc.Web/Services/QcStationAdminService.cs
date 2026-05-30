using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Security;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IQcStationAdminService
{
    Task<QcStationsPageViewModel> GetStationsAsync(string? search, string? warehouseCode, string activeFilter, CancellationToken cancellationToken);
    Task<(string? Error, QcStationConfigDownload? Download)> CreateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SetActiveAsync(int id, bool isActive, string changedByEmail, CancellationToken cancellationToken);
    Task<(string? Error, QcStationConfigDownload? Download)> RotateKeyAsync(int id, string changedByEmail, CancellationToken cancellationToken);
}

public sealed record QcStationConfigDownload(string FileName, string Json);

public sealed class QcStationAdminService(CropQcDbContext dbContext, IConfiguration configuration) : IQcStationAdminService
{
    public async Task<QcStationsPageViewModel> GetStationsAsync(string? search, string? warehouseCode, string activeFilter, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var query = dbContext.QcStations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.StationCode.Contains(term) || x.StationName.Contains(term) || x.Name.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(warehouseCode))
        {
            query = query.Where(x => x.WarehouseCode == warehouseCode || (x.Warehouse != null && x.Warehouse.Code == warehouseCode));
        }

        query = activeFilter switch
        {
            "Inactive" => query.Where(x => !x.IsActive),
            "All" => query,
            _ => query.Where(x => x.IsActive)
        };

        var stations = await query
            .OrderBy(x => x.WarehouseCode)
            .ThenBy(x => x.StationCode)
            .Select(x => new QcStationListItemViewModel(
                x.Id,
                x.StationName == "" ? x.Name : x.StationName,
                x.StationCode,
                x.WarehouseCode ?? (x.Warehouse == null ? "" : x.Warehouse.Code),
                x.Description,
                x.IsActive,
                x.ApiKeyLastFour,
                x.ApiKeyCreatedAt,
                x.ApiKeyRotatedAt,
                x.LastSeenAt,
                x.LastSeenIp,
                x.LastSyncAt))
            .ToListAsync(cancellationToken);

        return new QcStationsPageViewModel
        {
            Stations = stations,
            Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken),
            Search = search,
            WarehouseCode = warehouseCode,
            ActiveFilter = string.IsNullOrWhiteSpace(activeFilter) ? "Active" : activeFilter
        };
    }

    public async Task<(string? Error, QcStationConfigDownload? Download)> CreateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var error = await ValidateFormAsync(form, isCreate: true, cancellationToken);
        if (error is not null)
        {
            return (error, null);
        }

        var rawKey = QcStationApiKeyValidator.GenerateApiKey();
        var now = DateTimeOffset.UtcNow;
        var station = new QcStation
        {
            StationName = form.StationName.Trim(),
            Name = form.StationName.Trim(),
            StationCode = form.StationCode.Trim(),
            WarehouseCode = form.WarehouseCode.Trim(),
            WarehouseId = await FindWarehouseIdAsync(form.WarehouseCode, cancellationToken),
            Description = form.Description?.Trim(),
            Notes = form.Notes?.Trim(),
            IsActive = form.IsActive,
            ApiKeyHash = QcStationApiKeyValidator.HashApiKey(rawKey),
            ApiKeyLastFour = rawKey[^4..],
            ApiKeyCreatedAt = now,
            CreatedAt = now,
            CreatedByUserId = await FindUserIdAsync(changedByEmail, cancellationToken)
        };

        dbContext.QcStations.Add(station);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync("create", station, changedByEmail, null, "QC Station created and API key generated.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (null, BuildConfigDownload(station, rawKey));
    }

    public async Task<string?> UpdateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        if (form.Id is null)
        {
            return "Station id is required.";
        }

        var error = await ValidateFormAsync(form, isCreate: false, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var station = await dbContext.QcStations.FindAsync([form.Id.Value], cancellationToken);
        if (station is null)
        {
            return "Station not found.";
        }

        var before = JsonSerializer.Serialize(ToAuditSnapshot(station));
        station.StationName = form.StationName.Trim();
        station.Name = station.StationName;
        station.StationCode = form.StationCode.Trim();
        station.WarehouseCode = form.WarehouseCode.Trim();
        station.WarehouseId = await FindWarehouseIdAsync(form.WarehouseCode, cancellationToken);
        station.Description = form.Description?.Trim();
        station.Notes = form.Notes?.Trim();
        station.IsActive = form.IsActive;
        station.UpdatedAt = DateTimeOffset.UtcNow;

        await AddAuditAsync("update", station, changedByEmail, before, JsonSerializer.Serialize(ToAuditSnapshot(station)), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SetActiveAsync(int id, bool isActive, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var station = await dbContext.QcStations.FindAsync([id], cancellationToken);
        if (station is null)
        {
            return "Station not found.";
        }

        var before = JsonSerializer.Serialize(ToAuditSnapshot(station));
        station.IsActive = isActive;
        station.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync(isActive ? "reactivate" : "deactivate", station, changedByEmail, before, JsonSerializer.Serialize(ToAuditSnapshot(station)), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(string? Error, QcStationConfigDownload? Download)> RotateKeyAsync(int id, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var station = await dbContext.QcStations.FindAsync([id], cancellationToken);
        if (station is null)
        {
            return ("Station not found.", null);
        }

        var rawKey = QcStationApiKeyValidator.GenerateApiKey();
        var before = JsonSerializer.Serialize(ToAuditSnapshot(station));
        station.ApiKeyHash = QcStationApiKeyValidator.HashApiKey(rawKey);
        station.ApiKeyLastFour = rawKey[^4..];
        station.ApiKeyRotatedAt = DateTimeOffset.UtcNow;
        station.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync("rotate-key", station, changedByEmail, before, "QC Station API key rotated. Raw key not stored.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (null, BuildConfigDownload(station, rawKey));
    }

    private async Task<string?> ValidateFormAsync(QcStationForm form, bool isCreate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.StationName) || string.IsNullOrWhiteSpace(form.StationCode) || string.IsNullOrWhiteSpace(form.WarehouseCode))
        {
            return "Station name, station code, and warehouse are required.";
        }

        var id = form.Id ?? 0;
        if (await dbContext.QcStations.AnyAsync(x => x.StationCode == form.StationCode.Trim() && x.Id != id, cancellationToken))
        {
            return "Station code must be unique.";
        }

        if (!await dbContext.Warehouses.AnyAsync(x => x.Code == form.WarehouseCode.Trim(), cancellationToken))
        {
            return "Warehouse code is not valid.";
        }

        if (!isCreate && form.Id is null)
        {
            return "Station id is required.";
        }

        return null;
    }

    private QcStationConfigDownload BuildConfigDownload(QcStation station, string rawKey)
    {
        var config = new
        {
            StationName = station.StationName == "" ? station.Name : station.StationName,
            WarehouseCode = station.WarehouseCode ?? "",
            FtaMode = "RealDll",
            FtaDllPath = @"C:\Windows\SysWOW64",
            FtaDllFileName = "FTA_DLL.dll",
            FtaInitializationMode = "FTAInit",
            FtaConfigPath = @"C:\Program Files\FTADLL\FTA_DLL.CFG",
            FtaReadingTimeoutSeconds = 60,
            FtaWorkingDirectory = @"C:\Program Files (x86)\FTAWin",
            ComPort = (string?)null,
            ApiBaseUrl = configuration["QcStation:ApiBaseUrl"] ?? "https://crop-qc-dashboard.onrender.com",
            QcStationCode = station.StationCode,
            QcStationApiKey = rawKey,
            LocalDataPath = "local-data"
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        return new QcStationConfigDownload($"{station.StationCode}-qcstation.settings.json", json);
    }

    private async Task<int?> FindWarehouseIdAsync(string warehouseCode, CancellationToken cancellationToken) =>
        await dbContext.Warehouses.Where(x => x.Code == warehouseCode.Trim()).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

    private async Task<int?> FindUserIdAsync(string email, CancellationToken cancellationToken) =>
        await dbContext.Users.Where(x => x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

    private async Task AddAuditAsync(string action, QcStation station, string by, string? before, string? after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(QcStation),
            EntityKey = station.Id.ToString(),
            UserId = await FindUserIdAsync(by, cancellationToken),
            BeforeValuesJson = before,
            AfterValuesJson = after,
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static object ToAuditSnapshot(QcStation station) => new
    {
        station.Id,
        station.StationName,
        station.StationCode,
        station.WarehouseCode,
        station.Description,
        station.IsActive,
        station.ApiKeyLastFour,
        station.LastSeenAt,
        station.LastSyncAt
    };

    private async Task EnsureQcStationColumnsAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "StationName" character varying(150) NOT NULL DEFAULT '';
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "WarehouseCode" character varying(25) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "Description" character varying(500) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyHash" character varying(200) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyLastFour" character varying(12) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyCreatedAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyRotatedAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "LastSeenIp" character varying(100) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "LastSyncAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT '2026-01-01 00:00:00+00';
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "CreatedByUserId" integer NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "Notes" character varying(1000) NULL;
                UPDATE "QcStations" SET "StationName" = "Name" WHERE "StationName" = '';
                """, cancellationToken);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('QcStations', 'StationName') IS NULL ALTER TABLE [QcStations] ADD [StationName] nvarchar(150) NOT NULL CONSTRAINT [DF_QcStations_StationName] DEFAULT N'';
                IF COL_LENGTH('QcStations', 'WarehouseCode') IS NULL ALTER TABLE [QcStations] ADD [WarehouseCode] nvarchar(25) NULL;
                IF COL_LENGTH('QcStations', 'Description') IS NULL ALTER TABLE [QcStations] ADD [Description] nvarchar(500) NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyHash') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyHash] nvarchar(200) NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyLastFour') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyLastFour] nvarchar(12) NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyCreatedAt') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyCreatedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyRotatedAt') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyRotatedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'LastSeenAt') IS NULL ALTER TABLE [QcStations] ADD [LastSeenAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'LastSeenIp') IS NULL ALTER TABLE [QcStations] ADD [LastSeenIp] nvarchar(100) NULL;
                IF COL_LENGTH('QcStations', 'LastSyncAt') IS NULL ALTER TABLE [QcStations] ADD [LastSyncAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'CreatedAt') IS NULL ALTER TABLE [QcStations] ADD [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_QcStations_CreatedAt] DEFAULT '2026-01-01T00:00:00+00:00';
                IF COL_LENGTH('QcStations', 'UpdatedAt') IS NULL ALTER TABLE [QcStations] ADD [UpdatedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'CreatedByUserId') IS NULL ALTER TABLE [QcStations] ADD [CreatedByUserId] int NULL;
                IF COL_LENGTH('QcStations', 'Notes') IS NULL ALTER TABLE [QcStations] ADD [Notes] nvarchar(1000) NULL;
                UPDATE [QcStations] SET [StationName] = [Name] WHERE [StationName] = N'';
                """, cancellationToken);
        }
    }
}
