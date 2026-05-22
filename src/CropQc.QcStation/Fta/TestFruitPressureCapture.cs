namespace CropQc.QcStation.Fta;

public sealed class TestFruitPressureCapture
{
    private readonly List<CapturedPressureHistoryEntry> history = [];

    public int FruitNumber { get; set; } = 1;
    public decimal? Pressure1Lbs { get; private set; }
    public decimal? Pressure2Lbs { get; private set; }
    public PressureReading? LastCapturedReading { get; private set; }
    public decimal? AveragePressureLbs => Pressure1Lbs is not null && Pressure2Lbs is not null
        ? Math.Round((Pressure1Lbs.Value + Pressure2Lbs.Value) / 2m, 2, MidpointRounding.AwayFromZero)
        : null;
    public IReadOnlyList<CapturedPressureHistoryEntry> History => history;

    public string Capture(PressureReading reading, PressureCaptureTarget target)
    {
        var slot = ResolveTargetSlot(target);
        if (slot == "Pressure 1")
        {
            Pressure1Lbs = reading.ReadingValueLbs;
        }
        else
        {
            Pressure2Lbs = reading.ReadingValueLbs;
        }

        LastCapturedReading = reading;
        history.Insert(0, new CapturedPressureHistoryEntry(
            reading.CapturedAt,
            reading.ReadingValueLbs,
            reading.Source,
            FruitNumber,
            slot));
        return slot;
    }

    public void Clear()
    {
        Pressure1Lbs = null;
        Pressure2Lbs = null;
        LastCapturedReading = null;
        history.Clear();
    }

    private string ResolveTargetSlot(PressureCaptureTarget target) =>
        target switch
        {
            PressureCaptureTarget.Pressure1 => "Pressure 1",
            PressureCaptureTarget.Pressure2 => "Pressure 2",
            PressureCaptureTarget.AutoAdvance when Pressure1Lbs is null => "Pressure 1",
            PressureCaptureTarget.AutoAdvance => "Pressure 2",
            _ => "Pressure 1"
        };
}
