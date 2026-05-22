namespace CropQc.QcStation.Fta;

public sealed class TestFruitPressureCapture
{
    public const int MaxFruitCount = 25;
    private readonly List<CapturedPressureHistoryEntry> history = [];
    private readonly decimal?[] pressure1Values = new decimal?[MaxFruitCount + 1];
    private readonly decimal?[] pressure2Values = new decimal?[MaxFruitCount + 1];
    private int fruitNumber = 1;

    public int FruitNumber
    {
        get => fruitNumber;
        set
        {
            fruitNumber = Math.Clamp(value, 1, MaxFruitCount);
            SyncCurrentTargetToFruit();
        }
    }

    public string CurrentTargetSlot { get; private set; } = "Pressure 1";
    public decimal? Pressure1Lbs => pressure1Values[FruitNumber];
    public decimal? Pressure2Lbs => pressure2Values[FruitNumber];
    public PressureReading? LastCapturedReading { get; private set; }
    public decimal? AveragePressureLbs => CalculateAverage(Pressure1Lbs, Pressure2Lbs);
    public bool IsSampleComplete => Rows.All(row => row.Status == "Complete");
    public IReadOnlyList<CapturedPressureHistoryEntry> History => history;
    public IReadOnlyList<FruitPressureCaptureRow> Rows =>
        Enumerable.Range(1, MaxFruitCount).Select(CreateRow).ToArray();

    public string Capture(PressureReading reading, PressureCaptureTarget target)
    {
        var slot = ResolveTargetSlot(target);
        if (slot == "Sample Complete")
        {
            return slot;
        }

        if (slot == "Pressure 1")
        {
            pressure1Values[FruitNumber] = reading.ReadingValueLbs;
        }
        else
        {
            pressure2Values[FruitNumber] = reading.ReadingValueLbs;
        }

        LastCapturedReading = reading;
        history.Insert(0, new CapturedPressureHistoryEntry(
            reading.CapturedAt,
            reading.ReadingValueLbs,
            reading.Source,
            FruitNumber,
            slot));

        if (target == PressureCaptureTarget.AutoAdvance)
        {
            AdvanceAfterCapture(slot);
        }
        else
        {
            SyncCurrentTargetToFruit();
        }

        return slot;
    }

    public bool ShouldCaptureReading(PressureReading reading) =>
        LastCapturedReading is null || LastCapturedReading.ReadingId != reading.ReadingId;

    public void ClearCurrentFruit()
    {
        pressure1Values[FruitNumber] = null;
        pressure2Values[FruitNumber] = null;
        LastCapturedReading = null;
        history.RemoveAll(entry => entry.FruitNumber == FruitNumber);
        CurrentTargetSlot = "Pressure 1";
    }

    public void Clear()
    {
        Array.Clear(pressure1Values);
        Array.Clear(pressure2Values);
        LastCapturedReading = null;
        history.Clear();
        FruitNumber = 1;
        CurrentTargetSlot = "Pressure 1";
    }

    private FruitPressureCaptureRow CreateRow(int rowFruitNumber)
    {
        var pressure1 = pressure1Values[rowFruitNumber];
        var pressure2 = pressure2Values[rowFruitNumber];
        return new FruitPressureCaptureRow(
            rowFruitNumber,
            pressure1,
            pressure2,
            CalculateAverage(pressure1, pressure2),
            GetStatus(pressure1, pressure2));
    }

    private string ResolveTargetSlot(PressureCaptureTarget target) =>
        target switch
        {
            PressureCaptureTarget.Pressure1 => "Pressure 1",
            PressureCaptureTarget.Pressure2 => "Pressure 2",
            PressureCaptureTarget.AutoAdvance => CurrentTargetSlot,
            _ => "Pressure 1"
        };

    private void AdvanceAfterCapture(string capturedSlot)
    {
        if (capturedSlot == "Pressure 1")
        {
            CurrentTargetSlot = "Pressure 2";
            return;
        }

        if (FruitNumber < MaxFruitCount)
        {
            FruitNumber++;
            CurrentTargetSlot = "Pressure 1";
            return;
        }

        CurrentTargetSlot = "Sample Complete";
    }

    private void SyncCurrentTargetToFruit()
    {
        CurrentTargetSlot = Pressure1Lbs is null
            ? "Pressure 1"
            : Pressure2Lbs is null
                ? "Pressure 2"
                : FruitNumber < MaxFruitCount
                    ? "Pressure 1"
                    : "Sample Complete";
    }

    private static decimal? CalculateAverage(decimal? pressure1, decimal? pressure2) =>
        pressure1 is not null && pressure2 is not null
            ? Math.Round((pressure1.Value + pressure2.Value) / 2m, 2, MidpointRounding.AwayFromZero)
            : null;

    private static string GetStatus(decimal? pressure1, decimal? pressure2)
    {
        if (pressure1 is null)
        {
            return "Missing P1";
        }

        return pressure2 is null ? "Missing P2" : "Complete";
    }
}
