using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

internal sealed record ExternalEditProgress(string Message);

/// <summary>Recent editing activity, merged into the same Logs view as transfers and sync runs.</summary>
internal static class ExternalEditActivity
{
    private static readonly Lock Gate = new();
    private static readonly Queue<ActivityEntry> Entries = new();

    internal static void Record(string fileName, string state, string details = "")
    {
        static string Clean(string value, int maximum) => new(value.Take(maximum)
            .Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        var item = Clean(EditSessionDirectory.LocalName(fileName), 256);
        state = Clean(state, 128);
        details = Clean(details, 2048);
        lock (Gate)
        {
            Entries.Enqueue(new("editor:" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                Ui.Dialogs.ExternalEditorCaption, item, state, details));
            while (Entries.Count > ActivityLog.MaximumEntries) Entries.Dequeue();
        }
        Framework.DesktopErrorLog.Note("external-editor", $"{item}: {state}. {details}");
    }

    internal static IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (Gate) return [.. Entries];
    }
}
