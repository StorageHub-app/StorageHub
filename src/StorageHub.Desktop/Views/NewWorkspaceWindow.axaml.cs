using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Asks which arrangement a new workspace should have.
/// </summary>
/// <remarks>
/// Answers with a preset, or with nothing when it was dismissed -- the same contract
/// <see cref="DialogWindow"/> uses, so a caller that treats "nothing" as "do not make one" handles
/// the title bar for free.
/// </remarks>
public partial class NewWorkspaceWindow : Window
{
    public NewWorkspaceWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is NewWorkspaceModel model) model.Closed += (_, _) => Close();
        };
    }

    /// <summary>
    /// The arrangement for a new workspace, asking only when there is no saved answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A saved pane count is what "stop asking" means, so this returns it without a dialog when it
    /// is set. Clearing it in Settings, under Workspace, brings the dialog back -- which is the
    /// sentence the checkbox's description promises, and the reason the preference is one value
    /// rather than a separate "ask me" flag that could disagree with it.
    /// </para>
    /// <para>
    /// A saved combination that no longer names a preset falls through to asking rather than being
    /// repaired silently, because the six are the only arrangements a workspace can hold.
    /// </para>
    /// </remarks>
    internal static async Task<WorkspacePreset?> ChooseAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
        var saved = Load(store);
        if (saved?.DefaultWorkspacePaneCount is { } paneCount &&
            WorkspacePreset.Find(paneCount, saved.DefaultWorkspaceLayout) is { } remembered)
        {
            return remembered;
        }

        var model = new NewWorkspaceModel();
        var window = new NewWorkspaceWindow { DataContext = model };
        await window.ShowDialog(owner).ConfigureAwait(true);

        if (model is { Chosen: { } chosen, Remember: true } && saved is not null)
        {
            Save(store, saved with
            {
                DefaultWorkspacePaneCount = chosen.PaneCount,
                DefaultWorkspaceLayout = chosen.Layout
            });
        }

        return model.Chosen;
    }

    /// <summary>
    /// Reads the settings file, or reports that it could not be read.
    /// </summary>
    /// <remarks>
    /// A chooser that cannot reach the settings file still has to be able to make a workspace. It
    /// asks every time instead of remembering, which is the failure this dialog can survive.
    /// </remarks>
    private static DesktopUpdatePreferences? Load(DesktopConfigStore store)
    {
        try
        {
            store.Preflight();
            return store.Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Save(DesktopConfigStore store, DesktopUpdatePreferences preferences)
    {
        try
        {
            store.Save(preferences);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The workspace is still made; only the answer to "stop asking" is lost.
        }
    }
}
