using System.Text;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Importing, listing, renaming and deleting key store entries.
/// </summary>
/// <remarks>
/// The store holds the one thing a connection cannot work without and cannot get back, so what is
/// checked here is the order things happen in: that nothing is enrolled before the draft is one
/// the agent will take, that a refused registration deletes what it enrolled, and that every
/// change carries the version it was read at.
/// </remarks>
public class KeyStoreControllerTests
{
    [Fact]
    public async Task AListingIsNarrowedByTheSearchAndTheKind()
    {
        var agent = new FakeKeyStoreAgent();

        _ = await Controller(agent).ListAsync("  photo ", KeyStoreMaterialKind.SshPrivateKey, CancellationToken.None);

        Assert.Equal("photo", agent.Listed!.Text);
        Assert.Equal(KeyStoreMaterialKind.SshPrivateKey, agent.Listed.Kind);
        Assert.Equal(KeyStoreIpcLimits.MaximumEntriesPerPage, agent.Listed.Limit);
    }

    /// <summary>Nothing stored and nothing matching are different sentences.</summary>
    [Fact]
    public async Task AnEmptyListingSaysWhyItIsEmpty()
    {
        var listing = await Controller(new FakeKeyStoreAgent()).ListAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(Ui.KeyStore.NoKeysOrCertificatesAreStoredYet, listing.Describe());
        Assert.Equal(Ui.KeyStore.NothingMatches, listing.Describe(searched: true));
    }

    [Fact]
    public async Task AnUnreachableAgentIsAMessage()
    {
        var agent = new FakeKeyStoreAgent { Throws = new IOException("there is no agent") };

        var listing = await Controller(agent).ListAsync(cancellationToken: CancellationToken.None);

        Assert.True(listing.Failed);
        Assert.Empty(listing.Entries);
    }

    /// <summary>
    /// An import enrols the file, then the passphrase, then registers both references.
    /// </summary>
    /// <remarks>
    /// Two enrolments on the secret pipe and one registration on the ordinary one. The bytes the
    /// vault receives are the file's, under the purpose the connector will later ask for them by,
    /// and the registration carries the envelope the agent will validate the material against.
    /// </remarks>
    [Fact]
    public async Task ImportingEnrolsTheFileAndThePassphraseThenRegistersBoth()
    {
        var agent = new FakeKeyStoreAgent();
        var vault = new RecordingVault();
        using var file = TemporaryFile("-----BEGIN OPENSSH PRIVATE KEY-----");

        var result = await Controller(agent, vault).ImportAsync(
            new KeyStoreImportDraft(
                KeyStoreMaterialKind.SshPrivateKey, file.Path, "  build box  ", "open sesame",
                KeyStorePrivateKeyFormat.OpenSsh),
            CancellationToken.None);

        Assert.True(result.Changed, result.ErrorMessage);
        Assert.NotNull(result.Entry);

        Assert.Collection(
            vault.Enrolled,
            material =>
            {
                Assert.Equal(SecretMaterialPurpose.SshPrivateKey, material.Purpose);
                Assert.Equal(File.ReadAllBytes(file.Path), material.Secret);
            },
            passphrase =>
            {
                Assert.Equal(SecretMaterialPurpose.SshPrivateKeyPassphrase, passphrase.Purpose);
                Assert.Equal("open sesame"u8.ToArray(), passphrase.Secret);
            });

        var created = agent.Created!;
        Assert.Equal("build box", created.DisplayName);
        Assert.Equal(vault.Enrolled[0].Reference, created.MaterialReference);
        Assert.Equal(vault.Enrolled[1].Reference, created.PassphraseReference);
        Assert.Equal(KeyStorePrivateKeyFormat.OpenSsh, created.KeyFormat);
        Assert.Empty(vault.Deleted);
    }

