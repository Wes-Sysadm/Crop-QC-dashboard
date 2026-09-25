namespace CropQc.Web.Services;

/// <summary>Activation controls new workflow writes, never the interpretation of existing inventory.</summary>
public sealed class TruckReceiptOptions
{
    public bool Enabled { get; set; }
    public const string DisabledMessage = "Truck Receipt reconciliation is paused. Existing inventory and reconciliation history remain protected.";
}
