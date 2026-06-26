using System.Diagnostics;

namespace KetoanMini.Api.Services;

public sealed class CameraSnapshotBridgeService(
    IConfiguration config,
    IHostEnvironment env,
    ILogger<CameraSnapshotBridgeService> logger) : IHostedService
{
    private bool _started;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ShouldAutoStart())
            return;

        var workspaceRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));
        var script = Path.Combine(workspaceRoot, "tools", "camera-snapshot", "Start-CameraSnapshot.ps1");
        if (!File.Exists(script))
        {
            logger.LogWarning("Camera snapshot bridge script was not found: {Path}", script);
            return;
        }

        logger.LogInformation("Starting camera snapshot bridge.");
        var result = await RunPowerShellScript(
            script,
            $"-WorkspaceRoot \"{workspaceRoot}\"",
            workspaceRoot,
            TimeSpan.FromSeconds(25),
            cancellationToken);

        if (result.ExitCode == 0)
        {
            _started = true;
            logger.LogInformation("Camera snapshot bridge started. {Output}", result.Output.Trim());
            return;
        }

        logger.LogWarning("Camera snapshot bridge did not start. ExitCode={ExitCode}; Error={Error}; Output={Output}",
            result.ExitCode, result.Error.Trim(), result.Output.Trim());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopBridgeAsync(cancellationToken, onlyWhenStarted: true);
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        if (!ShouldAutoStart())
            return;

        await StopBridgeAsync(cancellationToken, onlyWhenStarted: false);
        await StartAsync(cancellationToken);
    }

    private async Task StopBridgeAsync(CancellationToken cancellationToken, bool onlyWhenStarted)
    {
        if (onlyWhenStarted && !_started)
            return;

        var workspaceRoot = GetWorkspaceRoot();
        var script = Path.Combine(workspaceRoot, "tools", "camera-snapshot", "Stop-CameraSnapshot.ps1");
        if (!File.Exists(script))
            return;

        logger.LogInformation("Stopping camera snapshot bridge.");
        await RunPowerShellScript(script, "-Quiet", workspaceRoot, TimeSpan.FromSeconds(10), cancellationToken);
        _started = false;
    }

    private bool ShouldAutoStart()
    {
        if (!config.GetValue("KioskCamera:Enabled", false))
            return false;

        if (!config.GetValue("KioskCamera:AutoStartSnapshotBridge", false))
            return false;

        var source = (config["KioskCamera:FrameSource"] ?? "Rtsp").Trim();
        return source.Equals("ImageFile", StringComparison.OrdinalIgnoreCase) ||
               source.Equals("Snapshot", StringComparison.OrdinalIgnoreCase);
    }

    private string GetWorkspaceRoot()
        => Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));

    private static async Task<ScriptResult> RunPowerShellScript(
        string script,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Cannot start powershell.exe.");

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ScriptResult(-1, "", "Timed out while running camera snapshot script.");
        }

        return new ScriptResult(process.ExitCode, "", "");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort only; the next start runs Stop-CameraSnapshot.ps1 first.
        }
    }

    private sealed record ScriptResult(int ExitCode, string Output, string Error);
}
