using System.Net.Http.Json;
using CropQc.Shared.Security;

namespace CropQc.QcStation.Api;

public sealed class QcStationApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<QcStationSampleListItem>> GetTodaySamplesAsync(string? warehouseCode, CancellationToken cancellationToken = default)
    {
        var path = "api/qc-station/samples/today";
        if (!string.IsNullOrWhiteSpace(warehouseCode))
        {
            path += $"?warehouseCode={Uri.EscapeDataString(warehouseCode)}";
        }

        return await httpClient.GetFromJsonAsync<IReadOnlyList<QcStationSampleListItem>>(path, cancellationToken)
            ?? [];
    }

    public async Task<QcStationSampleDetail?> GetSampleDetailAsync(long sampleId, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<QcStationSampleDetail>($"api/qc-station/samples/{sampleId}/pressure", cancellationToken);

    public async Task<QcStationSampleDetail?> SavePressuresAsync(long sampleId, IReadOnlyList<QcStationPressureRowUpdate> rows, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/qc-station/samples/{sampleId}/pressure",
            new QcStationPressureUpdateRequest(rows),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<QcStationSampleDetail>(cancellationToken);
    }

    public static QcStationApiClient Create(string apiBaseUrl, string? stationCode = null, string? apiKey = null, string? stationName = null)
    {
        var baseUri = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? new Uri("https://localhost:7001")
            : new Uri(apiBaseUrl.Trim().TrimEnd('/') + "/");
        var client = new HttpClient { BaseAddress = baseUri };
        if (!string.IsNullOrWhiteSpace(stationCode))
        {
            client.DefaultRequestHeaders.Add(QcStationApiKeyValidator.StationCodeHeaderName, stationCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add(QcStationApiKeyValidator.HeaderName, apiKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stationName))
        {
            client.DefaultRequestHeaders.Add("X-QC-STATION-NAME", stationName.Trim());
        }

        return new QcStationApiClient(client);
    }
}
