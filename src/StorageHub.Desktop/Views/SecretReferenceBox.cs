using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// A read-only box for a secret reference: a badge inside it saying where the secret came from,
/// then a name, rather than "shs_" and 43 random characters.
/// </summary>
/// <remarks>
/// <para>
/// One control for every place a reference is shown (the connection editor's secret fields on
/// every provider, and Settings' default key), so a Key Store entry reads the same wherever it
/// is used: [Key Store] deploy-key, [Vault] Stored for this connection, or, for an entry deleted
/// from under a field, [Missing from Key Store] in the warning colour under the name it had.
/// </para>
/// <para>
/// Still a TextBox, drawn by the TextBox theme, so it lines up with the boxes around it, takes
/// focus and can be selected from. The reference is not lost: it is in the tooltip, and the
/// context menu copies it. The badge and the name are what a screen reader reads, after the label.
/// </para>
/// </remarks>
internal sealed class SecretReferenceBox : TextBox
{
    public static readonly StyledProperty<SecretReferenceDisplay?> DisplayProperty =
        AvaloniaProperty.Register<SecretReferenceBox, SecretReferenceDisplay?>(nameof(Display));

    /// <summary>The field's label, which the accessible name starts with.</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SecretReferenceBox, string?>(nameof(Label));

    private readonly MenuItem _copyReference;

    public SecretReferenceBox()
    {
        IsReadOnly = true;
        _copyReference = new MenuItem { Header = Ui.KeyStore.CopyReference };
        _copyReference.Click += async (_, _) =>
        {
            if (Display is { Reference.Length: > 0 } display && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(display.Reference).ConfigureAwait(true);
            }
        };
        var menu = new MenuFlyout();
        menu.Items.Add(_copyReference);
        ContextFlyout = menu;
        Update();
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public SecretReferenceDisplay? Display
    {
        get => GetValue(DisplayProperty);
        set => SetValue(DisplayProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DisplayProperty || change.Property == LabelProperty) Update();
    }

    private void Update()
    {
        var display = Display ?? SecretReferenceDisplay.Empty;
        Text = display.Name;
        InnerLeftContent = display.HasBadge ? Badge(display) : null;
        ToolTip.SetTip(this, display.ToolTip.Length == 0 ? null : display.ToolTip);
        _copyReference.IsEnabled = display.Reference.Length > 0;

        var label = Label ?? string.Empty;
        AutomationProperties.SetName(
            this,
            !display.HasBadge ? label
            : label.Length == 0 ? display.AccessibleText
            : Ui.Format(Ui.KeyStore.SourceAccessibleFormat, label, display.AccessibleText));
    }

    /// <summary>The pill: the pane chips' shape, in the accent for the Key Store and amber when missing.</summary>
    internal static Border Badge(SecretReferenceDisplay display)
    {
        var badge = new Border
        {
            Child = new TextBlock { Text = display.Badge, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Classes.Add("pill");
        badge.Classes.Add("secret-source");
        badge.Classes.Set("primary", display.IsKeyStore);
        badge.Classes.Set("warning", display.IsMissing);
        return badge;
    }
}
