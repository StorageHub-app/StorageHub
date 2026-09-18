using System.Reflection;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The reconcile action shows the action it will apply.
/// </summary>
/// <remarks>
/// <see cref="ComboDisplayTextTests"/> walks the combo boxes of the dialogs it can construct, and
/// this one is not among them: <see cref="TransferQueueControl"/> needs an agent client, so the
/// toolbar that carries the most consequential choice in the queue — what to do with a transfer
/// nobody can account for — was the one enum combo with no coverage.
/// </remarks>
public sealed class ReconcileActionComboTests
{
    [Fact]
    public void TheReconcileActionShowsItsSelectedActionAsWords()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var control = new TransferQueueControl(new InertQueueClient());
            using var form = new Form();
            form.Controls.Add(control);
            _ = form.Handle;
            _ = control.Handle;
            System.Windows.Forms.Application.DoEvents();

            var combo = Find(control);
            Assert.NotNull(combo);

            Assert.True(
                combo!.SelectedItem is TransferReconciliationAction,
                $"The reconcile action has no selected value (SelectedItem was " +
                $"'{combo.SelectedItem ?? "null"}'), so the toolbar shows an empty box.");

            var shown = combo.GetItemText(combo.SelectedItem);
            Assert.False(
                string.IsNullOrWhiteSpace(shown),
                "The reconcile action renders as an empty box rather than the action it will apply.");
            Assert.Equal(
                UiEnumNames.Describe((TransferReconciliationAction)combo.SelectedItem!),
                shown);
        });
    }

    private static StorageHubChoiceField? Find(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is ToolStrip strip)
            {
                foreach (var hosted in strip.Items.OfType<StorageHubToolStripChoice>())
                {
                    if (hosted.Name == "ReconciliationAction")
                    {
                        return hosted.Field;
                    }
                }
            }

            if (Find(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>Never contacted: the toolbar is built before any request is made.</summary>
    private sealed class InertQueueClient : ITransferQueueAgentClient
    {
        public Task<TransferEnqueueResponse> EnqueueAsync(
            TransferEnqueueRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferListResponse> ListAsync(
            TransferListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferStatusResponse> GetStatusAsync(
            TransferStatusRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferMutationResponse> CancelAsync(
            TransferCancelRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferMutationResponse> RetryAsync(
            TransferRetryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferMutationResponse> ReconcileAsync(
            TransferReconcileRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
