using System.Globalization;
using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>Where the shell's text comes from.</summary>
internal interface IDesktopStringProvider
{
    T Get<T>() where T : LocalizationModelBase, new();
}

/// <summary>
/// The English text compiled into the shell.
/// </summary>
/// <remarks>
/// This is not only a safety net for a framework that failed to start. It is the provider in three
/// situations that happen normally: the splash draws before localization is loaded, a failure
/// panel needs words precisely when loading did not work, and the desktop tests construct forms
/// without a framework at all. CodeLogic's <c>Get&lt;T&gt;</c> throws when a model is not loaded, so
/// without this the shell could not render a sentence explaining why it could not render.
/// </remarks>
internal sealed class FallbackStringProvider : IDesktopStringProvider
{
    private readonly Dictionary<Type, LocalizationModelBase> _defaults = [];

    public T Get<T>() where T : LocalizationModelBase, new()
    {
        lock (_defaults)
        {
            if (!_defaults.TryGetValue(typeof(T), out var model))
            {
                model = new T();
                _defaults[typeof(T)] = model;
            }

            return (T)model;
        }
    }
}

/// <summary>Reads text from CodeLogic for one culture.</summary>
internal sealed class FrameworkStringProvider : IDesktopStringProvider
{
    private readonly ILocalizationManager _manager;
    private readonly string _culture;
    private readonly IDesktopStringProvider _fallback;

    internal FrameworkStringProvider(
        ILocalizationManager manager,
        string culture,
        IDesktopStringProvider fallback)
    {
        _manager = manager;
        _culture = culture;
        _fallback = fallback;
    }

    public T Get<T>() where T : LocalizationModelBase, new()
    {
        try
        {
            return _manager.Get<T>(_culture);
        }
        catch (InvalidOperationException)
        {
            // A model that was never loaded. Missing words are not a reason to fail a window.
            return _fallback.Get<T>();
        }
    }
}

/// <summary>
/// The shell's text.
/// </summary>
/// <remarks>
/// Every user-facing string in the desktop is read through here, so that switching language is a
/// question of which provider is installed rather than of what each form remembers to do.
/// </remarks>
internal static class Ui
{
    private static IDesktopStringProvider _provider = new FallbackStringProvider();

    internal static ShellStrings Shell => _provider.Get<ShellStrings>();

    internal static CommandStrings Commands => _provider.Get<CommandStrings>();

    internal static DialogStrings Dialogs => _provider.Get<DialogStrings>();

    internal static OverviewStrings Overview => _provider.Get<OverviewStrings>();

    internal static ConnectionStrings Connections => _provider.Get<ConnectionStrings>();

    internal static TransferStrings Transfer => _provider.Get<TransferStrings>();

    internal static SettingsStrings Settings => _provider.Get<SettingsStrings>();

    internal static SyncStrings Sync => _provider.Get<SyncStrings>();

    internal static ScheduleStrings Schedules => _provider.Get<ScheduleStrings>();

    internal static PaneStrings Pane => _provider.Get<PaneStrings>();

    internal static ConnectionEditorStrings ConnectionEditor => _provider.Get<ConnectionEditorStrings>();

    internal static ProviderStrings Providers => _provider.Get<ProviderStrings>();

    internal static KeyStoreStrings KeyStore => _provider.Get<KeyStoreStrings>();

    internal static InspectorStrings Inspector => _provider.Get<InspectorStrings>();

    internal static SettingsTransferStrings SettingsTransfer => _provider.Get<SettingsTransferStrings>();

    internal static UpdateStrings Updates => _provider.Get<UpdateStrings>();

    internal static ValidationStrings Validation => _provider.Get<ValidationStrings>();

    /// <summary>
    /// Formats a localized template. The only formatter the shell uses: a translator may reorder
    /// the placeholders, and the current culture is what decides how the values are rendered.
    /// </summary>
    internal static string Format(string template, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(template);
        return string.Format(CultureInfo.CurrentCulture, template, arguments);
    }

    /// <summary>
    /// The language the shell is speaking.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="CultureInfo.CurrentCulture"/>, which follows the Windows regional
    /// settings and decides how dates and numbers are written. This is the language of the words,
    /// and it is what anything asking .NET for text — a month name, a day name — has to use, or a
    /// Danish shell on an English Windows names its weekdays in English.
    /// </remarks>
    internal static CultureInfo Culture { get; private set; } =
        CultureInfo.GetCultureInfo(DesktopCulture.DefaultCulture);

    /// <summary>Switches to text loaded by the framework, once it is available.</summary>
    internal static void UseFramework(ILocalizationManager manager, string culture)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        _provider = new FrameworkStringProvider(manager, culture, new FallbackStringProvider());
        Culture = ResolveCulture(culture);
    }

    /// <summary>Returns to the compiled-in English text. Exists for tests.</summary>
    internal static void UseFallback()
    {
        _provider = new FallbackStringProvider();
        Culture = CultureInfo.GetCultureInfo(DesktopCulture.DefaultCulture);
    }

    /// <summary>Names the language a provider is speaking, for text that comes from .NET.</summary>
    internal static void UseCulture(string culture) => Culture = ResolveCulture(culture);

    private static CultureInfo ResolveCulture(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            // A culture the runtime does not know is not worth failing a window over; the words
            // themselves still come from the shipped files.
            return CultureInfo.GetCultureInfo(DesktopCulture.DefaultCulture);
        }
    }

    /// <summary>
    /// Installs an arbitrary source of text.
    /// </summary>
    /// <remarks>
    /// Exists so a test can render the shell in a shipped translation without starting the
    /// framework, which is how the settings pages are checked for overflow in German.
    /// </remarks>
    internal static void UseProvider(IDesktopStringProvider provider) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
}
