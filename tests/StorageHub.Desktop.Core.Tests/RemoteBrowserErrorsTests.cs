using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

public sealed class RemoteBrowserErrorsTests
{
    [Fact]
    public void CredentialRejectionIsNotReportedAsTrustFailure()
    {
        var failure = Failure(StorageIpcFailureCategory.Unauthorized, "storage.unauthorized");

        var message = RemoteBrowserErrors.ForFailure(failure);

        Assert.Contains("credential", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trust", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrustFailureIsNotReportedAsCredentialRejection()
    {
        var failure = Failure(StorageIpcFailureCategory.Security, "storage.security");

        var message = RemoteBrowserErrors.ForFailure(failure);

        Assert.Contains("trust", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Logged in but not allowed is not a bad password: since CL.Storage 4.8.93 the code says which,
    /// under the same category.
    /// </summary>
    [Fact]
    public void ARefusedFolderIsNotReportedAsARejectedLogin()
    {
        Assert.Equal(
            Ui.Validation.TheProviderAcceptedTheLoginButRefused,
            RemoteBrowserErrors.ForFailure(Failure(StorageIpcFailureCategory.Unauthorized, "storage.permission_denied")));
        Assert.Equal(
            Ui.Validation.TheProviderRejectedTheSavedUsernameOr,
            RemoteBrowserErrors.ForFailure(Failure(StorageIpcFailureCategory.Unauthorized, "storage.authentication_failed")));
    }

    [Theory]
    [InlineData(StorageIpcFailureCategory.Unavailable, "storage.connection_failed")]
    [InlineData(StorageIpcFailureCategory.Unavailable, "storage.connection_lost")]
    [InlineData(StorageIpcFailureCategory.Unavailable, "storage.server_busy")]
    [InlineData(StorageIpcFailureCategory.Provider, "storage.quota_exceeded")]
    [InlineData(StorageIpcFailureCategory.Security, "storage.trust.host_key_rejected")]
    [InlineData(StorageIpcFailureCategory.Security, "storage.tls_failure")]
    public void EachPreciseCodeHasItsOwnSentence(StorageIpcFailureCategory category, string code)
    {
        var precise = RemoteBrowserErrors.ForFailure(Failure(category, code));
        var general = RemoteBrowserErrors.ForFailure(Failure(category, "storage.something_else"));

        Assert.NotEqual(general, precise);
    }

    private static StorageIpcFailure Failure(StorageIpcFailureCategory category, string code) =>
        new(code, category, "Sanitized agent message.", IsTransient: false);
}
