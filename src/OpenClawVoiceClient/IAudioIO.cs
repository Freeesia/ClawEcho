namespace OpenClawVoiceClient;

public interface IAudioIO
{
    Task<string> RecordUntilSilenceAsync(CancellationToken ct = default);
    Task PlayAsync(string wavFile, CancellationToken ct = default);
}
