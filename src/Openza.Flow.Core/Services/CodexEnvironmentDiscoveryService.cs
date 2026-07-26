using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public interface IAgentEnvironmentDiscovery
{
    Task<IReadOnlyList<AgentEnvironment>> ProbeAsync(CancellationToken cancellationToken = default);
}

public sealed class CodexEnvironmentDiscoveryService : IAgentEnvironmentDiscovery
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ColdWslProbeTimeout = TimeSpan.FromSeconds(25);
    private readonly ICommandRunner _commandRunner;
    private readonly Func<string?> _windowsCodexResolver;

    public CodexEnvironmentDiscoveryService(ICommandRunner commandRunner, Func<string?>? windowsCodexResolver = null)
    {
        _commandRunner = commandRunner;
        _windowsCodexResolver = windowsCodexResolver ?? (() => FindOnPath("codex"));
    }

    public async Task<IReadOnlyList<AgentEnvironment>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var environments = new List<AgentEnvironment> { await ProbeWindowsAsync(cancellationToken) };
        IReadOnlyList<string> distributions;
        try
        {
            var list = await RunWslCommandWithColdStartRetryAsync(
                ["--list", "--quiet"],
                cancellationToken);
            if (list.ExitCode != 0)
            {
                return environments;
            }

            distributions = AgentSessionUtilities.DecodeWslDistributionList(list.StandardOutput);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return environments;
        }

        var probes = distributions.Select(distribution => ProbeWslAsync(distribution, cancellationToken));
        environments.AddRange(await Task.WhenAll(probes));
        return environments;
    }

    private async Task<AgentEnvironment> ProbeWindowsAsync(CancellationToken cancellationToken)
    {
        var executable = ResolveWindowsCodexExecutable(_windowsCodexResolver());
        if (executable is null)
        {
            return new AgentEnvironment(
                "codex:windows",
                AgentEnvironmentKind.Windows,
                "Windows",
                null,
                null,
                null,
                AgentEnvironmentAvailability.Missing,
                "Codex was not found on the current user's PATH.");
        }

        return await ProbeVersionAsync(
            "codex:windows",
            AgentEnvironmentKind.Windows,
            "Windows",
            null,
            executable,
            executable,
            ["--version"],
            cancellationToken);
    }

    private async Task<AgentEnvironment> ProbeWslAsync(string distribution, CancellationToken cancellationToken)
    {
        var id = $"codex:wsl:{distribution.ToLowerInvariant()}";
        try
        {
            var locate = await RunWslCommandWithColdStartRetryAsync(
                ["-d", distribution, "--", "sh", "-lc", "command -v codex"],
                cancellationToken);
            var executable = locate.StandardOutputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (locate.ExitCode != 0 || string.IsNullOrWhiteSpace(executable))
            {
                return new AgentEnvironment(
                    id,
                    AgentEnvironmentKind.Wsl,
                    distribution,
                    distribution,
                    null,
                    null,
                    AgentEnvironmentAvailability.Missing,
                    "Codex is not installed in this distribution.");
            }

            return await ProbeVersionAsync(
                id,
                AgentEnvironmentKind.Wsl,
                distribution,
                distribution,
                executable,
                "wsl.exe",
                ["-d", distribution, "--", executable, "--version"],
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AgentEnvironment(id, AgentEnvironmentKind.Wsl, distribution, distribution, null, null, AgentEnvironmentAvailability.TimedOut, "The environment probe timed out.");
        }
        catch (Exception)
        {
            return new AgentEnvironment(id, AgentEnvironmentKind.Wsl, distribution, distribution, null, null, AgentEnvironmentAvailability.Failed, "The environment probe failed.");
        }
    }

    private async Task<CommandResult> RunWslCommandWithColdStartRetryAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _commandRunner.RunAsync(
                new CommandRequest("wsl.exe", arguments, ProbeTimeout),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A stopped WSL VM can take longer than the normal probe budget to start.
            // Retry once with a larger budget; subsequent refreshes should use the fast path.
            return await _commandRunner.RunAsync(
                new CommandRequest("wsl.exe", arguments, ColdWslProbeTimeout),
                cancellationToken);
        }
    }

    private async Task<AgentEnvironment> ProbeVersionAsync(
        string id,
        AgentEnvironmentKind kind,
        string displayName,
        string? distribution,
        string executable,
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = kind == AgentEnvironmentKind.Wsl
                ? await RunWslCommandWithColdStartRetryAsync(arguments, cancellationToken)
                : await _commandRunner.RunAsync(new CommandRequest(command, arguments, ProbeTimeout), cancellationToken);
            var version = ParseVersion(result.StandardOutputText);
            if (result.ExitCode != 0 || version is null)
            {
                return new AgentEnvironment(id, kind, displayName, distribution, executable, version, AgentEnvironmentAvailability.Incompatible, "Codex did not return a supported version.");
            }

            return new AgentEnvironment(id, kind, displayName, distribution, executable, version, AgentEnvironmentAvailability.Available);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AgentEnvironment(id, kind, displayName, distribution, executable, null, AgentEnvironmentAvailability.TimedOut, "The environment probe timed out.");
        }
        catch (Exception)
        {
            return new AgentEnvironment(id, kind, displayName, distribution, executable, null, AgentEnvironmentAvailability.Failed, "The environment probe failed.");
        }
    }

    internal static string? ParseVersion(string output)
    {
        var token = output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => Version.TryParse(value.TrimStart('v'), out _));
        return token?.TrimStart('v');
    }

    internal static string? ResolveWindowsCodexExecutable(string? discoveredPath)
    {
        if (string.IsNullOrWhiteSpace(discoveredPath))
        {
            return null;
        }

        var extension = Path.GetExtension(discoveredPath);
        if (!extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return discoveredPath;
        }

        var adjacentExecutable = Path.ChangeExtension(discoveredPath, ".exe");
        if (File.Exists(adjacentExecutable))
        {
            return adjacentExecutable;
        }

        var shimDirectory = Path.GetDirectoryName(discoveredPath);
        if (string.IsNullOrWhiteSpace(shimDirectory))
        {
            return null;
        }

        var packageRoots = new[]
        {
            Path.Combine(shimDirectory, "node_modules", "@openai"),
            Path.Combine(shimDirectory, "node_modules", "@openai", "codex", "node_modules", "@openai")
        };
        foreach (var packageRoot in packageRoots)
        {
            var executable = FindNativePackageExecutable(packageRoot);
            if (executable is not null)
            {
                return executable;
            }
        }

        return null;
    }

    private static string? FindNativePackageExecutable(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
        {
            return null;
        }

        try
        {
            var preferredPackage = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64
                ? "codex-win32-arm64"
                : "codex-win32-x64";
            var packageDirectories = Directory
                .EnumerateDirectories(packageRoot, "codex-win32-*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(directory =>
                    Path.GetFileName(directory).Equals(preferredPackage, StringComparison.OrdinalIgnoreCase));
            foreach (var packageDirectory in packageDirectories)
            {
                var executable = Directory
                    .EnumerateFiles(packageDirectory, "codex.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (executable is not null)
                {
                    return executable;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executableName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
