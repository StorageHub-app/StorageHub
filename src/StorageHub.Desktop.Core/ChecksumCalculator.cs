using System.Security.Cryptography;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One file's SHA-256, where it came from, or why there is none.</summary>
/// <param name="Sha256">Lower-case hex, or null when the checksum could not be had.</param>
internal sealed record ChecksumOutcome(string? Sha256, ObjectChecksumSource Source, long BytesCovered, string? Failure)
{
    internal static ChecksumOutcome Failed(string message) => new(null, ObjectChecksumSource.Computed, 0, message);
}

/// <summary>
/// Tools, Checksums: the SHA-256 of each selected file, for comparing.
/// </summary>
/// <remarks>
/// <para>
/// 1.4 declared Checksums ("Compute and compare content hashes for the selected items") and never
/// wired it. 2.0's object inspector already gets a file's checksum over IPC, the server's own when
/// it keeps a SHA-256 and otherwise read and computed by the agent; this asks the same thing for
/// every selected file on a connection, so the two can never disagree about one.
/// </para>
/// <para>
/// A file on this computer is hashed here, as the desktop reads it for every other local action;
/// asking the agent to read a file the desktop can read itself would only add a hop and the
/// agent's size limit.
/// </para>
/// </remarks>
internal sealed class ChecksumCalculator
{
    /// <summary>The most files one window works through.</summary>
    internal const int MaximumFiles = 200;

    private readonly Func<IObjectInspectorAgentClient> _connect;

    internal ChecksumCalculator(Func<IObjectInspectorAgentClient> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    /// <summary>Whether a selection has a file a checksum can be had for.</summary>
    internal static bool CanCompute(PaneTransferContext? where, IReadOnlyList<PaneTransferItem> items) =>
        where is { Kind: PaneTransferContextKind.SavedConnection or PaneTransferContextKind.ThisPc } &&
        items.Any(static item => item.Kind == StorageItemKind.File);

    /// <summary>The checksum of one file: the server's when it has one, otherwise computed.</summary>
    internal async Task<ChecksumOutcome> ComputeAsync(
        PaneTransferContext where,
        PaneTransferItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind != StorageItemKind.File) return ChecksumOutcome.Failed(Ui.Shell.ChecksumFilesOnly);

        return where.Kind == PaneTransferContextKind.ThisPc
            ? await ComputeLocalAsync(item.RelativePath, cancellationToken).ConfigureAwait(false)
            : await AskAgentAsync(where, item, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a file on this computer through SHA-256.</summary>
    internal static async Task<ChecksumOutcome> ComputeLocalAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1 << 16, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return new ChecksumOutcome(Convert.ToHexStringLower(hash), ObjectChecksumSource.Computed, stream.Length, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ChecksumOutcome.Failed(error.Message);
        }
    }

    private async Task<ChecksumOutcome> AskAgentAsync(
        PaneTransferContext where,
        PaneTransferItem item,
        CancellationToken cancellationToken)
    {
        if (where.ConnectionId is not { } connectionId || where.RootIdentity is not { } root)
        {
            return ChecksumOutcome.Failed(Ui.Shell.InspectionRequiresConnection);
        }

        var address = new ObjectInspectorAddress(
            connectionId, root, item.RelativePath, item.NativeItemId, item.VersionId, item.EntityTag);
        if (!address.HasValidBounds) return ChecksumOutcome.Failed(Ui.Shell.FileHasNoObjectIdentity);

        var client = _connect();
        try
        {
            var response = await client.GetChecksumAsync(
                new ObjectChecksumGetRequest(ObjectInspectorIpcContract.CurrentVersion, address, PreferServer: true),
                cancellationToken).ConfigureAwait(false);
            return response.Failure is { } failure
                ? ChecksumOutcome.Failed(failure.Message)
                : new ChecksumOutcome(response.Value, response.Source, response.BytesCovered, null);
        }
        catch (Exception error) when (error is IOException or TimeoutException or InvalidOperationException or
            InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            return ChecksumOutcome.Failed(error is TimeoutException ? Ui.Shell.ChecksumTimedOut : Ui.Shell.AgentRequestFailed);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A checksum as somebody might paste it: hex, either case, with or without "sha256:" before
    /// it or a file name after, as sha256sum writes it. Null when it is not a SHA-256 at all.
    /// </summary>
    internal static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) value = value[7..].Trim();
        var space = value.IndexOfAny([' ', '\t']);
        if (space > 0) value = value[..space];
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
    }
}
