namespace CropQc.QcStation.Fta;

public interface IFtaMessagePump
{
    void ProcessPendingMessages();
}

public sealed class NoOpFtaMessagePump : IFtaMessagePump
{
    public static NoOpFtaMessagePump Instance { get; } = new();

    private NoOpFtaMessagePump()
    {
    }

    public void ProcessPendingMessages()
    {
    }
}
