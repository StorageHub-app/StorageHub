using System.ComponentModel;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Which side the connections panel is on, how wide it is, and whether it is showing: what 1.x's
/// MainForm kept in its split container and saved in the settings file.
/// </summary>
/// <remarks>
/// <para>
/// View > Connections Panel (Ctrl+B) shows and hides it, View > Move Connections Panel docks it to
/// the other side, and dragging the splitter sets its width. Each is saved as it happens, as 1.x
/// saved it, so a shell that does not close cleanly still reopens the way it was left.
/// </para>
/// <para>
/// The width is in device pixels, the unit 1.x's splitter saved, so a settings file carried over
/// from 1.x opens with the panel the size it was there. The window converts it as it lays out.
/// </para>
/// </remarks>
internal sealed class ConnectionsPanelLayout : INotifyPropertyChanged
{
    internal ConnectionsPanelLayout() => Restore(DesktopUpdatePreferences.Defaults);

    public bool IsVisible
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            Check.IsChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public ConnectionsPanelSide Side
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Side)));
        }
    }

    /// <summary>
    /// The width in device pixels. Raised when settings are restored, not when a drag sets it: the
    /// splitter already put the panel there, and laying it out again from a rounded number would
    /// nudge it.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>The View menu's check mark and the toolbar button's pressed look, both on while it shows.</summary>
    internal CommandCheck Check { get; } = new();

    /// <summary>
    /// Where each change is written. Null in a preview, which has no settings file.
    /// </summary>
    internal Action<Func<DesktopUpdatePreferences, DesktopUpdatePreferences>>? Persist { get; set; }

    /// <summary>Raised when the panel comes back, which is when 1.x read its connections again.</summary>
    internal event EventHandler? Shown;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Takes the side, width and visibility from saved settings, as 1.x did when its window was
    /// shown. A width outside 1.x's limits is its default, as the settings repair would make it.
    /// </summary>
    internal void Restore(DesktopUpdatePreferences saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Width = saved.ConnectionsPanelWidth is >= DesktopUpdatePreferences.MinimumConnectionsPanelWidth
            and <= DesktopUpdatePreferences.MaximumConnectionsPanelWidth
            ? saved.ConnectionsPanelWidth
            : DesktopUpdatePreferences.DefaultConnectionsPanelWidth;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
        Side = Enum.IsDefined(saved.ConnectionsPanelSide) ? saved.ConnectionsPanelSide : ConnectionsPanelSide.Left;
        IsVisible = saved.ConnectionsPanelVisible;
    }

    /// <summary>Ctrl+B: hides a showing panel and shows a hidden one.</summary>
    internal void Toggle() => SetVisible(!IsVisible);

    internal void SetVisible(bool visible)
    {
        if (IsVisible == visible) return;
        IsVisible = visible;
        Persist?.Invoke(current => current with { ConnectionsPanelVisible = visible });
        if (visible) Shown?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Docks the panel to the other side, the same width: 1.x carried the width across rather than
    /// the splitter's position, so the panel did not jump to the mirror of wherever that was.
    /// </summary>
    internal void MoveToOtherSide()
    {
        var side = Side == ConnectionsPanelSide.Left ? ConnectionsPanelSide.Right : ConnectionsPanelSide.Left;
        Side = side;
        Persist?.Invoke(current => current with { ConnectionsPanelSide = side });
    }

    /// <summary>
    /// Takes the side Settings saved, which can differ from this one once its Apply has run.
    /// </summary>
    internal void FollowSettings(DesktopUpdatePreferences saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (Enum.IsDefined(saved.ConnectionsPanelSide)) Side = saved.ConnectionsPanelSide;
    }

    /// <summary>
    /// The splitter was let go. Kept inside 1.x's limits, which the settings repair enforces, so a
    /// width is never written that the next start would throw away.
    /// </summary>
    internal void Resized(int width)
    {
        if (!IsVisible) return;
        width = WithinLimits(width);
        if (width == Width) return;
        Width = width;
        Persist?.Invoke(current => current with { ConnectionsPanelWidth = width });
    }

    /// <summary>
    /// The window moved to a screen with another scaling, which leaves the panel the size it was
    /// on screen and another number of pixels. Taken but not saved: nobody resized it, and a
    /// window moved between two screens at every start would otherwise walk the saved width
    /// further each time.
    /// </summary>
    internal void Rescaled(int width)
    {
        if (IsVisible) Width = WithinLimits(width);
    }

    private static int WithinLimits(int width) => Math.Clamp(
        width,
        DesktopUpdatePreferences.MinimumConnectionsPanelWidth,
        DesktopUpdatePreferences.MaximumConnectionsPanelWidth);
}
