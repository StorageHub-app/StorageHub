namespace StorageHub.Desktop;

/// <summary>
/// Field chrome around a control that has none of its own: a list, a multi-line box, anything that
/// still has to be a stock control but should not look like one.
///
/// A <see cref="ListBox"/> can be stripped of its border but cannot be given a rounded one, and it
/// cannot inset its own contents, so on its own it reads as a rectangle cut out of the page. Here
/// the border, the corners and the padding belong to the host and the list simply fills it.
/// </summary>
internal sealed class StorageHubFieldHost : Control
{
    private readonly Control _content;

    internal StorageHubFieldHost(Control content)
    {
        ArgumentNullException.ThrowIfNull(content);
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        _content = content;
        _content.Dock = DockStyle.Fill;
        Padding = new Padding(
            LogicalToDeviceUnits(6),
            LogicalToDeviceUnits(5),
            LogicalToDeviceUnits(4),
            LogicalToDeviceUnits(5));
        Controls.Add(_content);
    }

    /// <summary>The control inside, for callers that keep a handle on it.</summary>
    internal Control Content => _content;

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            e.Graphics.FillRectangle(backdrop, ClientRectangle);
        }

        StorageHubFieldChrome.PaintField(e.Graphics, this, _content.Focused);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Invalidate();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Invalidate();
    }
}
