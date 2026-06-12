namespace CropQc.QcStation.Fta;

public sealed class TestFruitPressureCapture
{
    public const int MaxFruitCount = 50;
    private readonly List<CapturedPressureHistoryEntry> history = [];
    private readonly decimal?[] pressure1Values = new decimal?[MaxFruitCount + 1];
    private readonly decimal?[] pressure2Values = new decimal?[MaxFruitCount + 1];
    private int fruitNumber = 1;
    private int targetFruitCount = 25;

    public int TargetFruitCount
    {
        get => targetFruitCount;
        private set => targetFruitCount = Math.Clamp(value, 1, MaxFruitCount);
    }

    public int FruitNumber
    {
        get => fruitNumber;
        set
        {
            fruitNumber = Math.Clamp(value, 1, TargetFruitCount);
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
        Enumerable.Range(1, TargetFruitCount).Select(CreateRow).ToArray();

    public void SetTargetFruitCount(int targetFruitCount)
    {
        if (targetFruitCount is not (10 or 25 or 50))
        {
            targetFruitCount = 10;
        }

        TargetFruitCount = targetFruitCount;
        FruitNumber = Math.Min(FruitNumber, TargetFruitCount);
        SyncCurrentTargetToFruit();
    }

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

    public void SetPressures(int rowNumber, decimal? pressure1Lbs, decimal? pressure2Lbs)
    {
        if (rowNumber is < 1 or > MaxFruitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(rowNumber), "Row number must be between 1 and 50.");
        }

        pressure1Values[rowNumber] = pressure1Lbs;
        pressure2Values[rowNumber] = pressure2Lbs;
        if (rowNumber == FruitNumber)
        {
            SyncCurrentTargetToFruit();
        }
    }

    public void LoadRows(IEnumerable<FruitPressureCaptureRow> rows, int targetFruitCount)
    {
        Clear();
        SetTargetFruitCount(targetFruitCount);
        foreach (var row in rows)
        {
            if (row.FruitNumber <= MaxFruitCount)
            {
                SetPressures(row.FruitNumber, row.Pressure1Lbs, row.Pressure2Lbs);
            }
        }

        var next = Rows.FirstOrDefault(row => row.Status != "Complete");
        FruitNumber = next?.FruitNumber ?? TargetFruitCount;
        CurrentTargetSlot = next is null ? "Sample Complete" : next.Status == "Missing P1" ? "Pressure 1" : "Pressure 2";
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

        if (FruitNumber < TargetFruitCount)
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
                : FruitNumber < TargetFruitCount
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
