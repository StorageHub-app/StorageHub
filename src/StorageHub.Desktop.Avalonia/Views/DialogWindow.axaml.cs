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

        var icon = this.GetControl<LucideIcon>("PART_Icon");
        icon.Kind = KindFor(request.Severity);
        icon.Foreground = BrushFor(request.Severity);

        var actions = this.GetControl<StackPanel>("PART_Actions");
        foreach (var choice in DialogDefaults.Choices(request.Buttons))
        {
            var button = new Button
            {
                Content = LabelFor(choice),
                MinWidth = 88,
                IsDefault = choice == PrimaryFor(request.Buttons),
                IsCancel = choice == fallback && choice is DialogChoice.Cancel or DialogChoice.No
            };

            // Captured rather than read from the sender, so a caller that restyles the button
            // cannot change what it answers.
            button.Click += (_, _) => Close(choice);
            if (choice == PrimaryFor(request.Buttons) && request.Severity == DialogSeverity.Question)
            {
                button.Classes.Add("primary");
            }

            actions.Children.Add(button);
        }
    }

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

    private void Close(DialogChoice choice)
    {
        _result = choice;
        Close();
    }

    /// <summary>The button Enter presses: the affirmative one, never the destructive one.</summary>
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
