using ConsoleAppFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenClawVoiceClient;

var app = ConsoleApp.Create();

app.ConfigureDefaultConfiguration(config =>
{
    // Load appsettings.json and environment variable overrides
    config.AddJsonFile("appsettings.json", optional: true);
    config.AddEnvironmentVariables("OPENCLAW_");
});

app.ConfigureServices((IConfiguration config, IServiceCollection services) =>
{
    // Bind configuration to AppOptions
    services.Configure<AppOptions>(config.GetSection("App"));

    // Systemd lifetime integration (context-aware: only activates when running as a systemd service)
    services.AddSystemd();

    // HTTP client for OpenClaw
    services.AddHttpClient<OpenClawClient>();

    // Application services
    services.AddSingleton<AudioIO>();
    services.AddSingleton<WakeWordDetector>();
    services.AddSingleton<WhisperStt>();
    services.AddSingleton<OpenClawClient>();
    services.AddSingleton<ITtsClient, PlaceholderTtsClient>();
    services.AddSingleton<VoiceSession>();
    services.AddSingleton<DaemonWorker>();
});

app.ConfigureLogging((IConfiguration config, ILoggingBuilder logging) =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddConfiguration(config.GetSection("Logging"));
});

app.Add<Commands>();

await app.RunAsync(args);
