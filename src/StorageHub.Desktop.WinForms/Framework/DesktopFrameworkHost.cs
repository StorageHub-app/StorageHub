using CodeLogic.Core.Configuration;
using CodeLogic.Core.Localization;
using CodeLogic.Core.Utilities;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Framework;

/// <summary>
/// Boots CodeLogic for the desktop shell.
/// </summary>
/// <remarks>
/// This is deliberately the only file in the desktop that touches the <c>CodeLogic.CodeLogic</c>
/// facade. Everything else consumes the managers it exposes. That keeps the choice of "full
/// framework" versus "just construct the two managers" a one-file decision rather than a
/// commitment spread across the UI.
/// </remarks>
internal sealed class DesktopFrameworkHost : IAsyncDisposable
{
    private bool _started;

    internal DesktopFrameworkHost(DesktopFrameworkPaths paths) =>
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));

    internal DesktopFrameworkPaths Paths { get; }

    /// <summary>Non-fatal environment findings from the startup validator, for the splash to surface.</summary>
    internal IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>Set once <see cref="StartAsync"/> succeeds.</summary>
    internal IConfigurationManager? Configuration { get; private set; }

    /// <summary>Set once <see cref="StartAsync"/> succeeds.</summary>
    internal ILocalizationManager? Localization { get; private set; }

    /// <summary>The language the shell settled on.</summary>
    internal string Culture { get; private set; } = DesktopCulture.DefaultCulture;

    /// <summary>
    /// Runs the framework through to started, reporting each step. Throws
    /// <see cref="DesktopBootException"/> for anything the user has to be told about.
    /// </summary>
    internal async Task StartAsync(
        IProgress<BootStatus> progress,
        string? configuredLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(BootStatus.PreparingData);
        try
        {
            Paths.EnsureCreated();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new DesktopBootException(
                BootStage.PreparingData,
                $"StorageHub could not prepare its data folder at {Paths.ApplicationRoot}. {error.Message}",
                error);
        }

        progress.Report(BootStatus.StartingFramework);
        var initialization = await CodeLogic.CodeLogic.InitializeAsync(options =>
        {
            options.FrameworkRootPath = Paths.FrameworkRoot;
            options.ApplicationRootPath = Paths.ApplicationRoot;
            options.AppVersion = DesktopApplicationVersion.Current;

            // WinForms owns the shutdown story. Letting the framework hook Ctrl+C and ProcessExit
            // would give a windowed app a second, invisible shutdown path.
            options.HandleShutdownSignals = false;
        }).ConfigureAwait(false);

        // InitializeAsync swallows every exception into a message, so this string is the only
        // diagnostic there is. Carry it verbatim rather than restating it.
        if (!initialization.Success || initialization.ShouldExit)
        {
            throw new DesktopBootException(BootStage.StartingFramework, initialization.Message);
        }

        _started = true;
        cancellationToken.ThrowIfCancellationRequested();

        // ConfigureAsync runs the validator itself, but keeps the instance private and only logs
        // the warnings. Running it here is the only way to show them, and it turns ConfigureAsync's
        // opaque "Startup validation failed: ..." into a message we wrote. The second run inside
        // ConfigureAsync is idempotent: it creates directories and touches a probe file.
        progress.Report(BootStatus.CheckingEnvironment);
        var validator = new StartupValidator();
        var validation = validator.Validate(Paths.FrameworkRoot);
        Warnings = validator.GetWarnings();
        if (!validation.IsSuccess)
        {
            throw new DesktopBootException(BootStage.CheckingEnvironment, validation.ErrorMessage);
        }

        // Both have to happen before ConfigureAsync: it is what generates and loads the
        // localization files, and it only touches the cultures CodeLogic.json names.
        Paths.EnsureSupportedCultures();
        Paths.SeedShippedTranslations(DesktopApplicationVersion.Current);

        CodeLogic.CodeLogic.RegisterApplication(new StorageHubDesktopApplication());

        progress.Report(BootStatus.LoadingLanguage);
        try
        {
            await CodeLogic.CodeLogic.ConfigureAsync().ConfigureAwait(false);
            await CodeLogic.CodeLogic.StartAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new DesktopBootException(BootStage.LoadingLanguage, error.Message, error);
        }

        var context = CodeLogic.CodeLogic.GetApplicationContext()
            ?? throw new DesktopBootException(
                BootStage.LoadingLanguage,
                "The StorageHub application context was not created.");
        Configuration = context.Configuration;
        Localization = context.Localization;

        // ConfigureAsync loads only the cultures named in CodeLogic.json, and it read that file
        // during InitializeAsync — before this class had a chance to add ours. Patching the file
        // alone would therefore not take effect until the next launch. Naming the cultures here
        // instead makes the shipped languages available on the very first run, and does not depend
        // on the framework configuration being writable.
        try
        {
            await context.Localization
                .LoadAllAsync(DesktopCulture.SupportedCultures, generateIfMissing: true)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // English is compiled in, so a language that will not load is a degraded start rather
            // than a failed one.
        }

        Culture = DesktopCulture.ResolveCurrent(configuredLanguage);
        Ui.UseFramework(context.Localization, Culture);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        try
        {
            await CodeLogic.CodeLogic.StopAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            // Shutting down is best effort: the process is going away regardless, and a framework
            // that never fully started has nothing to stop.
        }
    }
}
