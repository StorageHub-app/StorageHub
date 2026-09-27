using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using StorageHub.Agent;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Settings;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Background agent page: whether the agent starts when you sign in, or only runs while
/// StorageHub is open.
/// </summary>
/// <remarks>
/// <para>
/// 1.4's page, less the Windows service it also offered, which 2.0 removed (AgentHostMode says
/// why). What is left is whether a sign-in registration exists, which is a fact about the machine
/// rather than a preference, so it is read from the machine when the page opens and applied by its
/// own button rather than by the dialog's Apply, as 1.4 did it.
/// </para>
/// <para>
/// Where this build cannot change it the button stays dim, which is 1.4's answer everywhere but
/// Windows; the page still says how the agent is run.
/// </para>
/// </remarks>
internal sealed class AgentPageModel : SettingsPageModel, INotifyPropertyChanged
{
    /// <summary>The choices, in the order <see cref="Modes"/> names them; the index binds the two.</summary>
    private static readonly AgentHostMode[] ModeChoices = [AgentHostMode.UserSession, AgentHostMode.AppSession];

    private readonly AgentModeServices? _services;
    private AgentHostMode _current;
    private int _selectedMode;
    private StatusLine _status = StatusLine.Muted(string.Empty);

    internal AgentPageModel(SettingsPageDefinition definition, AgentModeServices? services)
        : base(definition, [])
    {
        _services = services;
        _current = services?.Current() ?? AgentHostMode.UserSession;
        _selectedMode = Math.Max(0, Array.IndexOf(ModeChoices, _current));
        ApplyCommand = new RelayCommand(_ => ApplyMode(), _ => CanApply);
    }

    public static string Caption => Ui.Settings.AgentModeSection.ToUpper(CultureInfo.CurrentCulture);

    public static string ModeLabel => Ui.Settings.AgentModeLabel;

    public static IReadOnlyList<string> Modes { get; } = [Ui.Settings.AgentModeUserSession, Ui.Settings.AgentModeAppSession];

    public int SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value < 0 || value >= ModeChoices.Length || _selectedMode == value) return;
            _selectedMode = value;
            Raise(nameof(SelectedMode));
            Raise(nameof(ModeHint));
            Raise(nameof(CanApply));
            (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>What the chosen mode means, in its own row as the host-key modes explain themselves.</summary>
    public string ModeHint => ModeChoices[_selectedMode] == AgentHostMode.UserSession
        ? Ui.Settings.AgentModeUserSessionDetail
        : Ui.Settings.AgentModeAppSessionDetail;

    public static string ApplyLabel => Ui.Settings.AgentModeApply;

    public ICommand ApplyCommand { get; }

    /// <summary>Only a change, and only where this build can make one.</summary>
    public bool CanApply => _services?.Apply is not null && ModeChoices[_selectedMode] != _current;

    /// <summary>What the last change did.</summary>
    public StatusLine Status
    {
        get => _status;
        private set
        {
            _status = value;
            Raise(nameof(Status));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ApplyMode()
    {
        if (_services?.Apply is not { } apply) return;

        var result = apply(ModeChoices[_selectedMode]);

        // Re-read rather than assume: the registration can be refused by policy or by the
        // environment switch, and the page should say what the machine now does.
        _current = _services.Current();
        Status = new StatusLine(result.Message, result.Succeeded ? MetricTone.Success : MetricTone.Danger);
        Raise(nameof(CanApply));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
