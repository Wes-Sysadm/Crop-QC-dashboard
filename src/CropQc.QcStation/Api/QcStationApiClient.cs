using System.Net;
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

        using var response = await httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<QcStationSampleListItem>>(cancellationToken)
            ?? [];
    }

    public async Task<QcStationSampleDetail?> GetSampleDetailAsync(long sampleId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/qc-station/samples/{sampleId}/pressure", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<QcStationSampleDetail>(cancellationToken);
    }

    public async Task<QcStationSampleDetail?> SavePressuresAsync(long sampleId, IReadOnlyList<QcStationPressureRowUpdate> rows, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/qc-station/samples/{sampleId}/pressure",
            new QcStationPressureUpdateRequest(rows),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<QcStationSampleDetail>(cancellationToken);
    }

    public static QcStationApiClient Create(string apiBaseUrl, string? stationCode = null, string? apiKey = null, string? stationName = null)
    {
        return Create(new HttpClientHandler(), apiBaseUrl, stationCode, apiKey, stationName);
    }

    public static QcStationApiClient Create(HttpMessageHandler handler, string apiBaseUrl, string? stationCode = null, string? apiKey = null, string? stationName = null)
    {
        var baseUri = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? new Uri("https://localhost:7001")
            : new Uri(apiBaseUrl.Trim().TrimEnd('/') + "/");
        var client = new HttpClient(handler) { BaseAddress = baseUri };
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new QcStationAuthorizationException(response.StatusCode, body);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new QcStationApiException(response.StatusCode, body);
        }
    }
}

public class QcStationApiException(HttpStatusCode statusCode, string? responseBody = null)
    : HttpRequestException($"QC Station API request failed with HTTP {(int)statusCode} {statusCode}.", null, statusCode)
{
    public string? ResponseBody { get; } = responseBody;
}

public sealed class QcStationAuthorizationException(HttpStatusCode statusCode, string? responseBody = null)
    : QcStationApiException(statusCode, responseBody)
{
}
