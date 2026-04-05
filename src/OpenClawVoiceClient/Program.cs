using ConsoleAppFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenClawVoiceClient;

var app = ConsoleApp.Create();

app.ConfigureDefaultConfiguration(config =>
{
    // appsettings.json と環境変数のオーバーライドを読み込む
    config.AddJsonFile("appsettings.json", optional: true);
    config.AddEnvironmentVariables("OPENCLAW_");
});

app.ConfigureServices((config, services) =>
{
    // 設定を AppOptions にバインドする
    services.Configure<AppOptions>(config.GetSection("App"));

    // systemd ライフタイム統合（systemd サービスとして実行時のみ有効）
    services.AddSystemd();

    // OpenClaw 用 HTTPクライアント
    services.AddHttpClient<OpenClawClient>();

    // アプリケーションサービス
#if WINDOWS
    services.AddSingleton<IAudioIO, WindowsAudioIO>();
#else
    services.AddSingleton<IAudioIO, AlsaAudioIO>();
#endif
    services.AddSingleton<WakeWordDetector>();
    services.AddSingleton<WhisperStt>();
    services.AddSingleton<OpenClawClient>();
    services.AddSingleton<ITtsClient, ConsoleTtsClient>();
    services.AddSingleton<VoiceSession>();
    services.AddSingleton<DaemonWorker>();
});

app.ConfigureLogging((config, logging) =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddConfiguration(config.GetSection("Logging"));
});

app.Add<Commands>();

await app.RunAsync(args);
