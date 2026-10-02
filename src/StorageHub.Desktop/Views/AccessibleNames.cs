using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Gives a button, tab, row, menu or tree entry the name it shows, for screen readers and UI
/// Automation.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia names such a control after its content only when the content is a string. Almost none
/// of ours are: a toolbar button is an icon beside a label, a row is the record it shows, a tab is
/// the model behind it, and a menu's heading is a template. Each was read out as its type,
/// "Avalonia.Controls.StackPanel", "BrowserListItem { Name = C:\, ... }" or
/// "StorageHub.Desktop.Views.TransferQueueTab", and the menu bar's headings had no name at all.
/// </para>
/// <para>
/// So, once for the whole application (<see cref="Install"/>), a control that has no name of its own
/// and no string to be named after takes the text it draws: the label of a button, every cell of
/// a row, the first line of a tab, menu entry or tree entry. An icon-only button takes its tooltip.
/// The name follows the text as it changes, so "Active (3)" is read as 3 and a recycled row is
/// read as the row it now shows. A name set in markup is left alone.
/// </para>
/// </remarks>
internal static class AccessibleNames
{
    private static readonly AttachedProperty<Watch?> WatchProperty =
        AvaloniaProperty.RegisterAttached<Control, Watch?>("AccessibleNameWatch", typeof(AccessibleNames));

    private static bool _installed;

    /// <summary>Names every such control as it is shown.</summary>
    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        Hook<Button>();
        Hook<TabItem>();
        Hook<ListBoxItem>();
        Hook<MenuItem>();
        Hook<TreeViewItem>();
    }

    private static void Hook<T>()
        where T : Control
    {
        Control.LoadedEvent.AddClassHandler<T>(static (control, _) => Attach(control));
        Control.UnloadedEvent.AddClassHandler<T>(static (control, _) => Detach(control));
    }

    private static void Attach(Control control)
    {
        Detach(control);
        if (HasOwnName(control)) return;

        // A number box's arrows draw an icon and nothing else, and have no tooltip to fall back on.
        if (SpinnerName(control) is { } spin)
        {
            AutomationProperties.SetName(control, spin);
            return;
        }

        var watch = new Watch(control, Texts(control));
        control.SetValue(WatchProperty, watch);
        watch.Update();
    }

    private static void Detach(Control control)
    {
        if (control.GetValue(WatchProperty) is not { } watch) return;
        watch.Dispose();
        control.ClearValue(WatchProperty);
        control.ClearValue(AutomationProperties.NameProperty);
    }

    /// <summary>Increase or Decrease, for the arrows of a ButtonSpinner (a NumericUpDown's).</summary>
    private static string? SpinnerName(Control control) =>
        control is RepeatButton { TemplatedParent: ButtonSpinner } spin
            ? spin.Name switch
            {
                "PART_IncreaseButton" => Localization.Ui.Shell.SpinIncrease,
                "PART_DecreaseButton" => Localization.Ui.Shell.SpinDecrease,
                _ => null
            }
            : null;

    /// <summary>
    /// A name somebody set, or content Avalonia already names the control after. The one given
    /// here is taken back when the control is hidden, so it is never mistaken for one of these.
    /// </summary>
    private static bool HasOwnName(Control control)
    {
        if (control.IsSet(AutomationProperties.NameProperty)) return true;
        return control switch
        {
            HeaderedSelectingItemsControl { Header: string header } => header.Length > 0,
            HeaderedItemsControl { Header: string header } => header.Length > 0,
            ContentControl { Content: string content } => content.Length > 0,
            _ => false
        };
    }

    /// <summary>
    /// The text the control draws, leaving out anything inside another control that is named on
    /// its own: a tree entry's children, a button inside a row, a menu entry's shortcut.
    /// </summary>
    private static List<TextBlock> Texts(Control control)
    {
        var found = new List<TextBlock>();
        Collect(control, control, found);
        return found;
    }

    private static void Collect(Visual parent, Control owner, List<TextBlock> found)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Button or ListBoxItem or MenuItem or TreeViewItem or TextBox) continue;
            if (child is TextBlock text)
            {
                if (text.Name?.StartsWith("PART_InputGesture", StringComparison.Ordinal) != true) found.Add(text);
                continue;
            }

            Collect(child, owner, found);
        }
    }

    private sealed class Watch : IDisposable
    {
        private readonly Control _control;
        private readonly List<TextBlock> _texts;

        internal Watch(Control control, List<TextBlock> texts)
        {
            _control = control;
            _texts = texts;
            foreach (var text in _texts) text.PropertyChanged += OnChanged;
            _control.PropertyChanged += OnChanged;
        }

        internal void Update()
        {
            var lines = _texts
                .Where(static text => text.IsVisible)
                .Select(static text => text.Text?.Trim())
                .Where(static line => !string.IsNullOrEmpty(line))
                .ToList();
            var name = lines.Count == 0
                ? ToolTip.GetTip(_control) as string
                : _control is Button or ListBoxItem ? string.Join(", ", lines) : lines[0];
            if (string.IsNullOrWhiteSpace(name))
            {
                _control.ClearValue(AutomationProperties.NameProperty);
                return;
            }

            AutomationProperties.SetName(_control, name);
        }

        public void Dispose()
        {
            foreach (var text in _texts) text.PropertyChanged -= OnChanged;
            _control.PropertyChanged -= OnChanged;
        }

        private void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBlock.TextProperty || e.Property == Visual.IsVisibleProperty ||
                (ReferenceEquals(sender, _control) && e.Property == ToolTip.TipProperty))
            {
                Update();
            }
        }
    }
}
