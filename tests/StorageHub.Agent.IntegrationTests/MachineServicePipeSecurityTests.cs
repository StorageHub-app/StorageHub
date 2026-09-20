using System.IO.Pipes;
using System.Security.Principal;
using StorageHub.Ipc;
using StorageHub.Contracts.Ipc;
using StorageHub.Testing;

namespace StorageHub.Agent.IntegrationTests;

/// <summary>
/// A machine-service pipe gives up the protection CurrentUserOnly provided for free: the name is
/// machine-wide and therefore guessable, and the server runs as a different account than its
/// clients. Everything that replaces it is asserted here, because a mistake in this file is the
/// difference between a private channel and one any local process can drive.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class MachineServicePipeSecurityTests
{
    private static NamedPipeIpcServerOptions Options(
        IpcPipeAccess access,
        params string[] permitted) => new()
        {
            PipeName = $"StorageHub.Test.{Guid.NewGuid():N}",
            AgentVersion = "1.0.0",
            Access = access,
            PermittedUserSids = permitted
        };

    /// <summary>
    /// An empty permit list leaves only LocalSystem and Administrators on the ACL. That looks like
    /// a healthy agent and fails at every connect, so it has to be refused at configuration time.
    /// </summary>
    [WindowsOnlyFact]
    public void A_machine_service_pipe_refuses_to_start_with_nobody_permitted()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new NamedPipeIpcServerSubsystem(Options(IpcPipeAccess.MachineService)));

        Assert.Contains("permitted account", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsOnlyFact]
    public void A_machine_service_pipe_refuses_a_malformed_account()
    {
        _ = Assert.Throws<ArgumentException>(
            () => new NamedPipeIpcServerSubsystem(Options(IpcPipeAccess.MachineService, "not-a-sid")));
    }

    [WindowsOnlyFact]
    public void The_default_access_stays_current_user_only()
    {
        Assert.Equal(IpcPipeAccess.CurrentUserOnly, Options(IpcPipeAccess.CurrentUserOnly).Access);
        Assert.Equal(
            IpcPipeAccess.CurrentUserOnly,
            new NamedPipeIpcServerOptions { PipeName = "StorageHub.Test.Default", AgentVersion = "1.0.0" }.Access);
    }

    /// <summary>
    /// The ACL must name exactly LocalSystem, Administrators and the permitted account -- no
    /// Everyone or Authenticated Users ACE, which the guessable name would otherwise expose.
    /// </summary>
    [WindowsOnlyFact]
    public async Task A_machine_service_pipe_grants_only_system_administrators_and_the_permitted_account()
    {
        using var current = WindowsIdentity.GetCurrent();
        var me = current.User!.Value;
        var options = Options(IpcPipeAccess.MachineService, me);
        await using var server = new NamedPipeIpcServerSubsystem(options);
        _ = await server.InitializeAsync(CancellationToken.None);
        await server.StartAsync(CancellationToken.None);
        try
        {
            // Deliberately a raw client rather than NamedPipeIpcClient: a pipe this process
            // creates is owned by whoever runs the tests, so the real client would accept or
            // refuse it depending on the environment. Ownership is proven separately below; what
            // is being proven here is the ACL itself.
            using var probe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut);
            await probe.ConnectAsync(2000, CancellationToken.None);
            var rules = probe.GetAccessControl()
                .GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<PipeAccessRule>()
                .ToArray();

            var granted = rules.Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value).ToHashSet();
            Assert.Contains(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value, granted);
            Assert.Contains(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value, granted);
            Assert.Contains(me, granted);
            Assert.DoesNotContain(new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value, granted);
            Assert.DoesNotContain(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).Value, granted);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// The impostor case, decided by the rule rather than by a live pipe.
    ///
    /// CurrentUserOnly used to answer this for free by comparing the server's owner to the caller.
    /// A service runs as LocalSystem, so the comparison had to be replaced: Windows lets any
    /// process add an instance of an existing pipe name, and the machine pipe's name is guessable,
    /// so an unprivileged squatter could otherwise answer on it and be handed credentials over the
    /// secret channel.
    ///
    /// Asserted against constructed owners because a pipe this process creates is owned by
    /// whoever runs the tests -- an ordinary user locally, an administrator on a build agent -- so
    /// a live pipe tests the environment rather than the rule.
    /// </summary>
    [WindowsOnlyFact]
    public void Only_the_service_accounts_are_trusted_to_own_the_machine_pipe()
    {
        Assert.True(NamedPipeIpcClient.IsTrustedServerOwner(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        Assert.True(NamedPipeIpcClient.IsTrustedServerOwner(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));

        // Constructed SIDs only. Asserting about the account running the tests would reintroduce
        // the environment dependency this test exists to remove.
        Assert.False(NamedPipeIpcClient.IsTrustedServerOwner(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
        Assert.False(NamedPipeIpcClient.IsTrustedServerOwner(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)));
        Assert.False(NamedPipeIpcClient.IsTrustedServerOwner(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null)));

        // An unreadable owner must fail closed rather than be treated as absent-and-fine.
        Assert.False(NamedPipeIpcClient.IsTrustedServerOwner(null));
    }
}
