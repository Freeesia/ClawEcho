using Whisper.net.Ggml;

namespace OpenClawVoiceClient;

public sealed class AppOptions
{
    public string OpenClawBaseUrl { get; set; } = "";
    public string OpenClawBearerToken { get; set; } = "";
    public string InputDevice { get; set; } = "default";
    public string OutputDevice { get; set; } = "default";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public int MaxRecordSeconds { get; set; } = 15;
    public int SilenceDurationMs { get; set; } = 1200;
    public double SilenceThreshold { get; set; } = 0.01;
    public string WhisperModelPath { get; set; } = "";
    public GgmlType WhisperModelType { get; set; } = GgmlType.Base;
    public string WhisperLanguage { get; set; } = "ja";
    public string WakeWordModelPath { get; set; } = "";
    public float WakeWordThreshold { get; set; } = 0.5f;
    public string? TtsEndpoint { get; set; }
    public string OpenClawModel { get; set; } = "openclaw:main";
    public string? SessionUser { get; set; } = "clawecho";
}
