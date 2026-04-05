using Microsoft.Extensions.Logging;

namespace OpenClawVoiceClient;

/// <summary>
/// Placeholder TTS client. Logs the text to synthesize and returns null.
/// Replace this with a real implementation when a TTS service is available.
/// </summary>
public sealed class PlaceholderTtsClient(ILogger<PlaceholderTtsClient> logger) : ITtsClient
{
    private readonly ILogger<PlaceholderTtsClient> _logger = logger;

    public Task<string?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        _logger.LogInformation("[TTS placeholder] Would synthesize: {Text}", text);
        return Task.FromResult<string?>(null);
    }
}
