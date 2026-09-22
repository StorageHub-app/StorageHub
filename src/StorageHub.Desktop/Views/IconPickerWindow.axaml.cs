using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// What the icon picker answered.
/// </summary>
/// <param name="Chosen">False when it was dismissed, and nothing should change.</param>
/// <param name="Key">The icon chosen, or null for "use the default".</param>
internal readonly record struct IconChoice(bool Chosen, string? Key)
{
    internal static IconChoice Dismissed { get; } = new(false, null);
}

/// <summary>One icon in the grid.</summary>
internal sealed record IconChoiceModel(string Key, LucideIconKind Icon);

/// <summary>Chooses one of the built-in icons, or clears the choice.</summary>
internal sealed class IconPickerModel
{
    internal IconPickerModel(string? currentKey, string title)
    {
        Title = title;
        Choices =
        [
            .. ConnectionIconCatalog.Choices.Select(static choice => new IconChoiceModel(
                choice.Key, Themes.IconCatalog.Resolve(choice.Glyph) ?? LucideIconKind.Cloud))
        ];
        Selected = Choices.FirstOrDefault(choice =>
            string.Equals(choice.Key, currentKey, StringComparison.OrdinalIgnoreCase));
        UseCommand = new RelayCommand(_ => Finish(new IconChoice(true, Selected?.Key)), _ => true);
        UseDefaultCommand = new RelayCommand(_ => Finish(new IconChoice(true, null)));
        CancelCommand = new RelayCommand(_ => Finish(IconChoice.Dismissed));
    }

    public string Title { get; }

    public IReadOnlyList<IconChoiceModel> Choices { get; }

    public IconChoiceModel? Selected { get; set; }

    public ICommand UseCommand { get; }

    public ICommand UseDefaultCommand { get; }

    public ICommand CancelCommand { get; }

    internal IconChoice Result { get; private set; } = IconChoice.Dismissed;

    internal event EventHandler? Closed;

    private void Finish(IconChoice result)
    {
        Result = result;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public static string UseLabel => Ui.Connections.IconPickerUse;

    public static string UseDefaultLabel => Ui.Connections.IconPickerUseDefault;

    public static string CancelLabel => Ui.Connections.IconPickerCancel;
}

/// <summary>
/// The built-in icons as a grid, for a connection and for a group in the connections panel.
/// </summary>
public partial class IconPickerWindow : Window
{
    public IconPickerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is IconPickerModel model) model.Closed += (_, _) => Close();
        };
    }

    /// <summary>Asks over a window, or answers "dismissed" when there is none to ask over.</summary>
    internal static async Task<IconChoice> AskAsync(Window? owner, string? currentKey, string title)
    {
        if (owner is null) return IconChoice.Dismissed;
        var model = new IconPickerModel(currentKey, title);
        await new IconPickerWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Result;
    }
}
