using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Projects the agent's <see cref="ConnectionSummary"/> into the presentation model the connection
/// list paints.
///
/// Extracted from the Connection Manager dialog when the saved-connection list moved into the shell:
/// two surfaces now render the same connections, and a second copy of this projection would let them
/// disagree about a connection's name, provider or health wording.
/// </summary>
internal static class ConnectionCardFactory
{
    /// <summary>
    /// Shared so the panel's row action and the editor's toolbar warn about the same consequence in
    /// the same words.
    /// </summary>
    internal static string DeleteConfirmationCaption => Ui.Dialogs.DeleteConnectionCaption;

    internal static string DeleteConfirmationPrompt(string displayName) =>
        Ui.Format(Ui.Dialogs.DeleteConnectionPromptFormat, displayName);

    internal static ConnectionCardModel Create(ConnectionSummary connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var provider = MapProvider(connection.Provider);
        var providerName = ConnectionProviderCatalog.Get(provider).DisplayName;
        var summary = string.IsNullOrWhiteSpace(connection.FolderPath)
            ? Ui.Format(Ui.Pane.SavedProfileSummaryFormat, providerName)
            : $"{providerName} · {connection.FolderPath}";
        return new ConnectionCardModel(
            connection.DisplayName,
            provider,
            summary,
            connection.IsEnabled ? DescribeHealth(connection.Health) : Ui.Pane.Disabled,
            connection.IsFavorite,
            connection.ConnectionId,
            connection.IsEnabled,
            connection.AccentColor,
            connection.FolderPath,
            connection.Tags,
            connection.IconKey);
    }

    /// <summary>
    /// A connection's last health check, in words.
    /// </summary>
    /// <remarks>
    /// These were English literals after the port, although every one of them had a translated
    /// counterpart in the pane's strings -- which is where 1.x read them from.
    /// </remarks>
    internal static string DescribeHealth(ConnectionHealthSnapshot? health) => health switch
    {
        null => Ui.Pane.NotTested,
        { State: ConnectionHealthState.Healthy } => Ui.Format(Ui.Pane.HealthyFormat, health.ElapsedMilliseconds),
        { RequiresCredentialAction: true } => Ui.Pane.CredentialsNeedAttention,
        { RequiresTrustAction: true } => Ui.Pane.TrustDecisionRequired,
        { State: ConnectionHealthState.Unavailable } => Ui.Pane.Unavailable,
        _ => Ui.Pane.NeedsAttention
    };

    internal static StorageProviderKind MapProvider(StorageConnectionProvider provider) => provider switch
    {
        StorageConnectionProvider.Local => StorageProviderKind.Local,
        StorageConnectionProvider.S3 => StorageProviderKind.S3,
        StorageConnectionProvider.Ftp => StorageProviderKind.Ftp,
        StorageConnectionProvider.Ftps => StorageProviderKind.Ftps,
        StorageConnectionProvider.Sftp => StorageProviderKind.Sftp,
        StorageConnectionProvider.Ssh => StorageProviderKind.Ssh,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
