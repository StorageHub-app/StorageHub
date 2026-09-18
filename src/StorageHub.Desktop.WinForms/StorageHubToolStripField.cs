using System.ComponentModel;

namespace StorageHub.Desktop;

/// <summary>
/// The app's own text field, in a <see cref="ToolStrip"/>: the pane's address bar and its filter.
///
/// <see cref="ToolStripTextBox"/> hosts a stock text box and renders it with the strip's renderer,
/// which is why the address bar stayed a square native box while every other input in the app grew
/// a border of its own. A <see cref="ToolStripControlHost"/> puts the real control in the strip
/// instead, and the strip stops having an opinion about how it looks.
/// </summary>
internal sealed class StorageHubToolStripField : ToolStripControlHost
{
    internal StorageHubToolStripField()
        : base(new StorageHubTextField())
    {
        AutoSize = false;
        // The strip paints its own background behind the item, and the field's rounded corners
        // have to show it rather than a rectangle of the wrong colour.
        BackColor = Color.Transparent;
        Field.Dense = true;
    }

    /// <summary>The field itself, for the few callers that need more than text.</summary>
    internal StorageHubTextField Field => (StorageHubTextField)Control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ReadOnly
    {
        get => Field.ReadOnly;
        set => Field.ReadOnly = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string? PlaceholderText
    {
        get => Field.PlaceholderText;
        set => Field.PlaceholderText = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal UiGlyph? Glyph
    {
        get => Field.Glyph;
        set => Field.Glyph = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowClearButton
    {
        get => Field.ShowClearButton;
        set => Field.ShowClearButton = value;
    }

    internal void SelectAll() => Field.SelectAll();
}
