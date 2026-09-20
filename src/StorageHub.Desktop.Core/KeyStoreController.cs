using System.Security.Cryptography;
using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>How near a stored certificate is to no longer working.</summary>
internal enum KeyStoreExpiry
{
    /// <summary>Nothing to expire. An SSH key has no end date.</summary>
    None,

    Valid,

    /// <summary>Within thirty days, which is the notice a rotation needs.</summary>
    ExpiringSoon,

    Expired
}

/// <summary>What the key store holds, or why it could not be asked.</summary>
internal sealed record KeyStoreListing(
    IReadOnlyList<KeyStoreEntryDocument> Entries,
    string? ErrorMessage = null)
{
    internal static KeyStoreListing Empty { get; } = new([]);

    internal bool Failed => ErrorMessage is not null;

    /// <param name="searched">
    /// Whether the listing was narrowed by a search. An empty result then means nothing matched,
    /// which is a different sentence from nothing being stored.
    /// </param>
    internal string Describe(bool searched = false) => ErrorMessage ?? (Entries.Count switch
    {
        0 when searched => Ui.KeyStore.NothingMatches,
        0 => Ui.KeyStore.NoKeysOrCertificatesAreStoredYet,
        var count => Ui.Format(Ui.KeyStore.StoredItemsFormat, count)
    });
}

/// <summary>The result of changing the store, or of trying to.</summary>
internal sealed record KeyStoreChangeResult(
    KeyStoreEntryDocument? Entry,
    string? ErrorMessage = null)
{
    internal bool Changed => ErrorMessage is null;
}

/// <summary>
/// What an import needs: the file, what it is, what protects it, and what to call it.
/// </summary>
/// <param name="Passphrase">
/// Empty means the material carries no password. That is legitimate for a PKCS#12 bundle and
/// refused for an SSH key; see <see cref="KeyStoreRules.DescribeMissingPassphrase"/>.
/// </param>
/// <param name="KeyFormat">
/// The envelope an SSH key uses. The agent validates the declared format against the material, so
/// a wrong answer is refused rather than stored. A certificate declares none.
/// </param>
internal sealed record KeyStoreImportDraft(
    KeyStoreMaterialKind Kind,
    string FilePath,
    string DisplayName,
    string Passphrase,
    KeyStorePrivateKeyFormat? KeyFormat);

/// <summary>
/// The rules the key store screens share, with no agent behind them.
/// </summary>
/// <remarks>
/// Both the manager and the picker describe an entry, and the manager and the connection editor
/// both decide what a field can borrow. In 1.x each screen carried its own copy of these -- the
/// expiry description existed twice, character for character -- so this is where they live now.
/// </remarks>
internal static class KeyStoreRules
{
    /// <summary>The largest file that can be enrolled. The vault contract's own bound.</summary>
    internal const long MaximumMaterialBytes = 16 * 1024 * 1024;

    /// <summary>The notice a certificate gets before it expires.</summary>
    internal static readonly TimeSpan ExpiryNotice = TimeSpan.FromDays(30);

    /// <summary>
    /// Explains why material is refused, or null when it can be stored.
    /// </summary>
    /// <remarks>
    /// A PKCS#12 bundle may legitimately carry no password, so an empty value is accepted and no
    /// passphrase is enrolled at all. An SSH private key still requires one: the SFTP connector
    /// rejects an unprotected key outright, so storing one would store something unusable.
    /// </remarks>
    internal static string? DescribeMissingPassphrase(KeyStoreMaterialKind kind, string? value) =>
        !string.IsNullOrEmpty(value) || kind is KeyStoreMaterialKind.Pkcs12Certificate
            ? null
            : Ui.KeyStore.StorageHubCannotStoreAnUnprotectedPrivateKey;

