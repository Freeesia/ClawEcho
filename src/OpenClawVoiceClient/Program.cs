using ConsoleAppFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenClawVoiceClient;

var app = ConsoleApp.Create();

app.ConfigureDefaultConfiguration(config =>
{
    var userConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "claw-echo");

    // ツール内蔵デフォルト設定
    config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
    // ユーザー設定（Linux: ~/.config/claw-echo/、Windows: %APPDATA%\claw-echo\）
    config.AddJsonFile(Path.Combine(userConfigDir, "appsettings.json"), optional: true);
    // ローカル秘密設定（Git 管理外）
    config.AddJsonFile(Path.Combine(userConfigDir, "appsettings.Local.json"), optional: true);
    config.AddEnvironmentVariables("OPENCLAW_");
});

app.ConfigureServices((config, services) =>
{
    // 設定を AppOptions にバインドする
    services.Configure<AppOptions>(config.GetSection("App"));

    // systemd ライフタイム統合（systemd サービスとして実行時のみ有効）
    services.AddSystemd();

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
app.Add<SystemdCommands>();

await app.RunAsync(args);
