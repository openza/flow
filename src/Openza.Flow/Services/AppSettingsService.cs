using Windows.ApplicationModel;
using Windows.Storage;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;

namespace Openza.Flow.Services;

public sealed class AppSettingsService : IAgentEnvironmentEnablement
{
    private const string RunInBackgroundKey = "run_in_background";
    private const string NotificationsEnabledKey = "notifications_enabled";
    private const string SelectedOrganizationKey = "selected_organization";
    private const string ThemeKey = "theme";
    private const string DisabledAgentEnvironmentsKey = "disabled_agent_environments";
    private const string TerminalLaunchModeKey = "terminal_launch_mode";
    private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

    public bool RunInBackground
    {
        get => ReadBool(RunInBackgroundKey);
        set => _settings.Values[RunInBackgroundKey] = value;
    }

    public bool NotificationsEnabled
    {
        get => !(_settings.Values.TryGetValue(NotificationsEnabledKey, out var value) && value is bool boolValue && !boolValue);
        set => _settings.Values[NotificationsEnabledKey] = value;
    }

    public string Theme
    {
        get => _settings.Values.TryGetValue(ThemeKey, out var value) ? value as string ?? "system" : "system";
        set => _settings.Values[ThemeKey] = value;
    }

    public string? SelectedOrganization
    {
        get => _settings.Values.TryGetValue(SelectedOrganizationKey, out var value) ? value as string : null;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _settings.Values.Remove(SelectedOrganizationKey);
            }
            else
            {
                _settings.Values[SelectedOrganizationKey] = value;
            }
        }
    }

    public IReadOnlySet<string> DisabledAgentEnvironmentIds
    {
        get
        {
            var serialized = _settings.Values.TryGetValue(DisabledAgentEnvironmentsKey, out var value) ? value as string : null;
            return string.IsNullOrWhiteSpace(serialized)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : serialized.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        set => _settings.Values[DisabledAgentEnvironmentsKey] = string.Join('|', value.Order(StringComparer.OrdinalIgnoreCase));
    }

    public TerminalLaunchMode TerminalLaunchMode
    {
        get => _settings.Values.TryGetValue(TerminalLaunchModeKey, out var value)
            && value is string text
            && Enum.TryParse<TerminalLaunchMode>(text, ignoreCase: true, out var mode)
                ? mode
                : TerminalLaunchMode.NewTab;
        set => _settings.Values[TerminalLaunchModeKey] = value.ToString();
    }

    public bool IsAgentEnvironmentEnabled(string environmentId) =>
        !DisabledAgentEnvironmentIds.Contains(environmentId);

    public void SetAgentEnvironmentEnabled(string environmentId, bool enabled)
    {
        var disabled = DisabledAgentEnvironmentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (enabled)
        {
            disabled.Remove(environmentId);
        }
        else
        {
            disabled.Add(environmentId);
        }

        DisabledAgentEnvironmentIds = disabled;
    }

    public async Task<bool> GetStartWithWindowsAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync("OpenzaFlowStartupTask");
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetStartWithWindowsAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync("OpenzaFlowStartupTask");
            if (enabled)
            {
                var state = await task.RequestEnableAsync();
                return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }

            task.Disable();
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool ReadBool(string key)
    {
        return _settings.Values.TryGetValue(key, out var value) && value is bool boolValue && boolValue;
    }
}
