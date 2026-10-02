using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// A connection's card in the connections panel, which the keyboard and a screen reader can reach.
/// </summary>
/// <remarks>
/// The card was a plain Border: a click selected it and a double click opened it, and nothing else
/// could. It took no focus, so Tab went past every connection, and UI Automation saw only its
/// text, with nothing to select or open. Drawn exactly as before (it styles as a Border), it now
/// takes focus, selects itself when tabbed to, opens on Enter, and offers Invoke (open) and
/// SelectionItem (select) to automation.
/// </remarks>
public sealed class ConnectionCard : Border
{
    public ConnectionCard()
    {
        Focusable = true;
    }

    protected override Type StyleKeyOverride => typeof(Border);

    private ConnectionRowModel? Row => DataContext as ConnectionRowModel;

    private ConnectionsSidebar? Sidebar =>
        this.FindAncestorOfType<ConnectionsPanelView>()?.DataContext as ConnectionsSidebar;

    internal void SelectRow()
    {
        if (Row is { } row) Sidebar?.Select(row);
    }

    internal void OpenRow()
    {
        if (Row is not { } row || row.Id == Guid.Empty || Sidebar?.OpenConnection is not { } open) return;
        Sidebar.Select(row);
        open(row.Id);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) SelectRow();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter)
        {
            OpenRow();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Space)
        {
            SelectRow();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CardPeer(this);

    private sealed class CardPeer(ConnectionCard owner) : ControlAutomationPeer(owner), IInvokeProvider, ISelectionItemProvider
    {
        private ConnectionCard Card => (ConnectionCard)Owner;

        public bool IsSelected => Card.Row?.IsSelected == true;

        public ISelectionProvider? SelectionContainer => null;

        public void Invoke() => Card.OpenRow();

        public void Select() => Card.SelectRow();

        public void AddToSelection() => Card.SelectRow();

        public void RemoveFromSelection()
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string? GetNameCore() => base.GetNameCore() ?? Card.Row?.Name;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
