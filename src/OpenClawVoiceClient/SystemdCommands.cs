using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace OpenClawVoiceClient;

/// <summary>
/// systemd サービス管理コマンド（Linux 専用）。
/// </summary>
public sealed class SystemdCommands(ILogger<SystemdCommands> logger)
{
    /// <summary>
    /// systemd システムサービスとして登録する（Linux 専用）。sudo で実行してください。
    /// </summary>
    /// <param name="enable">登録後に自動起動を有効化するか（デフォルト: true）。</param>
    [Command("install")]
    public async Task InstallAsync(bool enable = true, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("install コマンドは Linux でのみ使用できます。");

        var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
        if (string.IsNullOrEmpty(sudoUser))
            throw new InvalidOperationException("sudo で実行してください: sudo clawecho install");

        var userHome = await GetUserHomeAsync(sudoUser, cancellationToken).ConfigureAwait(false);
        var configDir = Path.Combine(userHome, ".config", "claw-echo");
        var toolPath = Path.Combine(userHome, ".dotnet", "tools", "clawecho");
        const string serviceFilePath = "/etc/systemd/system/claw-echo.service";

        // コンフィグディレクトリを作成して所有者を元のユーザーに設定
        Directory.CreateDirectory(configDir);
        await RunCommandAsync("chown", [$"{sudoUser}:{sudoUser}", configDir]).ConfigureAwait(false);
        logger.LogInformation("設定ディレクトリ: {Path}", configDir);

        // appsettings.json をテンプレートとしてコピー（既存は上書きしない）
        var templateSrc = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var templateDst = Path.Combine(configDir, "appsettings.json");
        if (!File.Exists(templateDst) && File.Exists(templateSrc))
        {
            File.Copy(templateSrc, templateDst);
            await RunCommandAsync("chown", [$"{sudoUser}:{sudoUser}", templateDst]).ConfigureAwait(false);
            logger.LogInformation("設定テンプレートをコピーしました: {Path}", templateDst);
        }

        // サービスファイルを生成
        var serviceContent = $"""
            [Unit]
            Description=ClawEcho - OpenClaw Voice Client
            After=network.target sound.target
            Wants=network.target

            [Service]
            Type=notify
            User={sudoUser}
            WorkingDirectory={configDir}
            ExecStart={toolPath} daemon
            Restart=on-failure
            RestartSec=5
            TimeoutStartSec=30
            TimeoutStopSec=30

            # 環境変数による設定上書き（appsettings.json の代替）
            # Environment=OPENCLAW_App__OpenClawBaseUrl=http://localhost:8080
            # Environment=OPENCLAW_App__OpenClawBearerToken=your-token-here
            # Environment=OPENCLAW_App__WhisperModelPath=/path/to/ggml-base.bin
            # Environment=OPENCLAW_App__WakeWordModelPath=/path/to/model.onnx

            [Install]
            WantedBy=multi-user.target
            """;
        await File.WriteAllTextAsync(serviceFilePath, serviceContent, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("サービスファイルを作成しました: {Path}", serviceFilePath);

        await RunCommandAsync("systemctl", ["daemon-reload"]).ConfigureAwait(false);

        if (enable)
        {
            await RunCommandAsync("systemctl", ["enable", "claw-echo.service"]).ConfigureAwait(false);
            logger.LogInformation("サービスを自動起動に登録しました。");
        }

        Console.WriteLine($"""

            インストール完了！

            サービスを開始するには:
              systemctl start claw-echo.service

            設定ファイルを編集してから起動してください:
              {templateDst}

            """);
    }

    /// <summary>
    /// systemd システムサービスの登録を解除する（Linux 専用）。sudo で実行してください。
    /// </summary>
    [Command("uninstall")]
    public async Task UninstallAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("uninstall コマンドは Linux でのみ使用できます。");

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SUDO_USER")))
            throw new InvalidOperationException("sudo で実行してください: sudo clawecho uninstall");

        const string serviceFilePath = "/etc/systemd/system/claw-echo.service";

        if (File.Exists(serviceFilePath))
        {
            try
            {
                await RunCommandAsync("systemctl", ["stop", "claw-echo.service"]).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "サービスの停止中にエラーが発生しました（続行します）");
            }

            try
            {
                await RunCommandAsync("systemctl", ["disable", "claw-echo.service"]).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "サービスの無効化中にエラーが発生しました（続行します）");
            }

            File.Delete(serviceFilePath);
            logger.LogInformation("サービスファイルを削除しました: {Path}", serviceFilePath);

            await RunCommandAsync("systemctl", ["daemon-reload"]).ConfigureAwait(false);

            Console.WriteLine("アンインストール完了。");
        }
        else
        {
            logger.LogWarning("サービスファイルが見つかりません: {Path}", serviceFilePath);
        }
    }

    private async Task RunCommandAsync(string command, IEnumerable<string> args)
    {
        var argList = args.ToList();
        var psi = new ProcessStartInfo(command) { UseShellExecute = false };
        foreach (var arg in argList) psi.ArgumentList.Add(arg);

        logger.LogDebug("実行: {Command} {Args}", command, string.Join(" ", argList));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"{command} を起動できませんでした。");
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{command} {string.Join(" ", argList)} が終了コード {process.ExitCode} で失敗しました。");
        }
    }

    private static async Task<string> GetUserHomeAsync(string userName, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("getent")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("passwd");
        psi.ArgumentList.Add(userName);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("getent を起動できませんでした。");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        // getent passwd 出力形式: username:password:uid:gid:gecos:home:shell
        var parts = output.Trim().Split(':');
        if (parts.Length < 6)
            throw new InvalidOperationException($"ユーザー '{userName}' のホームディレクトリを取得できませんでした。");

        return parts[5];
    }
}
