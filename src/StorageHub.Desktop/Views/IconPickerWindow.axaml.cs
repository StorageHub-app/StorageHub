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
/// <param name="Color">
/// The colour chosen as #RRGGBB, or null for none. Only a picker opened with colours sets it.
/// </param>
internal readonly record struct IconChoice(bool Chosen, string? Key, string? Color = null)
{
    internal static IconChoice Dismissed { get; } = new(false, null);
}

/// <summary>One icon in the grid.</summary>
internal sealed record IconChoiceModel(string Key, LucideIconKind Icon);

/// <summary>One of the colours a group can wear, and whether it is the one chosen.</summary>
internal sealed class ColorChoiceModel(string hex) : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isChosen;

    public string Hex { get; } = hex;

    public Avalonia.Media.IBrush Brush => AccentSwatch.BrushFor(Hex);

    public bool IsChosen
    {
        get => _isChosen;
        set
        {
            if (_isChosen == value) return;
            _isChosen = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChosen)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Chooses one of the built-in icons, or clears the choice; for a group, its colour as well.
/// </summary>
/// <remarks>
/// The colours are the twelve a connection can wear, so a group and the connections in it can be
/// matched by eye. A connection's colour is chosen beside its icon in the editor, so its picker
/// shows icons only.
/// </remarks>
internal sealed class IconPickerModel
{
    private string? _color;

    internal IconPickerModel(string? currentKey, string title, bool withColor = false, string? currentColor = null)
    {
        Title = title;
        ShowsColors = withColor;
        Colors = withColor
            ? [.. ConnectionEditorModel.AccentChoices.Select(static swatch => new ColorChoiceModel(swatch.Hex))]
            : [];
        Color = currentColor;
        ChooseColorCommand = new RelayCommand(hex => Color = hex as string);
        NoColorCommand = new RelayCommand(_ => Color = null);
        Choices =
        [
            .. ConnectionIconCatalog.Choices.Select(static choice => new IconChoiceModel(
                choice.Key, Themes.IconCatalog.Resolve(choice.Glyph) ?? LucideIconKind.Cloud))
        ];
        Selected = Choices.FirstOrDefault(choice =>
            string.Equals(choice.Key, currentKey, StringComparison.OrdinalIgnoreCase));
        UseCommand = new RelayCommand(_ => Finish(new IconChoice(true, Selected?.Key, _color)), _ => true);
        UseDefaultCommand = new RelayCommand(_ => Finish(new IconChoice(true, null, _color)));
        CancelCommand = new RelayCommand(_ => Finish(IconChoice.Dismissed));
    }

    public string Title { get; }

    /// <summary>Whether the colour row is shown: for a group, not for a connection.</summary>
    public bool ShowsColors { get; }

    public IReadOnlyList<ColorChoiceModel> Colors { get; }

    /// <summary>The colour chosen, as #RRGGBB, or null for none; the swatch for it is ringed.</summary>
    public string? Color
    {
        get => _color;
        private set
        {
            _color = Colors.FirstOrDefault(choice =>
                string.Equals(choice.Hex, value, StringComparison.OrdinalIgnoreCase))?.Hex;
            foreach (var choice in Colors) choice.IsChosen = string.Equals(choice.Hex, _color, StringComparison.Ordinal);
        }
    }

    public ICommand ChooseColorCommand { get; }

    public ICommand NoColorCommand { get; }

    public static string ColorLabel => Ui.Connections.IconPickerColor;

    public static string NoColorLabel => Ui.Connections.IconPickerNoColor;

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
    internal static async Task<IconChoice> AskAsync(
        Window? owner,
        string? currentKey,
        string title,
        bool withColor = false,
        string? currentColor = null)
    {
        if (owner is null) return IconChoice.Dismissed;
        var model = new IconPickerModel(currentKey, title, withColor, currentColor);
        await new IconPickerWindow { DataContext = model }.ShowDialog(owner).ConfigureAwait(true);
        return model.Result;
    }
}
