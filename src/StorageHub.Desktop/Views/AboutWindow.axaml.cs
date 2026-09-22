using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>Help, About: the version, and the project's address.</summary>
internal sealed class AboutModel
{
    private readonly Func<Uri, Task<bool>> _launch;
    private readonly IDialogService? _dialogs;

    /// <param name="launch">
    /// Opens an address in the browser, answering whether anything did. The shell's is the window's
    /// own launcher, which is the platform's -- ShellExecute on Windows, xdg-open or a portal on
    /// Linux -- rather than a process started by hand.
    /// </param>
    internal AboutModel(Func<Uri, Task<bool>> launch, IDialogService? dialogs = null)
    {
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _dialogs = dialogs;
        OpenProjectCommand = new RelayCommand(_ => _ = OpenProjectAsync());
        CloseCommand = new RelayCommand(_ => Closed?.Invoke(this, EventArgs.Empty));
    }

    public static string Title => Ui.Dialogs.AboutCaption;

    public static string Body => Ui.Format(Ui.Dialogs.AboutBodyFormat, DesktopApplicationVersion.Current);

    public static string ProjectLink => Ui.Dialogs.AboutProjectLink;

    /// <summary>
    /// Where the project lives: the same constant the updater trusts for releases, so About cannot
    /// come to disagree with where new versions come from.
    /// </summary>
    public static string ProjectUrl => StorageHubLinks.Project;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    public ICommand OpenProjectCommand { get; }

    public ICommand CloseCommand { get; }

    internal event EventHandler? Closed;

    /// <summary>
    /// Opens the project page, or shows its address when nothing could.
    /// </summary>
    /// <remarks>
    /// A machine with no browser registered still gets the useful part -- the address -- rather
    /// than a link that silently does nothing.
    /// </remarks>
    internal async Task OpenProjectAsync()
    {
        if (await _launch(new Uri(ProjectUrl)).ConfigureAwait(true)) return;
        if (_dialogs is null) return;
        await _dialogs.ShowAsync(new DialogRequest { Title = Title, Message = ProjectUrl }).ConfigureAwait(true);
    }
}

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AboutModel model) model.Closed += (_, _) => Close();
        };
    }

    internal static AboutWindow Create()
    {
        var window = new AboutWindow();
        window.DataContext = new AboutModel(
            uri => window.Launcher.LaunchUriAsync(uri),
            Services.ShellServices.Dialogs);
        return window;
    }
}
