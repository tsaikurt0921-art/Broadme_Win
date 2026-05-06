using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
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
        if (_httpClient.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }
    }

    public async Task<ValidateSerialApiResponse> ValidateSerialAsync(string serialNumber, string deviceId, CancellationToken ct = default)
    {
        var request = new ValidateSerialApiRequest
        {
            key = serialNumber,
            client_uid = deviceId
        };

        var baseUrls = BuildBaseUrls();
        Exception? lastException = null;
        ValidateSerialApiResponse? lastResponse = null;

        foreach (var baseUrl in baseUrls)
        {
            try
            {
                var endpoint = new Uri(new Uri(baseUrl.TrimEnd('/')), "/beta/keys/validate/");
                using var response = await _httpClient.PostAsJsonAsync(endpoint, request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    lastResponse = new ValidateSerialApiResponse
                    {
                        validation = false,
                        message = response.StatusCode switch
                        {
                            System.Net.HttpStatusCode.BadRequest => "請求格式錯誤",
                            System.Net.HttpStatusCode.Unauthorized => "未授權存取",
                            System.Net.HttpStatusCode.NotFound => "序號不存在或已被使用",
                            _ => $"伺服器錯誤 ({(int)response.StatusCode})"
                        }
                    };
                    continue;
                }

                var result = await response.Content.ReadFromJsonAsync<ValidateSerialApiResponse>(cancellationToken: ct);
                return result ?? new ValidateSerialApiResponse { validation = false, message = "伺服器回應格式錯誤" };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastException = ex;
            }
        }

        if (lastResponse is not null) return lastResponse;
        throw lastException ?? new HttpRequestException("驗證服務無法連線");
    }

    private List<string> BuildBaseUrls()
    {
        var urls = new List<string> { _settings.BaseUrl };
        if (_settings.FallbackBaseUrls is not null) urls.AddRange(_settings.FallbackBaseUrls);
        return urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
