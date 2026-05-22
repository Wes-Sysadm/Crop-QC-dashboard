namespace CropQc.QcStation.Fta;

public static class FtaStationServiceFactory
{
    public static IFtaStationService Create(StationConfiguration configuration, IFtaMessagePump? messagePump = null)
    {
        if (configuration.FtaMode == FtaMode.RealDll)
        {
            var dllReader = new FtaDllPressureReader(configuration, messagePump: messagePump);
            return new FtaStationService(configuration, dllReader, dllReader);
        }

        var mockReader = new MockFtaPressureReader(configuration.StationName);
        return new FtaStationService(configuration, mockReader, mockReader, mockReader);
    }
}
