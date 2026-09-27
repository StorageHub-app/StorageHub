using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The main toolbar's row: its buttons left to right, and what does not fit left for the chevron
/// at its end to offer, as 1.x's ToolStrip did.
/// </summary>
/// <remarks>
/// <para>
/// 1.x's toolbar was a ToolStrip, whose default layout moves the buttons that do not fit into a
/// drop-down behind a "»" at the right. Avalonia has no toolbar, and a StackPanel draws past its
/// edge, so once Settings could put labels on the buttons, the last of them were cut off in a
/// window of ordinary width and could not be reached from the toolbar at all.
/// </para>
/// <para>
/// The chevron is not a child: the window lays it over the right end of the row, and the row
/// leaves it <see cref="OverflowReserve"/> only once something has to go, so a toolbar that fits
/// uses the whole width. What does not fit is laid out past the edge and disabled, so Tab cannot
/// land on a button nobody can see, and a divider that would end the row goes with what follows it.
/// </para>
/// </remarks>
internal sealed class ToolbarOverflowPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ToolbarOverflowPanel, double>(nameof(Spacing));

    public static readonly StyledProperty<double> OverflowReserveProperty =
        AvaloniaProperty.Register<ToolbarOverflowPanel, double>(nameof(OverflowReserve));

    /// <summary>Raised when the buttons behind the chevron are no longer the same ones.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> OverflowChangedEvent =
        RoutedEvent.Register<ToolbarOverflowPanel, RoutedEventArgs>(nameof(OverflowChanged), RoutingStrategies.Bubble);

    private Control[] _overflow = [];

    static ToolbarOverflowPanel()
    {
        AffectsMeasure<ToolbarOverflowPanel>(SpacingProperty, OverflowReserveProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ToolbarOverflowPanel>(true);
    }

    /// <summary>The room between two items.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The width the chevron takes at the right end, once there is one.</summary>
    public double OverflowReserve
    {
        get => GetValue(OverflowReserveProperty);
        set => SetValue(OverflowReserveProperty, value);
    }

    /// <summary>The children that did not fit, in the toolbar's order.</summary>
    public IReadOnlyList<Control> Overflow => _overflow;

    public event EventHandler<RoutedEventArgs>? OverflowChanged
    {
        add => AddHandler(OverflowChangedEvent, value);
        remove => RemoveHandler(OverflowChangedEvent, value);
    }

    /// <summary>
    /// Every child at the width it wants, whether it will fit or not, so the arrange knows what the
    /// whole row would take, and the toolbar is as tall with a button behind the chevron as without.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        var count = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (!child.IsVisible) continue;
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
            count++;
        }

        width += Spacing * Math.Max(0, count - 1);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(static child => child.IsVisible).ToArray();
        var whole = children.Sum(static child => child.DesiredSize.Width) +
                    Spacing * Math.Max(0, children.Length - 1);

        // A row that only just fits can be given a hair less than its sum, from rounding, and still
        // fits.
        var fits = children.Length;
        if (whole - finalSize.Width > 0.01)
        {
            // As many as fit before the chevron, which keeps the spacing any other item would. A
            // divider does not end the row: it goes with what follows it.
            var room = finalSize.Width - OverflowReserve - Spacing;
            var used = 0.0;
            fits = 0;
            while (fits < children.Length && used + children[fits].DesiredSize.Width <= room)
            {
                used += children[fits].DesiredSize.Width + Spacing;
                fits++;
            }

            while (fits > 0 && children[fits - 1].DataContext is ToolbarSeparator) fits--;
        }

        // What does not fit is laid out past the right edge at its own size, where the clip hides
        // it, rather than squeezed to nothing, which would leave its icon drawn over the first
        // button.
        var x = 0.0;
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            child.IsEnabled = index < fits;
            if (index == fits) x = finalSize.Width;
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }

        var overflow = children[fits..];
        if (!overflow.SequenceEqual(_overflow))
        {
            _overflow = overflow;
            RaiseEvent(new RoutedEventArgs(OverflowChangedEvent, this));
        }

        return finalSize;
    }
}
