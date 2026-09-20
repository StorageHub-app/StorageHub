using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// StorageHub's own message box: the buttons a request produces, what each one answers, and what a
/// dismissal answers.
/// </summary>
/// <remarks>
/// Worth testing at this level because the dialog builds its buttons from the request rather than
/// declaring them, and because the answer a dismissed dialog gives is the one thing every call site
/// relies on without ever writing it down.
///
/// The tables that would read better as theory data are inside the methods instead: the vocabulary
/// is internal to the shell, and xUnit needs a public signature, so a theory parameter would mean
/// publishing four enums to satisfy a test runner.
/// </remarks>
public class DialogWindowTests
{
    private static DialogRequest Request(DialogButtons buttons, DialogChoice? fallback = null) => new()
    {
        Title = "StorageHub",
        Message = "Discard the queued transfers?",
        Buttons = buttons,
        Default = fallback
    };

    [AvaloniaFact]
    public void EachButtonSetProducesItsButtons()
    {
        (DialogButtons Buttons, int Expected)[] sets =
        [
            (DialogButtons.Ok, 1),
            (DialogButtons.OkCancel, 2),
            (DialogButtons.YesNo, 2),
            (DialogButtons.YesNoCancel, 3)
        ];

        foreach (var (buttons, expected) in sets)
        {
            var window = DialogWindow.For(Request(buttons));
            window.Show();

            Assert.Equal(expected, Actions(window).Children.Count);
        }
    }

    [AvaloniaFact]
    public void TheButtonsReadInTheLanguageTheShellIsSpeaking()
    {
        var window = DialogWindow.For(Request(DialogButtons.YesNoCancel));
        window.Show();

        Assert.Equal(
            [Ui.Dialogs.ButtonYes, Ui.Dialogs.ButtonNo, Ui.Dialogs.ButtonCancel],
            Actions(window).Children.OfType<Button>().Select(button => button.Content as string));
    }

    [AvaloniaFact]
    public void ClickingAButtonAnswersWithItsChoiceAndCloses()
    {
        var window = DialogWindow.For(Request(DialogButtons.YesNoCancel));
        window.Show();

        // The click handler is what answers, so raise the event the way input would.
        Button(window, Ui.Dialogs.ButtonNo)
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(
                global::Avalonia.Controls.Button.ClickEvent));

        Assert.Equal(DialogChoice.No, window.Result);
        Assert.False(window.IsVisible);
    }

    /// <summary>
    /// A dismissed dialog answers the safe choice, not nothing.
    /// </summary>
    /// <remarks>
    /// This is the behaviour that lets 36 call sites treat "cancel" as "do nothing" without any of
    /// them handling the window being closed from the title bar.
    /// </remarks>
    [AvaloniaFact]
    public void AnUntouchedDialogAlreadyHoldsTheSafeAnswer()
    {
        (DialogButtons Buttons, DialogChoice Expected)[] sets =
        [
            (DialogButtons.Ok, DialogChoice.Ok),
            (DialogButtons.OkCancel, DialogChoice.Cancel),
            (DialogButtons.YesNo, DialogChoice.No),
            (DialogButtons.YesNoCancel, DialogChoice.Cancel)
        ];

        foreach (var (buttons, expected) in sets)
        {
            var window = DialogWindow.For(Request(buttons));
            window.Show();

            Assert.Equal(expected, window.Result);
        }
    }

    [AvaloniaFact]
    public void EscapeDismissesEvenAnAcknowledgement()
    {
        // MessageBoxButtons.OK has no Cancel for IsCancel to attach to, and a dialog that ignores
        // Escape is one people report as frozen.
        var window = DialogWindow.For(Request(DialogButtons.Ok));
        window.Show();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(DialogChoice.Ok, window.Result);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void ADestructiveConfirmationCanRefuseToLetEnterAgreeToIt()
    {
        var window = DialogWindow.For(Request(DialogButtons.YesNo, DialogChoice.No));
        window.Show();

        // Enter presses Yes, but nothing was pressed, so the standing answer is still No.
        Assert.True(Button(window, Ui.Dialogs.ButtonYes).IsDefault);
        Assert.Equal(DialogChoice.No, window.Result);
    }

    [AvaloniaFact]
    public void TheDetailIsHiddenWhenThereIsNone()
    {
        var plain = DialogWindow.For(Request(DialogButtons.Ok));
        plain.Show();
        Assert.False(plain.GetControl<TextBlock>("PART_Detail").IsVisible);

        var detailed = DialogWindow.For(Request(DialogButtons.Ok) with { Detail = @"C:\work\a.shw" });
        detailed.Show();
        Assert.True(detailed.GetControl<TextBlock>("PART_Detail").IsVisible);
    }

    [AvaloniaFact]
    public void SeverityPicksTheIcon()
    {
        (DialogSeverity Severity, LucideIconKind Expected)[] sets =
        [
            (DialogSeverity.Information, LucideIconKind.Info),
            (DialogSeverity.Question, LucideIconKind.CircleQuestionMark),
            (DialogSeverity.Warning, LucideIconKind.TriangleAlert),
            (DialogSeverity.Error, LucideIconKind.CircleX)
        ];

        foreach (var (severity, expected) in sets)
        {
            var window = DialogWindow.For(Request(DialogButtons.Ok) with { Severity = severity });
            window.Show();

            Assert.Equal(expected, window.GetControl<LucideIcon>("PART_Icon").Kind);
        }
    }

    /// <summary>
    /// Renders each severity, in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Every visual defect found in this port so far -- a light default, a leaked ToString, a stock
    /// Fluent accent, a menu bar templated as a submenu, clipped headers, an empty menu -- was
    /// found by looking at a capture. None of them failed an assertion. Set STORAGEHUB_SHOT_DIR to
    /// keep the files.
    /// </remarks>
    [AvaloniaFact]
    public void TheDialogCanBePhotographedInBothAppearances()
    {
        (DialogSeverity Severity, DialogButtons Buttons, string? Detail)[] cases =
        [
            (DialogSeverity.Question, DialogButtons.YesNoCancel, null),
            (DialogSeverity.Warning, DialogButtons.OkCancel, "3 transfers are still running."),
            (DialogSeverity.Error, DialogButtons.Ok,
                "The agent refused the connection: the named pipe was not found."),
            (DialogSeverity.Information, DialogButtons.Ok, null)
        ];

        try
        {
            foreach (var variant in (ThemeVariant[])[ThemeVariant.Dark, ThemeVariant.Light])
            {
                global::Avalonia.Application.Current!.RequestedThemeVariant = variant;
                foreach (var (severity, buttons, detail) in cases)
                {
                    Photograph(severity, buttons, detail, variant);
                }
            }
        }
        finally
        {
            global::Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private static void Photograph(
        DialogSeverity severity,
        DialogButtons buttons,
        string? detail,
        ThemeVariant variant)
    {
        var window = DialogWindow.For(Request(buttons) with { Severity = severity, Detail = detail });
        window.Show();
        window.Measure(new Size(460, 400));
        window.Arrange(new Rect(0, 0, 460, window.DesiredSize.Height));

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var appearance = variant == ThemeVariant.Light ? "light" : "dark";
        using var stream = File.Create(Path.Combine(
            directory,
            $"dialog-{appearance}-{severity.ToString().ToLowerInvariant()}.png"));
        frame!.Save(stream, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static StackPanel Actions(DialogWindow window) =>
        window.GetControl<StackPanel>("PART_Actions");

    private static Button Button(DialogWindow window, string label) =>
        Actions(window).Children.OfType<Button>().Single(button => (string?)button.Content == label);
}
