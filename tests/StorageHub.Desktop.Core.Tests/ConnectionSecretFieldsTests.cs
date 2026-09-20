using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What the connection editor's secret fields mean to the vault and to the key store.
/// </summary>
/// <remarks>
/// The purpose a secret is enrolled under is what the agent checks when the profile later asks for
/// it back, so a field enrolled under the wrong one is a connection that cannot open. The pairs
/// are what let one key store entry fill a material field and its passphrase together.
/// </remarks>
public class ConnectionSecretFieldsTests
{
    [Theory]
    [InlineData("accessKeyReference", (int)SecretMaterialPurpose.AccessKey)]
    [InlineData("secretAccessKeyReference", (int)SecretMaterialPurpose.SecretAccessKey)]
    [InlineData("sessionTokenReference", (int)SecretMaterialPurpose.SessionToken)]
    [InlineData("privateKeyReference", (int)SecretMaterialPurpose.SshPrivateKey)]
    [InlineData("privateKeyPassphraseReference", (int)SecretMaterialPurpose.SshPrivateKeyPassphrase)]
    [InlineData("clientCertificateReference", (int)SecretMaterialPurpose.ClientCertificatePfx)]
    [InlineData("clientCertificatePasswordReference", (int)SecretMaterialPurpose.ClientCertificatePassword)]
    [InlineData("credentialReference", (int)SecretMaterialPurpose.ProxyCredential)]
    [InlineData("passwordReference", (int)SecretMaterialPurpose.Password)]
    public void EveryFieldEnrolsUnderItsOwnPurpose(string key, int purpose) =>
        Assert.Equal((SecretMaterialPurpose)purpose, ConnectionSecretFields.Purpose(key));

    /// <summary>Every secret field the catalog declares has a purpose that is not the fallback, or is a password.</summary>
    [Fact]
    public void EveryCatalogSecretFieldIsKnown()
    {
        var keys = ConnectionProviderCatalog.All
            .SelectMany(provider => provider.GeneralFields.Concat(provider.AuthenticationFields).Concat(provider.SecurityFields))
            .Where(field => field.Kind is ConnectionFieldKind.SecretReference or ConnectionFieldKind.CertificateReference)
            .Select(field => field.Key)
            .Distinct();

        foreach (var key in keys)
        {
            Assert.True(
                key == "passwordReference" || ConnectionSecretFields.Purpose(key) != SecretMaterialPurpose.Password,
                $"'{key}' would be enrolled as a password.");
        }
    }

    /// <summary>Only the two material fields can borrow from the key store, each with its passphrase.</summary>
    [Fact]
    public void OnlyMaterialFieldsAreKeyStoreSlots()
    {
        Assert.Equal(KeyStoreMaterialKind.SshPrivateKey, ConnectionSecretFields.KeyStoreSlot("privateKeyReference"));
        Assert.Equal(KeyStoreMaterialKind.Pkcs12Certificate, ConnectionSecretFields.KeyStoreSlot("clientCertificateReference"));
        Assert.Null(ConnectionSecretFields.KeyStoreSlot("privateKeyPassphraseReference"));
        Assert.Null(ConnectionSecretFields.KeyStoreSlot("passwordReference"));

        Assert.Equal("privateKeyPassphraseReference", ConnectionSecretFields.CompanionPassphrase("privateKeyReference"));
        Assert.Equal("clientCertificatePasswordReference", ConnectionSecretFields.CompanionPassphrase("clientCertificateReference"));
        Assert.Null(ConnectionSecretFields.CompanionPassphrase("passwordReference"));
    }

    /// <summary>A file is enrolled from disk; everything else is typed.</summary>
    [Fact]
    public void MaterialComesFromAFileAndTheRestIsTyped()
    {
        Assert.True(ConnectionSecretFields.IsFile("privateKeyReference"));
        Assert.True(ConnectionSecretFields.IsFile("clientCertificateReference"));
        Assert.False(ConnectionSecretFields.IsFile("privateKeyPassphraseReference"));
        Assert.False(ConnectionSecretFields.IsFile("accessKeyReference"));
    }
}