    /// <summary>
    /// Everything wrong with a draft, in the order it should be fixed, or nothing.
    /// </summary>
    /// <remarks>
    /// The file is checked for existence and size here rather than left to the read, because the
    /// import dialog shows this as it is typed: a path to a 20 MB file should dim the button with
    /// the reason, not enrol nothing and explain afterwards.
    /// </remarks>
    internal static string? Validate(KeyStoreImportDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (string.IsNullOrWhiteSpace(draft.FilePath))
        {
            return Ui.KeyStore.ChooseTheFileToImport;
        }

        var file = new FileInfo(draft.FilePath);
        if (!file.Exists)
        {
            return Ui.KeyStore.ChooseTheFileToImport;
        }

        if (file.Length is 0 or > MaximumMaterialBytes)
        {
            return Ui.KeyStore.TheSelectedFileIsEmptyOrLarger;
        }

        if (DescribeMissingPassphrase(draft.Kind, draft.Passphrase) is { } missing)
        {
            return missing;
        }

        if (draft.Kind is KeyStoreMaterialKind.SshPrivateKey && draft.KeyFormat is null)
        {
            return Ui.KeyStore.WhichEnvelopeDoesTheKeyUse;
        }

        return DescribeBadName(draft.DisplayName);
    }

    /// <summary>Why a name will not do, or nothing. The contract's own bounds.</summary>
    internal static string? DescribeBadName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Ui.KeyStore.NameRequired;
        }

        return name.Trim().Length > KeyStoreIpcLimits.MaximumDisplayNameLength || name.Any(char.IsControl)
            ? Ui.KeyStore.NameTooLong
            : null;
    }

    /// <summary>The refusal an agent's write answer amounts to, in words that say what to do.</summary>
    internal static string DescribeFailure(KeyStoreWriteResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Outcome switch
        {
            KeyStoreWriteOutcome.NameConflict => Ui.KeyStore.AnotherEntryAlreadyUsesThatName,
            KeyStoreWriteOutcome.VersionConflict => Ui.KeyStore.TheEntryChangedElsewhereReopenTheKey,
            KeyStoreWriteOutcome.NotFound => Ui.KeyStore.TheEntryNoLongerExists,
            KeyStoreWriteOutcome.StillReferenced => response.ReferencedByProfiles is { Length: > 0 } names
                ? Ui.Format(Ui.KeyStore.EntryStillUsedByFormat, string.Join(", ", names))
                : Ui.KeyStore.TheEntryIsStillUsedByA,
            _ => response.Failure?.Message ?? Ui.KeyStore.TheKeyStoreRejectedTheRequest
        };
    }

    /// <summary>Certificates carry a hard expiry; SSH keys do not.</summary>
    internal static KeyStoreExpiry Expiry(KeyStoreEntryDocument entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Summary.NotAfter is not { } notAfter)
        {
            return KeyStoreExpiry.None;
        }

        var remaining = notAfter - now;
        return remaining <= TimeSpan.Zero
            ? KeyStoreExpiry.Expired
            : remaining <= ExpiryNotice ? KeyStoreExpiry.ExpiringSoon : KeyStoreExpiry.Valid;
    }

    /// <summary>
    /// When the entry stops working, as a date, or that it already has.
    /// </summary>
    /// <remarks>
    /// A certificate that silently breaks a connection when it passes is the thing this column is
    /// for, so the date is shown for everything still valid and the word for what is not.
    /// </remarks>
    internal static string DescribeExpiry(KeyStoreEntryDocument entry, DateTimeOffset now) =>
        Expiry(entry, now) switch
        {
            KeyStoreExpiry.None => Ui.KeyStore.NoExpiry,
            KeyStoreExpiry.Expired => Ui.KeyStore.Expired,
            _ => entry.Summary.NotAfter!.Value.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.CurrentCulture)
        };

    /// <summary>A certificate is its subject; a key is its fingerprint.</summary>
    internal static string DescribeIdentity(KeyStoreEntryDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind is KeyStoreMaterialKind.Pkcs12Certificate
            ? entry.Summary.Subject ?? Ui.KeyStore.UnknownSubject
            : entry.Summary.Sha256Fingerprint ?? Ui.KeyStore.UnknownFingerprint;
    }

    /// <summary>
    /// The two vault purposes a kind of material enrols under: the material and what unlocks it.
    /// </summary>
    internal static (SecretMaterialPurpose Material, SecretMaterialPurpose Passphrase) Purposes(
        KeyStoreMaterialKind kind) => kind switch
    {
        KeyStoreMaterialKind.Pkcs12Certificate =>
            (SecretMaterialPurpose.ClientCertificatePfx, SecretMaterialPurpose.ClientCertificatePassword),
        _ => (SecretMaterialPurpose.SshPrivateKey, SecretMaterialPurpose.SshPrivateKeyPassphrase)
    };
}

