namespace StorageHub.Desktop;

/// <summary>
/// A settings page: a column of sections that scrolls vertically and never sideways.
/// </summary>
/// <remarks>
/// A page that is not the selected one has never been laid out at the size it will be shown at --
/// it sits at the default 200x100 while its cards are already several hundred pixels wide. That is
/// a horizontal overflow as far as <see cref="ScrollableControl"/> is concerned, so Windows puts
/// <c>WS_HSCROLL</c> on the window. Selecting the page docks it to the full width, where the
/// content fits again, but nothing re-evaluates the decision: the managed
/// <see cref="ScrollProperties.Visible"/> reads false while the real window still carries the
/// style, and the bar stays on screen taking a strip of the page with it.
///
/// It only showed on a scaled display, which is why it survived review on a 100% one: at 96 DPI
/// the pages happened to fit even while small.
///
/// Turning <see cref="ScrollableControl.AutoScroll"/> off and on again is what clears it. That
/// drops both scrollbars and the display rectangle, then works the range out from the size the
/// page actually has now.
/// </remarks>
internal sealed class SettingsPagePanel : FlowLayoutPanel
{
    private bool _resetting;

    internal SettingsPagePanel()
    {
        AutoScroll = true;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
    }

    /// <summary>
    /// Recomputes the scrollbars from the page's current size. Called once the page has been
    /// docked and its content fitted, which is the first moment the answer can be right.
    /// </summary>
    internal void ResetScrollState()
    {
        if (_resetting || !IsHandleCreated)
        {
            return;
        }

        _resetting = true;
        try
        {
            AutoScroll = false;
            AutoScroll = true;
            if (HorizontalScroll.Value != 0)
            {
                HorizontalScroll.Value = 0;
            }
        }
        finally
        {
            _resetting = false;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            ResetScrollState();
        }
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (Visible)
        {
            ResetScrollState();
        }
    }
}
