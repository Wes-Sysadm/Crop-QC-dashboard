namespace CropQc.Web.Services;

public sealed class RunSheetReconciliationOptions
{
    public const int DefaultCropYear = 2026;
    public const int DefaultPendingHours = 24;
    public const int DefaultRefreshMinutes = 15;
    public const int DefaultMaximumRows = 5000;
    public const int DefaultHeaderSearchRows = 25;

    public bool Enabled { get; set; }
    public int CropYear { get; set; } = DefaultCropYear;
    public int PendingHours { get; set; } = DefaultPendingHours;
    public int RefreshMinutes { get; set; } = DefaultRefreshMinutes;
    public int MaximumRows { get; set; } = DefaultMaximumRows;
    public int HeaderSearchRows { get; set; } = DefaultHeaderSearchRows;
    public string EbsSpreadsheetId { get; set; } = "1ml4Hslmd9fzkv2wlMvB99qLQ-mSN2kUAljwfS_EN4Wo";
    public string EbsSheetName { get; set; } = "DAILY";
    public string WpSpreadsheetId { get; set; } = "1F8hrn1Gl6CeXhbhPcNGWJA7vEQJW1HzRcTqbF-LgkcA";
    public string WpSheetName { get; set; } = "DAILY APPLE/PEAR";
    public Dictionary<string, string> SalesDeskMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DMX"] = "Domex",
        ["HB"] = "Honey Bear",
        ["VIVA"] = "Viva Tierra"
    };

    public TimeSpan PendingWindow => TimeSpan.FromHours(Math.Clamp(PendingHours, 1, 168));
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(Math.Clamp(RefreshMinutes, 1, 1440));
    public int BoundedMaximumRows => Math.Clamp(MaximumRows, 100, 25000);
    public int BoundedHeaderSearchRows => Math.Clamp(HeaderSearchRows, 1, 100);
}
