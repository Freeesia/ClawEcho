namespace OpenClawVoiceClient;

/// <summary>
/// TTS implementation that writes the response text to standard output.
/// Used for debugging: no audio synthesis is performed.
/// </summary>
public sealed class ConsoleTtsClient : ITtsClient
{
    public Task<string?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        Console.WriteLine(text);
        return Task.FromResult<string?>(null);
    }
}
