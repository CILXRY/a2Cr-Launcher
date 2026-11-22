using System.Diagnostics;
using System.Text;
using a2Cr.Core.Errors;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Launch;

public sealed record LaunchCallbacks(
    Action<Process>? OnProcess = null,
    Action<string>? OnLog = null);

public sealed record LaunchResult(int ExitCode);

public sealed class GameProcessLauncher
{
    public async Task<LaunchResult> RunAsync(
        LaunchPlan plan,
        string gameDirectory,
        LaunchCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(gameDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.JavaExecutable,
            WorkingDirectory = IoPath.GetFullPath(gameDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false, false),
            StandardErrorEncoding = new UTF8Encoding(false, false)
        };
        foreach (var argument in plan.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new LauncherException("launch_failed", "无法启动 Java 进程。");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new LauncherException("launch_failed", "无法启动 Java 进程。", exception.Message, exception);
        }

        try
        {
            callbacks?.OnProcess?.Invoke(process);
        }
        catch
        {
            // UI 回调故障不能改变已经创建的游戏进程状态。
        }

        var outputTask = ForwardLinesAsync(process.StandardOutput, callbacks?.OnLog, cancellationToken);
        var errorTask = ForwardLinesAsync(process.StandardError, callbacks?.OnLog, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw;
        }

        return new LaunchResult(process.ExitCode);
    }

    private static async Task ForwardLinesAsync(StreamReader reader, Action<string>? onLog,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            try { onLog?.Invoke(line); }
            catch { /* 日志消费者不能影响进程回收。 */ }
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
