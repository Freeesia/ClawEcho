namespace OpenClawVoiceClient;

public interface ITtsClient
{
    /// <summary>
    /// Synthesizes the given text into a WAV file and returns the path to that file.
    /// Returns null or empty string if synthesis is not available.
    /// </summary>
    Task<string?> SynthesizeAsync(string text, CancellationToken ct = default);
}