    /// <summary>A bundle with no password enrols no passphrase, and declares no envelope.</summary>
    [Fact]
    public async Task ACertificateWithoutAPasswordEnrolsOnlyTheBundle()
    {
        var agent = new FakeKeyStoreAgent();
        var vault = new RecordingVault();
        using var file = TemporaryFile("pkcs12");

        var result = await Controller(agent, vault).ImportAsync(
            new KeyStoreImportDraft(
                KeyStoreMaterialKind.Pkcs12Certificate, file.Path, "client cert", string.Empty,
                KeyStorePrivateKeyFormat.OpenSsh),
            CancellationToken.None);

        Assert.True(result.Changed, result.ErrorMessage);
        var enrolled = Assert.Single(vault.Enrolled);
        Assert.Equal(SecretMaterialPurpose.ClientCertificatePfx, enrolled.Purpose);
        Assert.Null(agent.Created!.PassphraseReference);
        Assert.Null(agent.Created.KeyFormat);
    }

    /// <summary>
    /// An unprotected SSH key is refused before anything reaches the vault.
    /// </summary>
    /// <remarks>
    /// The SFTP connector rejects an unprotected key outright, so storing one would store something
    /// unusable. Refusing it after enrolling would leave an orphan to clean up for a draft that was
    /// never going to be accepted.
    /// </remarks>
    [Fact]
    public async Task AnUnprotectedSshKeyIsRefusedBeforeAnythingIsEnrolled()
    {
        var agent = new FakeKeyStoreAgent();
        var vault = new RecordingVault();
        using var file = TemporaryFile("key");

        var result = await Controller(agent, vault).ImportAsync(
            new KeyStoreImportDraft(
                KeyStoreMaterialKind.SshPrivateKey, file.Path, "build box", string.Empty,
                KeyStorePrivateKeyFormat.OpenSsh),
            CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(Ui.KeyStore.StorageHubCannotStoreAnUnprotectedPrivateKey, result.ErrorMessage);
        Assert.Empty(vault.Enrolled);
        Assert.Null(agent.Created);
    }

    [Fact]
    public async Task AnEmptyFileIsRefused()
    {
        var vault = new RecordingVault();
        using var file = TemporaryFile(string.Empty);

        var result = await Controller(new FakeKeyStoreAgent(), vault).ImportAsync(
            new KeyStoreImportDraft(
                KeyStoreMaterialKind.Pkcs12Certificate, file.Path, "client cert", "pw", null),
            CancellationToken.None);

        Assert.Equal(Ui.KeyStore.TheSelectedFileIsEmptyOrLarger, result.ErrorMessage);
        Assert.Empty(vault.Enrolled);
    }

    [Fact]
    public async Task AMissingFileIsRefused()
    {
        var result = await Controller(new FakeKeyStoreAgent(), new RecordingVault()).ImportAsync(
            new KeyStoreImportDraft(
                KeyStoreMaterialKind.Pkcs12Certificate,
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                "client cert", "pw", null),
            CancellationToken.None);

        Assert.Equal(Ui.KeyStore.ChooseTheFileToImport, result.ErrorMessage);
    }

    /// <summary>
    /// A registration the agent refuses deletes what was enrolled for it.
    /// </summary>
    /// <remarks>
    /// Otherwise a name conflict leaves two envelopes in the vault that nothing references and
    /// nothing can list, one of them a private key.
    /// </remarks>
    [Fact]
    public async Task ARefusedRegistrationDeletesWhatItEnrolled()
    {
        var agent = new FakeKeyStoreAgent { Outcome = KeyStoreWriteOutcome.NameConflict };
        var vault = new RecordingVault();
        using var file = TemporaryFile("key");

        var result = await Controller(agent, vault).ImportAsync(
            new KeyStoreImportDraft(
                KeyStoreMaterialKind.SshPrivateKey, file.Path, "build box", "open sesame",
                KeyStorePrivateKeyFormat.Pem),
            CancellationToken.None);

        Assert.Equal(Ui.KeyStore.AnotherEntryAlreadyUsesThatName, result.ErrorMessage);
        Assert.Equal(
            vault.Enrolled.Select(static enrolled => (enrolled.Reference, enrolled.Purpose)),
            vault.Deleted);
    }

    /// <summary>And a refused passphrase enrolment deletes the material already enrolled.</summary>
    [Fact]
    public async Task ARefusedPassphraseDeletesTheMaterial()
    {
        var agent = new FakeKeyStoreAgent();
        var vault = new RecordingVault { RefuseAfter = 1 };
        using var file = TemporaryFile("key");

        var result = await Controller(agent, vault).ImportAsync(
            new KeyStoreImportDraft(
                KeyStoreMaterialKind.SshPrivateKey, file.Path, "build box", "open sesame",
                KeyStorePrivateKeyFormat.Pem),
            CancellationToken.None);

        Assert.Equal(Ui.KeyStore.ThePassphraseCouldNotBeEnrolled, result.ErrorMessage);
        Assert.Null(agent.Created);
        var deleted = Assert.Single(vault.Deleted);
        Assert.Equal(vault.Enrolled[0].Reference, deleted.Reference);
    }

    [Fact]
    public async Task RenamingCarriesTheVersionItWasReadAt()
    {
        var agent = new FakeKeyStoreAgent();
        var entry = Entry("build box", KeyStoreMaterialKind.SshPrivateKey) with { Version = 4 };

        var result = await Controller(agent).RenameAsync(entry, "  build box 2 ", CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(4, agent.Updated!.ExpectedVersion);
        Assert.Equal("build box 2", agent.Updated.DisplayName);
        Assert.Equal(entry.EntryId, agent.Updated.EntryId);
    }

    [Fact]
    public async Task ABlankNameIsNotSent()
    {
        var agent = new FakeKeyStoreAgent();

        var result = await Controller(agent)
            .RenameAsync(Entry("build box", KeyStoreMaterialKind.SshPrivateKey), "   ", CancellationToken.None);

        Assert.Equal(Ui.KeyStore.NameRequired, result.ErrorMessage);
        Assert.Null(agent.Updated);
    }

    /// <summary>An entry a connection still names is refused here, naming the connection.</summary>
    [Fact]
    public async Task DeletingAnEntryAConnectionUsesIsRefusedByName()
    {
        var agent = new FakeKeyStoreAgent();
        var entry = Entry("build box", KeyStoreMaterialKind.SshPrivateKey) with
        {
            ReferencedByProfiles = ["Studio SFTP"]
        };

        var result = await Controller(agent).DeleteAsync(entry, CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains("Studio SFTP", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(agent.Deleted);
    }

    [Fact]
    public async Task DeletingCarriesTheVersionItWasReadAt()
    {
        var agent = new FakeKeyStoreAgent();
        var entry = Entry("build box", KeyStoreMaterialKind.SshPrivateKey) with { Version = 9 };

        var result = await Controller(agent).DeleteAsync(entry, CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(9, agent.Deleted!.ExpectedVersion);
    }

    /// <summary>Every refusal the contract can answer with has a sentence that says what to do.</summary>
    [Theory]
    [InlineData((int)KeyStoreWriteOutcome.NotFound)]
    [InlineData((int)KeyStoreWriteOutcome.VersionConflict)]
    [InlineData((int)KeyStoreWriteOutcome.NameConflict)]
    [InlineData((int)KeyStoreWriteOutcome.StillReferenced)]
    [InlineData((int)KeyStoreWriteOutcome.Rejected)]
    public void EveryRefusalHasWords(int outcomeValue)
    {
        var response = new KeyStoreWriteResponse(
            KeyStoreIpcContract.CurrentVersion, (KeyStoreWriteOutcome)outcomeValue);

        var described = KeyStoreRules.DescribeFailure(response);

        Assert.False(string.IsNullOrWhiteSpace(described));
        Assert.DoesNotContain(((KeyStoreWriteOutcome)outcomeValue).ToString(), described, StringComparison.Ordinal);
    }

    /// <summary>And a refusal that names the connections says which.</summary>
    [Fact]
    public void AStillReferencedRefusalNamesTheConnections()
    {
        var response = new KeyStoreWriteResponse(
            KeyStoreIpcContract.CurrentVersion,
            KeyStoreWriteOutcome.StillReferenced,
            ReferencedByProfiles: ["Studio SFTP", "Backup box"]);

        Assert.Equal(
            Ui.Format(Ui.KeyStore.EntryStillUsedByFormat, "Studio SFTP, Backup box"),
            KeyStoreRules.DescribeFailure(response));
    }

    /// <summary>
    /// A key never expires; a certificate warns thirty days out and says so once it has.
    /// </summary>
    [Theory]
    [InlineData(null, (int)KeyStoreExpiry.None)]
    [InlineData(400, (int)KeyStoreExpiry.Valid)]
    [InlineData(30, (int)KeyStoreExpiry.ExpiringSoon)]
    [InlineData(1, (int)KeyStoreExpiry.ExpiringSoon)]
    [InlineData(0, (int)KeyStoreExpiry.Expired)]
    [InlineData(-5, (int)KeyStoreExpiry.Expired)]
    public void ExpiryWarnsThirtyDaysOut(int? daysLeft, int expected)
    {
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var entry = Entry("cert", KeyStoreMaterialKind.Pkcs12Certificate) with
        {
            Summary = new KeyStoreSummaryDocument(NotAfter: daysLeft is { } days ? now.AddDays(days) : null)
        };

        Assert.Equal((KeyStoreExpiry)expected, KeyStoreRules.Expiry(entry, now));

        var described = KeyStoreRules.DescribeExpiry(entry, now);
        switch ((KeyStoreExpiry)expected)
        {
            case KeyStoreExpiry.None:
                Assert.Equal(Ui.KeyStore.NoExpiry, described);
                break;
            case KeyStoreExpiry.Expired:
                Assert.Equal(Ui.KeyStore.Expired, described);
                break;
            default:
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", described);
                break;
        }
    }

    /// <summary>A certificate is its subject; a key is its fingerprint.</summary>
    [Fact]
    public void IdentityIsTheSubjectOrTheFingerprint()
    {
        var certificate = Entry("cert", KeyStoreMaterialKind.Pkcs12Certificate) with
        {
            Summary = new KeyStoreSummaryDocument(Subject: "CN=client", Sha256Fingerprint: "SHA256:abc")
        };
        var key = Entry("key", KeyStoreMaterialKind.SshPrivateKey) with
        {
            Summary = new KeyStoreSummaryDocument(Subject: "CN=client", Sha256Fingerprint: "SHA256:abc")
        };

        Assert.Equal("CN=client", KeyStoreRules.DescribeIdentity(certificate));
        Assert.Equal("SHA256:abc", KeyStoreRules.DescribeIdentity(key));
        Assert.Equal(
            Ui.KeyStore.UnknownFingerprint,
            KeyStoreRules.DescribeIdentity(key with { Summary = new KeyStoreSummaryDocument() }));
    }

    private static KeyStoreController Controller(FakeKeyStoreAgent agent, RecordingVault? vault = null) =>
        new(() => agent, () => vault ?? new RecordingVault());

    internal static KeyStoreEntryDocument Entry(string name, KeyStoreMaterialKind kind) => new(
        Guid.NewGuid(),
        kind,
        name,
        null,
        [],
        Reference('m'),
        kind is KeyStoreMaterialKind.SshPrivateKey ? Reference('p') : null,
        new KeyStoreSummaryDocument(),
        Version: 1,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        []);

    /// <summary>A reference of the shape the contract accepts: "shs_" and 43 safe characters.</summary>
    internal static string Reference(char fill) => "shs_" + new string(fill, 43);

    private static TemporaryFileHandle TemporaryFile(string contents) => new(contents);

    private sealed class TemporaryFileHandle : IDisposable
    {
        internal TemporaryFileHandle(string contents)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "storagehub-keystore-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(Path, contents, Encoding.ASCII);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The store, written down instead of written to.</summary>
    internal sealed class FakeKeyStoreAgent : IKeyStoreAgentClient
    {
        internal List<KeyStoreEntryDocument> Entries { get; } = [];

        internal KeyStoreWriteOutcome Outcome { get; set; } = KeyStoreWriteOutcome.Applied;

        internal Exception? Throws { get; set; }

        internal KeyStoreListRequest? Listed { get; private set; }

        internal KeyStoreCreateRequest? Created { get; private set; }

        internal KeyStoreUpdateRequest? Updated { get; private set; }

        internal KeyStoreDeleteRequest? Deleted { get; private set; }

        public Task<KeyStoreListResponse> ListAsync(
            KeyStoreListRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            Listed = request;
            var matching = Entries
                .Where(entry => request.Kind is null || entry.Kind == request.Kind)
                .Where(entry => request.Text is null ||
                    entry.DisplayName.Contains(request.Text, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Task.FromResult(new KeyStoreListResponse(KeyStoreIpcContract.CurrentVersion, matching));
        }

        public Task<KeyStoreWriteResponse> CreateAsync(
            KeyStoreCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            Created = request;
            if (Outcome is not KeyStoreWriteOutcome.Applied)
            {
                return Task.FromResult(new KeyStoreWriteResponse(KeyStoreIpcContract.CurrentVersion, Outcome));
            }

            var entry = new KeyStoreEntryDocument(
                Guid.NewGuid(), request.Kind, request.DisplayName, request.Description, request.Tags,
                request.MaterialReference, request.PassphraseReference,
                new KeyStoreSummaryDocument(KeyFormat: request.KeyFormat), 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
            Entries.Add(entry);
            return Task.FromResult(new KeyStoreWriteResponse(
                KeyStoreIpcContract.CurrentVersion, KeyStoreWriteOutcome.Applied, entry));
        }

        public Task<KeyStoreWriteResponse> UpdateAsync(
            KeyStoreUpdateRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            Updated = request;
            var existing = Entries.FirstOrDefault(entry => entry.EntryId == request.EntryId);
            var renamed = existing is null
                ? null
                : existing with { DisplayName = request.DisplayName, Version = existing.Version + 1 };
            if (renamed is not null) Entries[Entries.IndexOf(existing!)] = renamed;
            return Task.FromResult(new KeyStoreWriteResponse(KeyStoreIpcContract.CurrentVersion, Outcome, renamed));
        }

        public Task<KeyStoreWriteResponse> DeleteAsync(
            KeyStoreDeleteRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            Deleted = request;
            Entries.RemoveAll(entry => entry.EntryId == request.EntryId);
            return Task.FromResult(new KeyStoreWriteResponse(KeyStoreIpcContract.CurrentVersion, Outcome));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The vault, which answers with a reference and remembers what it was given.</summary>
    internal sealed class RecordingVault : IRemoteSecretVaultClient
    {
        private int _count;

        internal List<(string Reference, SecretMaterialPurpose Purpose, byte[] Secret)> Enrolled { get; } = [];

        internal List<(string Reference, SecretMaterialPurpose Purpose)> Deleted { get; } = [];

        /// <summary>How many enrolments succeed before the vault starts refusing. Null: all of them.</summary>
        internal int? RefuseAfter { get; set; }

        public Task<SecretVaultResponse> EnrollAsync(
            SecretMaterialPurpose purpose, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken = default)
        {
            if (RefuseAfter is { } limit && Enrolled.Count >= limit)
            {
                return Task.FromResult(new SecretVaultResponse(
                    SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Enroll, Succeeded: false));
            }

            var reference = Reference((char)('a' + _count++));
            Enrolled.Add((reference, purpose, secret.ToArray()));
            return Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Enroll, true, reference, 1));
        }

        public Task<SecretVaultResponse> UpdateAsync(
            string reference, SecretMaterialPurpose purpose, ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default)
        {
            Enrolled.Add((reference, purpose, secret.ToArray()));
            return Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Update, true, reference, 2));
        }

        public Task<SecretVaultResponse> DeleteAsync(
            string reference, SecretMaterialPurpose purpose, CancellationToken cancellationToken = default)
        {
            Deleted.Add((reference, purpose));
            return Task.FromResult(new SecretVaultResponse(
                SecretVaultIpcContract.CurrentVersion, SecretVaultOperation.Delete, true, reference));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
