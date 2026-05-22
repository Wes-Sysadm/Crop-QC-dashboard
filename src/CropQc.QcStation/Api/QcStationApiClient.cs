using System.Net.Http.Json;

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
        await httpClient.GetFromJsonAsync<QcStationSampleDetail>($"api/qc-station/samples/{sampleId}", cancellationToken);

    public async Task<QcStationSampleDetail?> SavePressuresAsync(long sampleId, IReadOnlyList<QcStationPressureRowUpdate> rows, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/qc-station/samples/{sampleId}/pressures",
            new QcStationPressureUpdateRequest(rows),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<QcStationSampleDetail>(cancellationToken);
    }

    public static QcStationApiClient Create(string apiBaseUrl)
    {
        var baseUri = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? new Uri("https://localhost:7001")
            : new Uri(apiBaseUrl.Trim().TrimEnd('/') + "/");
        return new QcStationApiClient(new HttpClient { BaseAddress = baseUri });
    }
}
