using System.Text;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public static class TerminalCommandBuilder
{
    public static TerminalLaunchCommand Build(AgentSessionSummary session, TerminalLaunchMode mode)
    {
        if (!session.Environment.IsAvailable || string.IsNullOrWhiteSpace(session.Environment.ExecutablePath))
        {
            throw new InvalidOperationException("The Codex environment is unavailable.");
        }

        var arguments = new List<string>();
        if (mode == TerminalLaunchMode.NewTab)
        {
            arguments.AddRange(["-w", "0", "new-tab"]);
        }
        else
        {
            arguments.AddRange(["-w", "-1", "new-tab"]);
        }

        if (session.Environment.Kind == AgentEnvironmentKind.Windows)
        {
            arguments.AddRange([
                "--startingDirectory",
                session.WorkingDirectory,
                session.Environment.ExecutablePath,
                "resume",
                session.Key.SessionId
            ]);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(session.Environment.DistributionName))
            {
                throw new InvalidOperationException("The WSL distribution is unavailable.");
            }

            arguments.AddRange([
                "wsl.exe",
                "-d",
                session.Environment.DistributionName,
                "--cd",
                session.WorkingDirectory,
                "--",
                session.Environment.ExecutablePath,
                "resume",
                session.Key.SessionId
            ]);
        }

        return new TerminalLaunchCommand("wt.exe", arguments, FormatForCopy("wt.exe", arguments));
    }

    private static string FormatForCopy(string fileName, IEnumerable<string> arguments)
    {
        var builder = new StringBuilder(Quote(fileName));
        foreach (var argument in arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-' or '/' or ':' or '\\'))
        {
            return value;
        }

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
