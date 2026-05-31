using System.Net;
using System.Text;
using CropQc.QcStation.Api;
using CropQc.Shared.Security;

namespace CropQc.Api.Tests.QcStation;

public sealed class QcStationApiClientTests
{
    [Fact]
    public async Task GetTodaySamplesAsync_SendsStationHeaders()
    {
        var handler = new CapturingHandler("""[]""");
        var client = QcStationApiClient.Create(handler, "https://dashboard.example", "MCD-12", "secret-key", "MCD 12");

        await client.GetTodaySamplesAsync("MCD");

        Assert.Equal("MCD-12", handler.LastRequest?.Headers.GetValues(QcStationApiKeyValidator.StationCodeHeaderName).Single());
        Assert.Equal("secret-key", handler.LastRequest?.Headers.GetValues(QcStationApiKeyValidator.HeaderName).Single());
        Assert.Equal("MCD 12", handler.LastRequest?.Headers.GetValues("X-QC-STATION-NAME").Single());
    }

    [Fact]
    public async Task GetSampleDetailAsync_SendsStationHeaders()
    {
        var handler = new CapturingHandler(SampleDetailJson);
        var client = QcStationApiClient.Create(handler, "https://dashboard.example", "WP-FTA-01", "secret-key");

        await client.GetSampleDetailAsync(123);

        Assert.Equal("WP-FTA-01", handler.LastRequest?.Headers.GetValues(QcStationApiKeyValidator.StationCodeHeaderName).Single());
        Assert.Equal("secret-key", handler.LastRequest?.Headers.GetValues(QcStationApiKeyValidator.HeaderName).Single());
        Assert.Equal("/api/qc-station/samples/123/pressure", handler.LastRequest?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task SavePressuresAsync_SendsStationHeaders()
    {
        var handler = new CapturingHandler(SampleDetailJson);
        var client = QcStationApiClient.Create(handler, "https://dashboard.example", "DH-FTA-02", "secret-key");

        await client.SavePressuresAsync(123, [new QcStationPressureRowUpdate(1, 12.3m, 13.4m)]);

        Assert.Equal("DH-FTA-02", handler.LastRequest?.Headers.GetValues(QcStationApiKeyValidator.StationCodeHeaderName).Single());
        Assert.Equal("secret-key", handler.LastRequest?.Headers.GetValues(QcStationApiKeyValidator.HeaderName).Single());
        Assert.Equal(HttpMethod.Put, handler.LastRequest?.Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetSampleDetailAsync_ThrowsAuthorizationExceptionForAuthFailures(HttpStatusCode statusCode)
    {
        var handler = new CapturingHandler("""not authorized""", statusCode);
        var client = QcStationApiClient.Create(handler, "https://dashboard.example", "MCD-12", "secret-key");

        var ex = await Assert.ThrowsAsync<QcStationAuthorizationException>(() => client.GetSampleDetailAsync(123));

        Assert.Equal(statusCode, ex.StatusCode);
        Assert.Equal("""not authorized""", ex.ResponseBody);
    }

    private sealed class CapturingHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private const string SampleDetailJson = """
        {
          "sampleId": 123,
          "receiptId": 456,
          "displayReceiptId": "R-456",
          "originalReceiptId": "456",
          "growerName": "Grower",
          "lotCode": "Lot",
          "varietyName": "Gala",
          "varietyCode": "GALA",
          "warehouseCode": "WP",
          "roomCode": "WP-4",
          "status": "InProgress",
          "starchStatus": "Pending",
          "emailStatus": "NotSent",
          "sampleTakenAt": "2026-05-31T12:00:00Z",
          "fruitReadings": []
        }
        """;
}
