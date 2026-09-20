namespace StorageHub.Desktop.Shell;

/// <summary>How serious a message is, which decides its icon and accent.</summary>
internal enum DialogSeverity
{
    Information,
    Question,
    Warning,
    Error
}

/// <summary>The buttons a dialog offers.</summary>
/// <remarks>
/// Four sets, because that is what the shell actually uses: 19 plain acknowledgements, 8 yes/no,
/// 5 ok/cancel and 4 yes/no/cancel. A more general "list of buttons" would be a seam nothing pulls
/// on, and would lose the one guarantee this enum gives -- that every dialog in the product looks
/// like the others.
/// </remarks>
internal enum DialogButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel
}

/// <summary>What the person chose.</summary>
/// <remarks>
/// <see cref="Cancel"/> is also what a dismissed dialog returns -- closed with Escape, with the
/// title bar, or by the window manager. A caller that treats "cancel" as "do nothing" therefore
/// handles dismissal for free, which is the behaviour every call site wants and none would
/// remember to write.
/// </remarks>
internal enum DialogChoice
{
    Ok,
    Cancel,
    Yes,
    No
}

/// <summary>One message to put in front of the person.</summary>
/// <remarks>
/// A record rather than eight overloads. MessageBox.Show has twelve of them and the shell used five
/// shapes across 36 call sites, which is how two dialogs about the same thing ended up with
/// different buttons.
/// </remarks>
internal sealed record DialogRequest
{
    /// <summary>The window's title. Short: it is read as a label, not a sentence.</summary>
    public required string Title { get; init; }

    /// <summary>The message itself, in one or two sentences.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// What a person would need to act on the message: a path, an error, what happens next.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Message"/> so it can be drawn quieter and can wrap independently.
    /// The shell used to concatenate the two with a blank line, which meant a long exception
    /// message pushed the actual question off the top of the dialog.
    /// </remarks>
    public string? Detail { get; init; }

    public DialogSeverity Severity { get; init; } = DialogSeverity.Information;

    public DialogButtons Buttons { get; init; } = DialogButtons.Ok;

    /// <summary>
    /// The choice a dismissed dialog reports, and the button that starts focused.
    /// </summary>
    /// <remarks>
    /// Null means the safe one for the button set: Ok for <see cref="DialogButtons.Ok"/>, Cancel
    /// where there is one, No otherwise. Worth overriding for a confirmation whose destructive
    /// answer must never be the one a stray Enter picks.
    /// </remarks>
    public DialogChoice? Default { get; init; }
}

/// <summary>
/// One question that wants a name back.
/// </summary>
/// <remarks>
/// Separate from <see cref="DialogRequest"/> rather than a field on it, because the answer is a
/// different shape: a prompt returns text or nothing, and a message returns a choice. Folding them
/// together would give every call site a nullable string it has to ignore.
/// </remarks>
internal sealed record DialogPromptRequest
{
    /// <summary>The window's title: "New folder", "Rename item".</summary>
    public required string Title { get; init; }

    /// <summary>What the box is for, beside it: "Folder name", "New name".</summary>
    public required string Label { get; init; }

    /// <summary>What the box starts with, selected so typing replaces it.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The accept button: "Create", "Rename".</summary>
    public required string Accept { get; init; }

    /// <summary>
    /// Says whether a value is acceptable, returning the reason it is not.
    /// </summary>
    /// <remarks>
    /// Checked as it is typed, so the accept button is dim with the reason showing rather than
    /// enabled onto a refusal. Null means anything non-empty will do.
    /// </remarks>
    public Func<string, string?>? Validate { get; init; }
}

/// <summary>
/// Puts a message in front of the person and, when it asks something, reports the answer.
/// </summary>
/// <remarks>
/// This exists because MessageBox.Show is a static call into a Windows API that also needs an
/// owner handle, which is why 36 call sites across the shell each had to know what a window is.
/// A view model takes this instead and can be tested by handing it a recorder.
///
/// Deliberately not here yet: a "show this view model as a modal" seam. The custom dialogs that
/// would use it have not been ported, so it would be a shape guessed rather than observed. It goes
/// in with the first one.
/// </remarks>
internal interface IDialogService
{
    /// <summary>Shows a message and returns once it has been acknowledged.</summary>
    Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default);

    /// <summary>Asks, and reports what was chosen.</summary>
    Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default);

    /// <summary>Asks for a name, and reports it -- or nothing, when the dialog was dismissed.</summary>
    Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="DialogRequest.Default"/> means when it is not given.</summary>
internal static class DialogDefaults
{
    internal static DialogChoice For(DialogButtons buttons) => buttons switch
    {
        DialogButtons.Ok => DialogChoice.Ok,
        DialogButtons.OkCancel or DialogButtons.YesNoCancel => DialogChoice.Cancel,
        DialogButtons.YesNo => DialogChoice.No,
        _ => DialogChoice.Cancel
    };

    /// <summary>The choices a button set can produce, in the order they are shown.</summary>
    internal static IReadOnlyList<DialogChoice> Choices(DialogButtons buttons) => buttons switch
    {
        DialogButtons.Ok => [DialogChoice.Ok],
        DialogButtons.OkCancel => [DialogChoice.Ok, DialogChoice.Cancel],
        DialogButtons.YesNo => [DialogChoice.Yes, DialogChoice.No],
        DialogButtons.YesNoCancel => [DialogChoice.Yes, DialogChoice.No, DialogChoice.Cancel],
        _ => [DialogChoice.Cancel]
    };
}
