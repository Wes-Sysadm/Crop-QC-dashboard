namespace CropQc.Shared.Inventory;

public static class ReceiptCorrectionTreatmentPolicy
{
    public const string ConfirmationRequired = "Confirm that the additional bins are untreated and are being recorded at the correction time. "
        + "If their treatment history is uncertain or they require an earlier treatment attribution, stop and request evidence review; existing treatments cannot be copied onto them.";
}
