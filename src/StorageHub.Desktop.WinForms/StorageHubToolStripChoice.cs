using System.ComponentModel;

namespace StorageHub.Desktop;

/// <summary>
/// The app's own drop-down, in a <see cref="ToolStrip"/>: the transfer queue's reconcile action.
///
/// The same reason as <see cref="StorageHubToolStripField"/> -- a <see cref="ToolStripComboBox"/>
/// hosts a stock combo and hands its look to the strip's renderer, leaving one square box in a
/// toolbar of rounded ones.
/// </summary>
internal sealed class StorageHubToolStripChoice : ToolStripControlHost
{
    internal StorageHubToolStripChoice()
        : base(new StorageHubChoiceField())
    {
        AutoSize = false;
        BackColor = Color.Transparent;
        Field.Dense = true;
    }

    /// <summary>The drop-down itself: its items, its selection and how an entry is captioned.</summary>
    internal StorageHubChoiceField Field => (StorageHubChoiceField)Control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal object? SelectedItem
    {
        get => Field.SelectedItem;
        set => Field.SelectedItem = value;
    }

    /// <summary>Raised when the selection changes, so a toolbar can act on it.</summary>
    internal event EventHandler? SelectedIndexChanged
    {
        add => Field.SelectedIndexChanged += value;
        remove => Field.SelectedIndexChanged -= value;
    }
}
