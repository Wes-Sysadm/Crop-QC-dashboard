using CropQc.QcStation.Fta;

namespace CropQc.QcStation.WinForms;

public sealed class WinFormsFtaMessagePump : IFtaMessagePump
{
    public void ProcessPendingMessages() => Application.DoEvents();
}
