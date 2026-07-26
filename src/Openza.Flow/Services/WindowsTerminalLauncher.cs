using System.Diagnostics;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;

namespace Openza.Flow.Services;

public sealed class WindowsTerminalLauncher(ICommandRunner commandRunner) : ITerminalLauncher
{
    public async Task<TerminalLaunchValidation> ValidateAsync(AgentSessionSummary session, CancellationToken cancellationToken = default)
    {
        if (!session.Environment.IsAvailable || string.IsNullOrWhiteSpace(session.Environment.ExecutablePath))
        {
            return new TerminalLaunchValidation(false, "environment_unavailable", "This agent environment is unavailable. Refresh environments and try again.");
        }

        if (FindOnPath("wt.exe") is null)
        {
            return new TerminalLaunchValidation(false, "terminal_missing", "Windows Terminal is unavailable. You can still copy the resume command.");
        }

        if (session.Environment.Kind == AgentEnvironmentKind.Windows)
        {
            return Directory.Exists(session.WorkingDirectory)
                ? new TerminalLaunchValidation(true)
                : new TerminalLaunchValidation(false, "directory_missing", "The original Windows working directory no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(session.Environment.DistributionName))
        {
            return new TerminalLaunchValidation(false, "distribution_unavailable", "The original WSL distribution is unavailable.");
        }

        try
        {
            var result = await commandRunner.RunAsync(
                new CommandRequest(
                    "wsl.exe",
                    ["-d", session.Environment.DistributionName, "--", "test", "-d", session.WorkingDirectory],
                    TimeSpan.FromSeconds(8)),
                cancellationToken);
            return result.ExitCode == 0
                ? new TerminalLaunchValidation(true)
                : new TerminalLaunchValidation(false, "directory_missing", "The original Linux working directory no longer exists.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TerminalLaunchValidation(false, "validation_timeout", "The WSL directory check timed out.");
        }
        catch
        {
            return new TerminalLaunchValidation(false, "distribution_unavailable", "The WSL distribution could not be reached.");
        }
    }

    public TerminalLaunchCommand BuildCommand(AgentSessionSummary session, TerminalLaunchMode mode) =>
        TerminalCommandBuilder.Build(session, mode);

    public async Task LaunchAsync(AgentSessionSummary session, TerminalLaunchMode mode, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(session, cancellationToken);
        if (!validation.IsValid)
        {
            throw new TerminalLaunchException(validation.ErrorCategory ?? "validation_failed", validation.Message ?? "The session cannot be resumed.");
        }

        var command = BuildCommand(session, mode);
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new TerminalLaunchException("terminal_start", "Windows Terminal could not be started.");
            }
        }
        catch (TerminalLaunchException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TerminalLaunchException("terminal_start", "Windows Terminal could not be started.", exception);
        }
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        return path?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, executableName))
            .FirstOrDefault(File.Exists);
    }
}

public sealed class TerminalLaunchException(string category, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Category { get; } = category;
}
