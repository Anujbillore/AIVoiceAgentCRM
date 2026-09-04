using System.Net.Http.Headers;
using System.Text;

namespace AiVoicePortal.Api.Services;

public interface IExotelMediaService
{
    bool IsConfigured { get; }
    Task<byte[]?> DownloadRecordingAsync(string recordingUrl, CancellationToken cancellationToken = default);
}

public class ExotelMediaService : IExotelMediaService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ExotelMediaService> _logger;

    public ExotelMediaService(HttpClient http, IConfiguration config, ILogger<ExotelMediaService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Exotel:ApiKey"]) &&
        !string.IsNullOrWhiteSpace(_config["Exotel:ApiToken"]);

    public async Task<byte[]?> DownloadRecordingAsync(string recordingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recordingUrl))
        {
            return null;
        }

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, recordingUrl);
                if (IsConfigured)
                {
                    var raw = Encoding.ASCII.GetBytes($"{_config["Exotel:ApiKey"]}:{_config["Exotel:ApiToken"]}");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
                }

                using var response = await _http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (bytes.Length > 0)
                    {
                        return bytes;
                    }
                }
                else
                {
                    _logger.LogWarning("Exotel recording download failed ({Attempt}): {Status} {Url}", attempt, response.StatusCode, recordingUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exotel recording download error ({Attempt})", attempt);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(700 * attempt), cancellationToken);
        }

        return null;
    }
}
