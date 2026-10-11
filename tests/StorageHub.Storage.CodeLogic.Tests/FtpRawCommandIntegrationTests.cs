using CL.Storage;
using CL.Storage.Configuration;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;

namespace StorageHub.Storage.CodeLogic.Tests;

/// <summary>
/// The FTP command console's way to the server, against a real one: a command goes out on a
/// control connection the library leases and then closes, and the reply comes back whole.
/// </summary>
/// <remarks>
/// Runs when <c>STORAGEHUB_FTP_CONSOLE_PORT</c>, <c>_USERNAME</c> and <c>_PASSWORD</c> name a plain
/// FTP server on 127.0.0.1 whose account can see a <c>mounted</c> folder holding <c>a.txt</c>
/// ("hello\n"); pyftpdlib serves one in a few lines. Separate from the FTP/FTPS conformance
/// fixture, which needs four TLS ports and a client certificate this does not.
/// </remarks>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class FtpRawCommandIntegrationTests
{
    [Fact]
    [Trait("Category", "FtpProviderIntegration")]
    public async Task CommandsAreAnsweredOnAConnectionThatIsClosedAfterwards()
    {
        if (Settings() is not { } settings) return;
        var library = await CodeLogicTestFramework.EnsureStartedAsync();
        var profileId = ConnectionProfileId.New();
        var rootIdentity = $"ftp-console-{Guid.NewGuid():N}";
        var registration = await new CodeLogicStorageSessionFactory(library).RegisterAsync(
            profileId,
            rootIdentity,
            new FtpConnectionConfig
            {
                Enabled = true,
                Host = "127.0.0.1",
                Port = settings.Port,
                Root = "mounted",
                EncryptionMode = StorageFtpEncryptionMode.None,
                DataConnectionMode = StorageFtpDataConnectionMode.Pasv,
                Username = settings.Username,
                Password = settings.Password,
                TimeoutSeconds = 10
            });
        Assert.True(registration.IsSuccess, registration.IsFailure ? registration.Error.Message : null);
        await using var connection = registration.Value;
        var raw = Assert.IsAssignableFrom<IStorageRawCommandSession>(connection.Session);

        var system = await raw.ExecuteRawCommandAsync("SYST");
        Assert.True(system.IsSuccess, system.IsFailure ? system.Error.Message : null);
        Assert.Equal("215", system.Value.Code);
        Assert.True(system.Value.Succeeded);
        Assert.StartsWith("215 ", Assert.Single(system.Value.Lines), StringComparison.Ordinal);

        // A multi-line reply keeps every line, codes and all, as the server sent them.
        var features = await raw.ExecuteRawCommandAsync("FEAT");
        Assert.True(features.IsSuccess);
        Assert.Equal("211", features.Value.Code);
        Assert.True(features.Value.Lines.Count > 2);
        Assert.StartsWith("211-", features.Value.Lines[0], StringComparison.Ordinal);
        Assert.StartsWith("211 ", features.Value.Lines[^1], StringComparison.Ordinal);
        Assert.Contains(features.Value.Lines, static line => line.Trim() == "SIZE");

        // The command is sent as typed, so a path is the server's, not the mounted root's. (MDTM
        // rather than SIZE: pyftpdlib answers SIZE only in binary mode, and TYPE does not outlast
        // the command that sets it.)
        var modified = await raw.ExecuteRawCommandAsync("MDTM /mounted/a.txt");
        Assert.True(modified.IsSuccess);
        Assert.Equal("213", modified.Value.Code);
        Assert.Matches("^213 [0-9]{14}", Assert.Single(modified.Value.Lines));

        // A refusal is the server's answer, not a failure.
        var unknown = await raw.ExecuteRawCommandAsync("XYZZY");
        Assert.True(unknown.IsSuccess);
        Assert.StartsWith("5", unknown.Value.Code, StringComparison.Ordinal);
        Assert.False(unknown.Value.Succeeded);

        // What a command changes about its session goes with it: the pane's listing still starts
        // at the mounted root after the console moved its own connection somewhere else.
        var moved = await raw.ExecuteRawCommandAsync("CWD /");
        Assert.True(moved.IsSuccess);
        Assert.True(moved.Value.Succeeded);
        var root = StorageAddress.Create(profileId, rootIdentity, string.Empty).Value;
        var listing = await connection.Session.ListAsync(root);
        Assert.True(listing.IsSuccess, listing.IsFailure ? listing.Error.Message : null);
        Assert.Contains(listing.Value.Entries, static entry => entry.Name == "a.txt");

        // Each of those five commands took a connection of its own and closed it.
        var details = await connection.GetDetailsAsync();
        Assert.True(details.IsSuccess);
        Assert.True(details.Value.Sessions?.Closed >= 5, $"closed: {details.Value.Sessions?.Closed}");

        // A line break is refused before anything is sent.
        var smuggled = await raw.ExecuteRawCommandAsync("NOOP\r\nDELE /mounted/a.txt");
        Assert.True(smuggled.IsFailure);
        Assert.Equal(StorageFailureKind.Validation, smuggled.Error.Kind);
        Assert.True((await raw.ExecuteRawCommandAsync("MDTM /mounted/a.txt")).Value.Succeeded);
    }

    private static (int Port, string Username, string Password)? Settings()
    {
        var port = Environment.GetEnvironmentVariable("STORAGEHUB_FTP_CONSOLE_PORT");
        var username = Environment.GetEnvironmentVariable("STORAGEHUB_FTP_CONSOLE_USERNAME");
        var password = Environment.GetEnvironmentVariable("STORAGEHUB_FTP_CONSOLE_PASSWORD");
        if (port is null && username is null && password is null) return null;
        if (!int.TryParse(port, out var number) || number is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("The FTP console fixture settings are incomplete.");
        }

        return (number, username, password);
    }
}
