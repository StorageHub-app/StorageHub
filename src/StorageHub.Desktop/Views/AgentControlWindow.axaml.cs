using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The background agent's state, and the buttons that change it.
/// </summary>
/// <remarks>
/// <para>
/// The state is polled rather than subscribed to, once a second while the window is open. That is
/// what the WinForms window did and what the monitor offers; the timer is stopped when the window
/// closes, and the model's cancellation stops any action still in flight with it.
/// </para>
/// <para>
/// The controller may be null, which is a real answer: a build with no packaged agent beside it
/// and no systemd unit can report the agent's state but not change it.
/// </para>
/// </remarks>
public partial class AgentControlWindow : Window
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };

    public AgentControlWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _refresh.Tick += (_, _) => (DataContext as AgentControlModel)?.Refresh();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is AgentControlModel model) model.Closed += (_, _) => Close();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Opened += (_, _) =>
        {
            (DataContext as AgentControlModel)?.Refresh();
            _refresh.Start();
        };

        Closed += (_, _) =>
        {
            _refresh.Stop();
            (DataContext as AgentControlModel)?.Dispose();
        };
    }

    /// <summary>The window over a live agent, reading whatever the shell last heard from it.</summary>
    internal static AgentControlWindow ForCurrentAgent(Func<AgentMonitorStatus?> readStatus)
    {
        ArgumentNullException.ThrowIfNull(readStatus);
        return new AgentControlWindow
        {
            DataContext = new AgentControlModel(readStatus, AgentLifecycleControllers.ForThisMachine())
        };
    }
}
