using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The fixed-size dialogs, measured in every shipped language.
/// </summary>
/// <remarks>
/// These dialogs size themselves in literal pixels — a 520x330 client area, a button pinned at
/// Left = 326 with Width = 82 — which is workable while every caption is the English one they were
/// measured against. German runs roughly a third longer and Danish compounds its nouns, so the
/// first translation of a dialog is also the first time its geometry is asked a question.
///
/// <see cref="SettingsPageFitTests"/> does this for the settings pages, which lay themselves out.
/// These do not, so nothing here can be inferred from that: a caption that no longer fits its
/// button is simply painted clipped, in a language the author does not read.
/// </remarks>
public sealed class DialogFitTests
{
    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryDialogControlFitsTheSpaceItIsGiven(string culture)
    {
        ShippedTranslationProvider.InCulture(culture, () =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            foreach (var (name, dialog) in CreateDialogs())
            {
                using (dialog)
                {
                    dialog.Show();
                    System.Windows.Forms.Application.DoEvents();
                    AssertFits(culture, name, dialog);
                }
            }
        }));
    }

    private static IEnumerable<(string Name, Form Dialog)> CreateDialogs()
    {
        yield return (
            nameof(DeleteItemsConfirmationForm),
            new DeleteItemsConfirmationForm(
                [.. Enumerable.Range(0, 8).Select(index => PaneTransferItem.Create(
                    $"report-{index}.txt",
                    $"reports/report-{index}.txt",
                    StorageItemKind.File,
                    1024).Value)],
                local: true));

        yield return (
            "PaneItemNameDialog",
            new PaneItemNameDialog(
                Ui.Shell.NewFolderTitle, Ui.Shell.FolderName, "New folder", Ui.Shell.Create));

        yield return (
            "PaneItemRenameDialog",
            new PaneItemNameDialog(
                Ui.Shell.RenameItem, Ui.Shell.FileName, "report.txt", Ui.Shell.RenameWorkspaceAccept));

        yield return (
            nameof(BatchRenameDialog),
            new BatchRenameDialog(["one.txt", "two.txt"], []));

        yield return (nameof(NewWorkspaceForm), new NewWorkspaceForm(WorkspaceLayout.SideBySide));
    }

    /// <summary>
    /// Walks the whole control tree, because these dialogs mix absolute placement with docked
    /// panels: a clipped caption can be three levels down from the form.
    /// </summary>
    private static void AssertFits(string culture, string dialogName, Control root)
    {
        var measured = 0;
        foreach (var control in Descendants(root))
        {
            if (control.Text.Length == 0 || !control.Visible)
            {
                continue;
            }

            // What the text actually needs, measured the way the control will paint it. A
            // single-line control is the case worth catching: a Label that wraps grows taller and
            // stays legible, but a Button clips its caption and says nothing.
            var wraps = control is Label { AutoSize: false } or Label { MaximumSize.Width: > 0 };
            if (wraps)
            {
                continue;
            }

            measured++;
            var needed = TextRenderer.MeasureText(control.Text, control.Font).Width;
            var available = control.Width - control.Padding.Horizontal;
            Assert.True(
                needed <= available,
                $"{culture} / {dialogName}: '{control.Text}' needs {needed}px but " +
                $"{control.GetType().Name} gives it {available}px.");
        }

        // Without this the whole check passes by measuring nothing, which is how a fit test quietly
        // stops being one after a dialog is rebuilt out of different controls.
        Assert.True(measured > 0, $"{culture} / {dialogName}: no captions were measured.");

        // A child pushed past the bottom edge is invisible rather than clipped, which is worse:
        // the checkbox that suppresses a warning is the control most likely to sit there.
        foreach (Control child in root.Controls)
        {
            Assert.True(
                child.Bottom <= root.ClientSize.Height,
                $"{culture} / {dialogName}: '{child.Name}' ends at {child.Bottom}px, below the " +
                $"{root.ClientSize.Height}px client area.");
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}
