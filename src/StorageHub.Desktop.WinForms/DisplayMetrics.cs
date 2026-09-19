namespace StorageHub.Desktop;

/// <summary>
/// The <see cref="Control.LogicalToDeviceUnits(int)"/> overloads WinForms does not ship.
///
/// The framework converts an <see cref="int"/> and a <see cref="Size"/> and stops there, so the
/// two metrics layout code writes most often -- a <see cref="Padding"/> and a
/// <see cref="Point"/> -- had no conversion to reach for. That is most of why the rule in
/// docs/architecture.md was only ever applied to the custom controls: a padding could not be
/// written as a logical unit without spelling out four separate calls, so it was written as
/// literal pixels instead, and a literal pixel is the same size at every display scaling while
/// the font inside it is not.
///
/// These are extension methods rather than members of a base control, because the layout code
/// they serve lives on <see cref="Form"/>, <see cref="Panel"/>, <see cref="ToolStrip"/> and
/// several framework types that cannot share an ancestor.
/// </summary>
internal static class DisplayMetrics
{
    /// <summary>A padding written in logical units, at the size it is drawn.</summary>
    internal static Padding LogicalToDeviceUnits(this Control control, Padding logical)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new Padding(
            control.LogicalToDeviceUnits(logical.Left),
            control.LogicalToDeviceUnits(logical.Top),
            control.LogicalToDeviceUnits(logical.Right),
            control.LogicalToDeviceUnits(logical.Bottom));
    }

    /// <summary>The four edges of a padding, written in logical units.</summary>
    internal static Padding LogicalToDeviceUnits(this Control control, int left, int top, int right, int bottom)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new Padding(
            control.LogicalToDeviceUnits(left),
            control.LogicalToDeviceUnits(top),
            control.LogicalToDeviceUnits(right),
            control.LogicalToDeviceUnits(bottom));
    }

    /// <summary>
    /// The height of a box that holds one line of text: the line itself, plus logical breathing
    /// room around it.
    ///
    /// Preferred over a logical height for anything containing text, because a logical height
    /// only tracks the display scaling, while the text inside also tracks the font. A box stated
    /// in pixels clips as soon as the two disagree -- a longer translation, a different system
    /// font, or the Windows text-size setting, none of which change the dpi.
    /// </summary>
    internal static int TextBoxHeight(this Control control, int logicalPadding)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.Font.Height + control.LogicalToDeviceUnits(logicalPadding);
    }

    /// <summary>
    /// The height of a box holding one line set in <paramref name="font"/> rather than the
    /// control's own -- a heading or a section title.
    /// </summary>
    internal static int TextBoxHeight(this Control control, Font font, int logicalPadding)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(font);
        return font.Height + control.LogicalToDeviceUnits(logicalPadding);
    }

    /// <summary>A point written in logical units, at the position it is drawn.</summary>
    internal static Point LogicalToDeviceUnits(this Control control, Point logical)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new Point(control.LogicalToDeviceUnits(logical.X), control.LogicalToDeviceUnits(logical.Y));
    }

    /// <summary>
    /// A window size written in logical units, converted and then held inside the display it will
    /// open on.
    ///
    /// The clamp is the price of scaling window sizes at all. A minimum of 1120x720 logical is
    /// comfortable at 100% and is 2240x1440 at 200% -- larger than the work area of the laptop
    /// panel that asked for 200% in the first place. Unclamped, the window simply hangs off the
    /// screen with its lower edge, and a minimum size cannot be dragged back.
    /// </summary>
    internal static Size LogicalWindowSize(this Form form, Size logical)
    {
        ArgumentNullException.ThrowIfNull(form);
        var scaled = form.LogicalToDeviceUnits(logical);

        // Deliberately not Screen.FromControl: that reads Control.Handle, and these sizes are
        // assigned from a constructor, so it would force the window into existence before the
        // constructor had finished describing it. Until there is a handle, the display the pointer
        // is on is the one a dialog is about to be centred over.
        var workingArea = (form.IsHandleCreated
            ? Screen.FromHandle(form.Handle)
            : Screen.FromPoint(Cursor.Position)).WorkingArea.Size;
        return new Size(
            Math.Min(scaled.Width, workingArea.Width),
            Math.Min(scaled.Height, workingArea.Height));
    }
}
