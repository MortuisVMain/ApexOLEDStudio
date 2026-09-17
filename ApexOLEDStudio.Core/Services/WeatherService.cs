using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Fetches current local weather and temperature using free Open-Meteo API.
/// Caches responses for 30 minutes to minimize network activity.
/// </summary>
public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private CancellationTokenSource? _cts;
    private string _city = "auto";
    private TimeSpan _updateInterval = TimeSpan.FromMinutes(30);

    public string CurrentTemperature { get; private set; } = string.Empty; // e.g. "+18°C"
    public string CurrentCondition   { get; private set; } = string.Empty; // e.g. "Clear", "Rain"

    public void Start(string city = "auto", int updateIntervalMinutes = 30)
    {
        _city = string.IsNullOrWhiteSpace(city) ? "auto" : city.Trim();
        _updateInterval = TimeSpan.FromMinutes(Math.Clamp(updateIntervalMinutes, 5, 1440));
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await UpdateWeatherAsync(token);
                    await Task.Delay(_updateInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WeatherService] Error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromMinutes(5), token);
                }
            }
        }, token);
    }

    private async Task UpdateWeatherAsync(CancellationToken token)
    {
        // Use wttr.in plain format: "%t|%C" -> e.g. "+19°C|Clear"
        string location = Uri.EscapeDataString(_city);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://wttr.in/{location}?format=%t|%C");
        request.Headers.Add("User-Agent", "curl/8.0"); // wttr.in returns clean plain text for curl

        using var response = await _http.SendAsync(request, token);
        if (response.IsSuccessStatusCode)
        {
            string content = await response.Content.ReadAsStringAsync(token);
            string[] parts = content.Trim().Split('|');
            if (parts.Length >= 1)
            {
                CurrentTemperature = parts[0].Trim();
            }
            if (parts.Length >= 2)
            {
                CurrentCondition = parts[1].Trim();
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
