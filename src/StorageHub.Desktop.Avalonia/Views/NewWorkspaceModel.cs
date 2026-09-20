using System.ComponentModel;
using System.Windows.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The New Workspace chooser: six arrangements, and whether to stop being asked.
/// </summary>
/// <remarks>
/// <para>
/// A dialog rather than a row of chips along the toolbar, which is what this was first. The chips
/// were readable but they answered the wrong question: an arrangement is chosen once, when a
/// workspace is made, and the rest of the time that strip was six controls of permanent width
/// competing with the panes for the window. The 1.x shell asked once and got out of the way, and
/// that is the better shape.
/// </para>
/// <para>
/// Picking creates: there is no Create button, because a picture of an arrangement is the whole
/// question and a second click to confirm it would only be a second click.
/// </para>
/// </remarks>
internal sealed class NewWorkspaceModel : INotifyPropertyChanged
{
    private bool _remember;

    internal NewWorkspaceModel() =>
        ChooseCommand = new RelayCommand(preset =>
        {
            if (preset is not WorkspacePreset chosen) return;
            Chosen = chosen;
            Closed?.Invoke(this, EventArgs.Empty);
        });

    /// <summary>The six arrangements, each drawn from the layout it actually produces.</summary>
    public static IReadOnlyList<WorkspacePreset> Presets => WorkspacePreset.All;

    /// <summary>What was picked, or nothing if the dialog was dismissed.</summary>
    internal WorkspacePreset? Chosen { get; private set; }

    /// <summary>
    /// Whether this arrangement becomes the default and the dialog stops appearing.
    /// </summary>
    /// <remarks>
    /// Stored as <c>DefaultWorkspacePaneCount</c> and <c>DefaultWorkspaceLayout</c>: the pane count
    /// being set is what "stop asking" means, and clearing it in Settings brings the dialog back.
    /// One piece of state rather than two that could disagree.
    /// </remarks>
    public bool Remember
    {
        get => _remember;
        set
        {
            if (_remember == value) return;
            _remember = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Remember)));
        }
    }

    public ICommand ChooseCommand { get; }

    public static string Caption => Ui.Shell.NewWorkspaceCaption;

    public static string ChooserAccessibleName => Ui.Shell.NewWorkspaceChooserAccessibleName;

    public static string RememberLabel => Ui.Shell.RememberLayout;

    public static string RememberAccessibleName => Ui.Shell.RememberLayoutAccessibleName;

    public static string RememberAccessibleDescription => Ui.Shell.RememberLayoutAccessibleDescription;

    /// <summary>Raised when an arrangement has been picked and the window should close.</summary>
    internal event EventHandler? Closed;

    public event PropertyChangedEventHandler? PropertyChanged;
}
