using CL.Storage;
using CL.Storage.Configuration;
using CodeLogic;

namespace StorageHub.Storage.CodeLogic.Tests;

/// <summary>
/// Starts the CodeLogic framework once for the whole test process.
///
/// CodeLogic initialises globally, and a second <c>InitializeAsync</c> in the same process fails
/// even after <c>StopAsync</c>. Each provider integration class used to bootstrap it from its own
/// <c>InitializeAsync</c>, so whichever test ran first won and every later one failed on
/// <c>Assert.True(initialization.Success)</c>. That only became visible once a fixture was
/// actually configured -- with the fixtures unset the classes return early and never initialise --
/// which is why the suites looked green while being unrunnable together.
///
/// The framework therefore belongs to the process rather than to a test class. The collection
/// that serialises these classes makes the single startup uncontended in practice; the gate keeps
/// it correct regardless.
/// </summary>
internal static class CodeLogicTestFramework
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static StorageLibrary? _library;

    /// <summary>
    /// The registered CL.Storage library, starting the framework on first use.
    /// </summary>
    internal static async Task<StorageLibrary> EnsureStartedAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_library is not null)
            {
                return _library;
            }

            var root = Path.Combine(Path.GetTempPath(), $"storagehub-codelogic-{Guid.NewGuid():N}");
            var initialization = await global::CodeLogic.CodeLogic.InitializeAsync(options =>
            {
                options.FrameworkRootPath = Path.Combine(root, "framework");
                options.ApplicationRootPath = Path.Combine(root, "application");
                options.AppVersion = "test";
                options.HandleShutdownSignals = false;
            });
            Assert.True(initialization.Success);

            await Libraries.LoadAsync<StorageLibrary>();
            Libraries.OverrideConfig<StorageConfig>(
                "CL.Storage",
                "storage",
                configuration => configuration.Enabled = false);
            await global::CodeLogic.CodeLogic.ConfigureAsync();
            await global::CodeLogic.CodeLogic.StartAsync();

            // Deliberately never stopped. The framework cannot be initialised a second time, so
            // stopping it would only strand whichever class ran next; process exit reclaims it.
            _library = Libraries.Get<StorageLibrary>() ??
                throw new InvalidOperationException("CL.Storage was not registered by CodeLogic.");
            return _library;
        }
        finally
        {
            _ = Gate.Release();
        }
    }
}