/// <summary>
/// Manages the shared key and certificate store: import once, reference from any number of
/// connections, rename, and delete what nothing uses any more.
/// </summary>
/// <remarks>
/// <para>
/// Importing is deliberately a two-step flow. Material is enrolled on the dedicated secret pipe,
/// which returns an opaque reference and never reads anything back; only that reference is then
/// registered on the ordinary pipe. Nothing here holds key material beyond the moment it is sent,
/// and the buffers it does hold are zeroed on the way out.
/// </para>
/// <para>
/// An enrolment that never became an entry is an orphan envelope in the vault, so a create that is
/// refused deletes what it enrolled. Best effort: the entry was not created, so nothing references
/// it, and a cleanup that fails leaves an envelope rather than a broken screen.
/// </para>
/// <para>
/// Extracted from <c>KeyStoreForm</c>, 683 lines of which this was about a hundred and fifty.
/// </para>
/// </remarks>
internal sealed class KeyStoreController(
    Func<IKeyStoreAgentClient> clients,
    Func<IRemoteSecretVaultClient> vaults)
{
    private readonly Func<IKeyStoreAgentClient> _clients =
        clients ?? throw new ArgumentNullException(nameof(clients));

    private readonly Func<IRemoteSecretVaultClient> _vaults =
        vaults ?? throw new ArgumentNullException(nameof(vaults));

    /// <summary>What is stored, narrowed by a search and by the kind a slot accepts.</summary>
    internal async Task<KeyStoreListing> ListAsync(
        string? text = null,
        KeyStoreMaterialKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        await using var client = _clients();
        try
        {
            var response = await client.ListAsync(
                new KeyStoreListRequest(
                    KeyStoreIpcContract.CurrentVersion,
                    string.IsNullOrWhiteSpace(text) ? null : Truncate(text.Trim()),
                    kind,
                    Limit: KeyStoreIpcLimits.MaximumEntriesPerPage),
                cancellationToken).ConfigureAwait(false);

            return response.Failure is { } failure
                ? new KeyStoreListing([], failure.Message)
                : new KeyStoreListing(response.Entries);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (IsExpected(error))
        {
            return new KeyStoreListing([], Ui.Format(Ui.KeyStore.KeyStoreUnavailableFormat, error.Message));
        }
    }

    /// <summary>
    /// Enrols a file and its passphrase, then registers them as one entry.
    /// </summary>
    /// <remarks>
    /// The draft is validated first, so an unprotected SSH key is refused before anything reaches
    /// the vault rather than after the material has been enrolled and has to be deleted again.
    /// </remarks>
    internal async Task<KeyStoreChangeResult> ImportAsync(
        KeyStoreImportDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (KeyStoreRules.Validate(draft) is { } problem)
        {
            return new KeyStoreChangeResult(null, problem);
        }

        var (materialPurpose, passphrasePurpose) = KeyStoreRules.Purposes(draft.Kind);
        byte[]? material = null;
        // An empty value means the material carries no password, so nothing is enrolled for it:
        // the vault stores secrets, not the absence of one.
        var passphrase = Encoding.UTF8.GetBytes(draft.Passphrase);
        string? materialReference = null;
        string? passphraseReference = null;
        try
        {
            material = await File.ReadAllBytesAsync(draft.FilePath, cancellationToken).ConfigureAwait(false);

            await using var vault = _vaults();
            var enrolledMaterial = await vault
                .EnrollAsync(materialPurpose, material, cancellationToken).ConfigureAwait(false);
            if (!enrolledMaterial.Succeeded || enrolledMaterial.Reference is null)
            {
                return new KeyStoreChangeResult(
                    null, enrolledMaterial.Failure?.Message ?? Ui.KeyStore.TheMaterialCouldNotBeEnrolled);
            }

            materialReference = enrolledMaterial.Reference;
            if (passphrase.Length > 0)
            {
                var enrolledPassphrase = await vault
                    .EnrollAsync(passphrasePurpose, passphrase, cancellationToken).ConfigureAwait(false);
                if (!enrolledPassphrase.Succeeded || enrolledPassphrase.Reference is null)
                {
                    return new KeyStoreChangeResult(
                        null,
                        enrolledPassphrase.Failure?.Message ?? Ui.KeyStore.ThePassphraseCouldNotBeEnrolled);
                }

                passphraseReference = enrolledPassphrase.Reference;
            }

            await using var client = _clients();
            var created = await client.CreateAsync(
                new KeyStoreCreateRequest(
                    KeyStoreIpcContract.CurrentVersion,
                    draft.Kind,
                    draft.DisplayName.Trim(),
                    null,
                    [],
                    materialReference,
                    passphraseReference,
                    draft.Kind is KeyStoreMaterialKind.SshPrivateKey ? draft.KeyFormat : null),
                cancellationToken).ConfigureAwait(false);

            if (created.Outcome is not KeyStoreWriteOutcome.Applied)
            {
                return new KeyStoreChangeResult(null, KeyStoreRules.DescribeFailure(created));
            }

            // The agent now owns both envelopes; clearing the locals stops the cleanup below.
            materialReference = null;
            passphraseReference = null;
            return new KeyStoreChangeResult(created.Entry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (IsExpected(error))
        {
            return new KeyStoreChangeResult(null, DesktopAgentAvailability.ReportFailure(error));
        }
        finally
        {
            if (material is not null)
            {
                CryptographicOperations.ZeroMemory(material);
            }

            CryptographicOperations.ZeroMemory(passphrase);
            await DiscardOrphanAsync(materialReference, materialPurpose).ConfigureAwait(false);
            await DiscardOrphanAsync(passphraseReference, passphrasePurpose).ConfigureAwait(false);
        }
    }

    /// <summary>Renames an entry, at the version it was read at.</summary>
    internal async Task<KeyStoreChangeResult> RenameAsync(
        KeyStoreEntryDocument entry,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (KeyStoreRules.DescribeBadName(displayName) is { } problem)
        {
            return new KeyStoreChangeResult(null, problem);
        }

        await using var client = _clients();
        try
        {
            var response = await client.UpdateAsync(
                new KeyStoreUpdateRequest(
                    KeyStoreIpcContract.CurrentVersion,
                    entry.EntryId,
                    displayName.Trim(),
                    entry.Description,
                    entry.Tags,
                    entry.Version),
                cancellationToken).ConfigureAwait(false);

            return response.Outcome is KeyStoreWriteOutcome.Applied
                ? new KeyStoreChangeResult(response.Entry ?? entry with { DisplayName = displayName.Trim() })
                : new KeyStoreChangeResult(null, KeyStoreRules.DescribeFailure(response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (IsExpected(error))
        {
            return new KeyStoreChangeResult(null, DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>
    /// Deletes an entry nothing uses, at the version it was read at.
    /// </summary>
    /// <remarks>
    /// Refused here when the listing already says a connection uses it, naming the connection.
    /// The agent refuses too, but the listing knows the names and the agent's answer may not.
    /// </remarks>
    internal async Task<KeyStoreChangeResult> DeleteAsync(
        KeyStoreEntryDocument entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ReferencedByProfiles.Length > 0)
        {
            return new KeyStoreChangeResult(
                null,
                Ui.Format(
                    Ui.KeyStore.StillUsedByFormat,
                    entry.DisplayName,
                    string.Join(", ", entry.ReferencedByProfiles)));
        }

        await using var client = _clients();
        try
        {
            var response = await client.DeleteAsync(
                new KeyStoreDeleteRequest(KeyStoreIpcContract.CurrentVersion, entry.EntryId, entry.Version),
                cancellationToken).ConfigureAwait(false);

            return response.Outcome is KeyStoreWriteOutcome.Applied
                ? new KeyStoreChangeResult(null)
                : new KeyStoreChangeResult(null, KeyStoreRules.DescribeFailure(response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (IsExpected(error))
        {
            return new KeyStoreChangeResult(null, DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private async Task DiscardOrphanAsync(string? reference, SecretMaterialPurpose purpose)
    {
        if (reference is null) return;
        try
        {
            await using var vault = _vaults();
            _ = await vault.DeleteAsync(reference, purpose, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) when (IsExpected(error))
        {
            // Best effort: the entry was not created, so nothing references the envelope.
        }
    }

    private static string Truncate(string text) =>
        text.Length <= KeyStoreIpcLimits.MaximumSearchTextLength
            ? text
            : text[..KeyStoreIpcLimits.MaximumSearchTextLength];

    private static bool IsExpected(Exception error) => error is
        IOException or UnauthorizedAccessException or InvalidDataException or
        InvalidOperationException or TimeoutException or ObjectDisposedException or
        System.Text.Json.JsonException or ArgumentException;
}
