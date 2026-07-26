using System.Text;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class CodexEnvironmentDiscoveryTests
{
    [Fact]
    public async Task ProbeFindsWindowsAndMultipleWslEnvironmentsWithPartialFailure()
    {
        var runner = new FakeCommandRunner(request =>
        {
            if (request.FileName == "C:\\Tools\\codex.exe")
            {
                return Result("codex-cli 0.144.6\n");
            }

            if (request.Arguments.SequenceEqual(["--list", "--quiet"]))
            {
                return new CommandResult(0, Encoding.Unicode.GetBytes("Ubuntu\r\nDebian\r\n"), string.Empty);
            }

            if (request.Arguments.Contains("Ubuntu") && request.Arguments.Contains("command -v codex"))
            {
                return Result("/home/user/.local/bin/codex\n");
            }

            if (request.Arguments.Contains("Ubuntu") && request.Arguments.Contains("--version"))
            {
                return Result("codex-cli 0.144.6\n");
            }

            return new CommandResult(1, [], "not found");
        });
        var service = new CodexEnvironmentDiscoveryService(runner, () => "C:\\Tools\\codex.exe");

        var environments = await service.ProbeAsync();

        Assert.Equal(3, environments.Count);
        Assert.Equal(2, environments.Count(environment => environment.IsAvailable));
        Assert.Contains(environments, environment => environment.DisplayName == "Debian" && environment.Availability == AgentEnvironmentAvailability.Missing);
    }

    [Fact]
    public async Task ProbeReportsMissingWindowsCodexWithoutBlockingWslEnumeration()
    {
        var runner = new FakeCommandRunner(request => request.Arguments.SequenceEqual(["--list", "--quiet"])
            ? new CommandResult(0, Encoding.Unicode.GetBytes(string.Empty), string.Empty)
            : new CommandResult(1, [], string.Empty));
        var service = new CodexEnvironmentDiscoveryService(runner, () => null);

        var environment = Assert.Single(await service.ProbeAsync());

        Assert.Equal(AgentEnvironmentAvailability.Missing, environment.Availability);
    }

    [Fact]
    public async Task ProbeRetriesWslDistributionListWhenColdStartExceedsFastTimeout()
    {
        var listAttempts = 0;
        var runner = new FakeCommandRunner(request =>
        {
            if (!request.Arguments.SequenceEqual(["--list", "--quiet"]))
            {
                return new CommandResult(1, [], string.Empty);
            }

            listAttempts++;
            if (listAttempts == 1)
            {
                throw new OperationCanceledException();
            }

            Assert.Equal(TimeSpan.FromSeconds(25), request.Timeout);
            return new CommandResult(0, Encoding.Unicode.GetBytes("Ubuntu\r\n"), string.Empty);
        });
        var service = new CodexEnvironmentDiscoveryService(runner, () => null);

        var environments = await service.ProbeAsync();

        Assert.Equal(2, listAttempts);
        Assert.Contains(environments, environment => environment.DisplayName == "Ubuntu");
    }

    [Fact]
    public async Task ProbeRetriesCodexLookupWhenDistributionIsCold()
    {
        var lookupAttempts = 0;
        var runner = new FakeCommandRunner(request =>
        {
            if (request.Arguments.SequenceEqual(["--list", "--quiet"]))
            {
                return new CommandResult(0, Encoding.Unicode.GetBytes("Ubuntu\r\n"), string.Empty);
            }

            if (request.Arguments.Contains("command -v codex"))
            {
                lookupAttempts++;
                if (lookupAttempts == 1)
                {
                    throw new OperationCanceledException();
                }

                Assert.Equal(TimeSpan.FromSeconds(25), request.Timeout);
                return Result("/home/user/.local/bin/codex\n");
            }

            if (request.Arguments.Contains("--version"))
            {
                return Result("codex-cli 0.144.6\n");
            }

            return new CommandResult(1, [], string.Empty);
        });
        var service = new CodexEnvironmentDiscoveryService(runner, () => null);

        var environments = await service.ProbeAsync();

        Assert.Equal(2, lookupAttempts);
        Assert.Contains(environments, environment => environment.DisplayName == "Ubuntu" && environment.IsAvailable);
    }

    [Fact]
    public async Task ProbeRetriesCodexVersionWhenDistributionIsStillStarting()
    {
        var versionAttempts = 0;
        var runner = new FakeCommandRunner(request =>
        {
            if (request.Arguments.SequenceEqual(["--list", "--quiet"]))
            {
                return new CommandResult(0, Encoding.Unicode.GetBytes("Ubuntu\r\n"), string.Empty);
            }

            if (request.Arguments.Contains("command -v codex"))
            {
                return Result("/home/user/.local/bin/codex\n");
            }

            if (request.Arguments.Contains("--version"))
            {
                versionAttempts++;
                if (versionAttempts == 1)
                {
                    throw new OperationCanceledException();
                }

                Assert.Equal(TimeSpan.FromSeconds(25), request.Timeout);
                return Result("codex-cli 0.145.0\n");
            }

            return new CommandResult(1, [], string.Empty);
        });
        var service = new CodexEnvironmentDiscoveryService(runner, () => null);

        var environments = await service.ProbeAsync();

        Assert.Equal(2, versionAttempts);
        Assert.Contains(environments, environment => environment.DisplayName == "Ubuntu" && environment.IsAvailable);
    }

    [Fact]
    public void ResolveWindowsCodexExecutableUsesNativeNpmPackageBinary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Openza&Flow%PATH%^{Guid.NewGuid():N}");
        var shim = Path.Combine(root, "codex.cmd");
        var nativeExecutable = Path.Combine(
            root,
            "node_modules",
            "@openai",
            "codex",
            "node_modules",
            "@openai",
            "codex-win32-x64",
            "vendor",
            "x86_64-pc-windows-msvc",
            "codex",
            "codex.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(nativeExecutable)!);
            File.WriteAllText(shim, "@echo off\r\n");
            File.WriteAllBytes(nativeExecutable, []);

            var resolved = CodexEnvironmentDiscoveryService.ResolveWindowsCodexExecutable(shim);

            Assert.Equal(nativeExecutable, resolved);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveWindowsCodexExecutableRejectsUnresolvedBatchShim()
    {
        var shim = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "codex.cmd");

        Assert.Null(CodexEnvironmentDiscoveryService.ResolveWindowsCodexExecutable(shim));
    }

    private static CommandResult Result(string output) => new(0, Encoding.UTF8.GetBytes(output), string.Empty);

    private sealed class FakeCommandRunner(Func<CommandRequest, CommandResult> handler) : ICommandRunner
    {
        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(handler(request));
    }
}
