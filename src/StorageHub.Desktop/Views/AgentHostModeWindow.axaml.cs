using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Agent;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Services;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>The "How should StorageHub run?" question, and the answer given.</summary>
/// <remarks>
/// Starting at sign-in is chosen already, as in 1.4: it is how StorageHub is installed to run, and
/// the other way should be picked on purpose rather than by pressing Enter.
/// </remarks>
internal sealed class AgentHostModeModel : INotifyPropertyChanged
{
    private bool _appSession;

    internal AgentHostModeModel()
    {
        ContinueCommand = new RelayCommand(_ =>
        {
            Confirmed = true;
            Closed?.Invoke(this, EventArgs.Empty);
        });
    }

    public static string Title => Ui.Settings.AgentModeStartupQuestionTitle;

    public static string Intro => Ui.Settings.AgentModeStartupIntro;

    public static string UserSessionLabel => Ui.Settings.AgentModeUserSession;

    public static string UserSessionDetail => Ui.Settings.AgentModeUserSessionDetail;

    public static string AppSessionLabel => Ui.Settings.AgentModeAppSession;

    public static string AppSessionDetail => Ui.Settings.AgentModeAppSessionDetail;

    public static string ChangeLaterHint => Ui.Settings.AgentModeChangeLaterHint;

    public static string ContinueLabel => Ui.Settings.AgentModeContinue;

    public bool IsUserSession
    {
        get => !_appSession;
        set => SetAppSession(!value);
    }

    public bool IsAppSession
    {
        get => _appSession;
        set => SetAppSession(value);
    }

    public ICommand ContinueCommand { get; }

    /// <summary>Whether Continue was pressed, rather than the window closed.</summary>
    internal bool Confirmed { get; private set; }

    internal AgentHostMode SelectedMode => _appSession ? AgentHostMode.AppSession : AgentHostMode.UserSession;

    internal event EventHandler? Closed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetAppSession(bool value)
    {
        if (_appSession == value) return;
        _appSession = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUserSession)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAppSession)));
    }
}

/// <summary>
/// Asks once, on the first run of an installed Windows build, how StorageHub should run its agent,
/// and applies the answer.
/// </summary>
/// <remarks>
/// <para>
/// 1.4 asked this from the main window once it was shown, so the shell is already usable behind it,
/// and applied the answer with AgentHostModeController. Its third choice, the Windows service, went
/// in 2.0, and with it the elevation prompt; what is left is whether a sign-in entry exists.
/// </para>
/// <para>
/// Not asked on Linux. The agent's registration there is the user's systemd unit, which the .deb
/// installs and leaves each user to enable, and neither Settings nor this can change it yet.
/// </para>
/// </remarks>
public partial class AgentHostModeWindow : Window
{
    public AgentHostModeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AgentHostModeModel model) model.Closed += (_, _) => Close();
        };
    }

    /// <summary>Asks over <paramref name="owner"/>; null when the window was closed without an answer.</summary>
    internal static async Task<AgentHostMode?> AskAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var model = new AgentHostModeModel();
        await new AgentHostModeWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Confirmed ? model.SelectedMode : null;
    }

    /// <summary>
    /// Asks, if this is the first run that should, and applies a change of mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marked as asked before the question is shown, as 1.4 did. Only a change is applied:
    /// choosing the way the agent already runs has nothing to do.
    /// </para>
    /// <para>
    /// Closing the window is taken as the answer already chosen, and keeping sign-in is reported
    /// only when it could not be made so. In 1.4 both left alone the sign-in entry its installer
    /// had made. The MSI does not make one yet (P.1.5), so it is made here, quietly, for the same
    /// outcome; once the MSI does, the mode already matches and there is nothing to apply.
    /// </para>
    /// </remarks>
    internal static async Task OfferOnceAsync(Window owner)
    {
        try
        {
            var root = DesktopFrameworkPaths.Resolve().ApplicationRoot;
            if (!AgentHostModePrompt.ShouldAsk(root)) return;

            AgentHostModePrompt.MarkAsked(root);
            var chosen = await AskAsync(owner).ConfigureAwait(true) ?? AgentHostMode.UserSession;
            if (chosen == DesktopAgentHost.Mode) return;

            var result = AgentHostModeController.Apply(chosen);
            if (chosen == AgentHostMode.UserSession && result.Succeeded) return;

            await new AvaloniaDialogService(() => owner).ShowAsync(new DialogRequest
            {
                Title = Ui.Settings.AgentModeStartupQuestionTitle,
                Message = result.Message,
                Severity = result.Succeeded ? DialogSeverity.Information : DialogSeverity.Warning,
            }).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            // A failed offer must never stop the shell from opening.
            DesktopErrorLog.Write("agent-mode", error);
        }
    }
}
