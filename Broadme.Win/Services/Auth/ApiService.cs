using System.Net.Http.Json;
using Broadme.Win.Models;

namespace Broadme.Win.Services.Auth;

public sealed class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly ApiSettings _settings;

    public ApiService(HttpClient httpClient, ApiSettings? settings = null)
    {
        _httpClient = httpClient;
        _settings = settings ?? new ApiSettings();
    }

    public async Task<ValidateSerialApiResponse> ValidateSerialAsync(string serialNumber, string deviceId, CancellationToken ct = default)
    {
        var request = new ValidateSerialApiRequest
        {
            key = serialNumber,
            client_uid = deviceId
        };

        var endpoint = new Uri(new Uri(_settings.BaseUrl.TrimEnd('/')), "/beta/keys/validate/");
        using var response = await _httpClient.PostAsJsonAsync(endpoint, request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "請求格式錯誤",
                System.Net.HttpStatusCode.Unauthorized => "未授權存取",
                System.Net.HttpStatusCode.NotFound => "序號不存在或已被使用",
                _ => $"伺服器錯誤 ({(int)response.StatusCode})"
            };

            return new ValidateSerialApiResponse { validation = false, message = message };
        }

        var result = await response.Content.ReadFromJsonAsync<ValidateSerialApiResponse>(cancellationToken: ct);
        return result ?? new ValidateSerialApiResponse { validation = false, message = "伺服器回應格式錯誤" };
    }
}
