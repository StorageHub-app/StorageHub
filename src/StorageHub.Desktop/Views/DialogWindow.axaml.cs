using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>
/// One message, its detail, and the buttons that answer it.
/// </summary>
/// <remarks>
/// The buttons are built rather than declared because the set is a property of the request, and
/// four templates that must stay identical is how two dialogs about the same thing end up looking
/// different. The severity picks the icon and its colour from the catalog and the tokens, so a
/// warning here is the same yellow as a warning in the status bar.
/// </remarks>
public partial class DialogWindow : Window
{
    private DialogChoice _result = DialogChoice.Cancel;

    public DialogWindow() => AvaloniaXamlLoader.Load(this);

    internal static DialogWindow For(DialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var window = new DialogWindow();
        window.Apply(request);
        return window;
    }

    /// <summary>What was chosen, once the window has closed.</summary>
    internal DialogChoice Result => _result;

    private void Apply(DialogRequest request)
    {
        var fallback = request.Default ?? DialogDefaults.For(request.Buttons);
        _result = fallback;

        Title = request.Title;
        this.GetControl<TextBlock>("PART_Message").Text = request.Message;

        var detail = this.GetControl<TextBlock>("PART_Detail");
        detail.Text = request.Detail;
        detail.IsVisible = !string.IsNullOrWhiteSpace(request.Detail);

        var check = this.GetControl<CheckBox>("PART_CheckBox");
        check.Content = request.CheckBoxLabel;
        check.IsVisible = !string.IsNullOrWhiteSpace(request.CheckBoxLabel);
        _checkBoxAnswered = request.CheckBoxAnswered;

        var icon = this.GetControl<LucideIcon>("PART_Icon");
        icon.Kind = KindFor(request.Severity);
        icon.Foreground = BrushFor(request.Severity);

        var actions = this.GetControl<StackPanel>("PART_Actions");
        var enter = EnterFor(request);
        foreach (var choice in DialogDefaults.Choices(request.Buttons))
        {
            var affirmative = choice == PrimaryFor(request.Buttons);
            var named = affirmative && !string.IsNullOrWhiteSpace(request.Accept);
            var button = new Button
            {
                Content = named ? request.Accept : LabelFor(choice),
                IsDefault = choice == enter,
                IsCancel = choice == fallback && choice is DialogChoice.Cancel or DialogChoice.No
            };
            button.Classes.Add("dialog");

            // Captured rather than read from the sender, so a caller that restyles the button
            // cannot change what it answers.
            button.Click += (_, _) => Close(choice);

            // The button Enter presses is the accented one, as Windows draws its default button,
            // and it starts focused so the ring and the accent agree on where Enter goes.
            if (choice == enter)
            {
                button.Classes.Add("primary");
                _defaultButton = button;
            }

            actions.Children.Add(button);
        }
    }

    private Button? _defaultButton;

    /// <summary>Focuses the default button once the window can take focus.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _defaultButton?.Focus();
    }

    /// <summary>
    /// The button Enter presses: the one the caller named as the default, or else the affirmative
    /// one.
    /// </summary>
    /// <remarks>
    /// A caller names a default to keep a stray Enter off the destructive answer, which is what
    /// MB_DEFBUTTON2 did for 1.4's message boxes; "trust this host key" with No as its default must
    /// not be agreed to by Enter.
    /// </remarks>
    private static DialogChoice EnterFor(DialogRequest request) =>
        request.Default is { } named && DialogDefaults.Choices(request.Buttons).Contains(named)
            ? named
            : PrimaryFor(request.Buttons);

    /// <summary>
    /// Escape answers with the safe choice rather than nothing.
    /// </summary>
    /// <remarks>
    /// IsCancel covers the button sets that have a Cancel or a No. An acknowledgement has neither,
    /// and a dialog that cannot be dismissed with Escape is one people report as frozen.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            Close(_result);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private Action<bool>? _checkBoxAnswered;

    private void Close(DialogChoice choice)
    {
        _result = choice;
        if (choice is not (DialogChoice.Cancel or DialogChoice.No) && _checkBoxAnswered is { } answered)
        {
            answered(this.GetControl<CheckBox>("PART_CheckBox").IsChecked == true);
        }

        Close();
    }

    /// <summary>The affirmative answer of a button set: the one an Accept label renames.</summary>
    private static DialogChoice PrimaryFor(DialogButtons buttons) => buttons switch
    {
        DialogButtons.YesNo or DialogButtons.YesNoCancel => DialogChoice.Yes,
        _ => DialogChoice.Ok
    };

    internal static LucideIconKind KindFor(DialogSeverity severity) => severity switch
    {
        DialogSeverity.Question => LucideIconKind.CircleQuestionMark,
        DialogSeverity.Warning => LucideIconKind.TriangleAlert,
        DialogSeverity.Error => LucideIconKind.CircleX,
        _ => LucideIconKind.Info
    };

    private static IBrush BrushFor(DialogSeverity severity) => Themes.DesignTokens.Get<IBrush>(
        severity switch
        {
            DialogSeverity.Warning => "WarningBrush",
            DialogSeverity.Error => "DangerBrush",
            DialogSeverity.Question => "PrimaryBrush",
            _ => "PrimaryBrush"
        });

    internal static string LabelFor(DialogChoice choice) => choice switch
    {
        DialogChoice.Ok => Ui.Dialogs.ButtonOk,
        DialogChoice.Cancel => Ui.Dialogs.ButtonCancel,
        DialogChoice.Yes => Ui.Dialogs.ButtonYes,
        DialogChoice.No => Ui.Dialogs.ButtonNo,
        _ => Ui.Dialogs.ButtonOk
    };
}
