using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The connection picker in a pane's header.
/// </summary>
/// <remarks>
/// The grouping and keyboard rules are pinned in ConnectionPickerSessionTests. What is checked here
/// is the seam with the pane: that it offers what the pane can open, marks what the pane has open,
/// and that choosing a connection opens it.
/// </remarks>
public sealed class ConnectionPickerTests
{
    [AvaloniaFact]
    public async Task ThePickerOffersWhatThePaneCanOpen()
    {
        await using var pane = await Pane(
            Summary("Studio Assets", StorageConnectionProvider.S3),
            Summary("Build Box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client));

        var picker = pane.CreatePicker();
        var names = picker.Rows.Where(row => row.IsCard).Select(row => row.Name).ToArray();

        Assert.Equal(Ui.Pane.ThisPc, names[0]);
        Assert.Contains("Studio Assets", names);
        Assert.Contains("Build Box", names);
        Assert.Equal(pane.Connections.Count, names.Length);
    }

    /// <summary>Choosing a connection opens it in the pane, however it was reached.</summary>
    [AvaloniaFact]
    public async Task ChoosingAConnectionOpensIt()
    {
        var assets = Summary("Studio Assets", StorageConnectionProvider.S3);
        await using var pane = await Pane(assets);
        var picker = pane.CreatePicker();
        var chosen = false;
        picker.Chosen += (_, _) => chosen = true;

        picker.Search = "studio";
        Assert.True(picker.Choose());

        Assert.True(chosen);
        Assert.Equal(assets.ConnectionId, pane.Connection?.Id);
        Assert.Equal("Studio Assets", pane.Title);
    }

    /// <summary>What the pane has open is marked, and highlighted when the picker opens.</summary>
    [AvaloniaFact]
    public async Task WhatIsOpenIsMarked()
    {
        var assets = Summary("Studio Assets", StorageConnectionProvider.S3);
        await using var pane = await Pane(assets, Summary("Archive", StorageConnectionProvider.S3));
        pane.Choose(pane.Cards.Single(card => card.ConnectionId == assets.ConnectionId));

        var picker = pane.CreatePicker();
        var active = picker.Rows.Single(row => row.IsActive);

        Assert.Equal("Studio Assets", active.Name);
        Assert.Equal(picker.Rows.IndexOf(active), picker.Highlighted);
    }

    /// <summary>This computer can be chosen, and has no id to be found by.</summary>
    [AvaloniaFact]
    public async Task ThisComputerCanBeChosen()
    {
        await using var pane = await Pane(Summary("Studio Assets", StorageConnectionProvider.S3));

        pane.Choose(pane.Cards.Single(card => card.ConnectionId is null));

        Assert.Null(pane.Connection?.Id);
        Assert.Equal(Ui.Pane.ThisPc, pane.Title);
    }

    /// <summary>
    /// A heading cannot keep the highlight: the list is told where it really is.
    /// </summary>
    [AvaloniaFact]
    public async Task AHeadingCannotTakeTheHighlight()
    {
        await using var pane = await Pane(Summary("Studio Assets", StorageConnectionProvider.S3));
        var picker = pane.CreatePicker();
        var before = picker.Highlighted;

        picker.Highlighted = picker.Rows.ToList().FindIndex(row => row.IsHeader);

        Assert.Equal(before, picker.Highlighted);
        Assert.True(picker.Rows[picker.Highlighted].IsCard);
    }

    /// <summary>A search that matches nothing says so, and Enter does nothing.</summary>
    [AvaloniaFact]
    public async Task ASearchThatMatchesNothingSaysSo()
    {
        await using var pane = await Pane(Summary("Studio Assets", StorageConnectionProvider.S3));
        var picker = pane.CreatePicker();

        picker.Search = "no such connection";

        Assert.True(picker.IsEmpty);
        Assert.False(picker.HasRows);
        Assert.False(picker.Choose());
    }

    /// <summary>A shell and storage read differently at a glance, and this computer as local.</summary>
    [AvaloniaFact]
    public async Task EachRowSaysWhatItIs()
    {
        await using var pane = await Pane(
            Summary("Studio Assets", StorageConnectionProvider.S3),
            Summary("Build Box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client));
        var rows = pane.CreatePicker().Rows.Where(row => row.IsCard).ToDictionary(row => row.Name);

        Assert.Equal(Ui.Connections.PickerSystemLocalBadge, rows[Ui.Pane.ThisPc].Badge);
        Assert.Equal(Ui.Format(Ui.Connections.PickerStorageBadgeFormat, "S3"), rows["Studio Assets"].Badge);
        Assert.Equal(Ui.Format(Ui.Connections.PickerClientBadgeFormat, "SSH"), rows["Build Box"].Badge);
        Assert.True(rows["Build Box"].IsClient);
    }

    /// <summary>
    /// Photographs the picker in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Headings, cards with two badges, one marked open and one highlighted: the row that ran its
    /// badges into each other in 1.x is the one to look at. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThePickerCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        // The open connection is near the top, so the list opens unscrolled and every group is in
        // the frame; the highlight is moved one down so the open and highlighted rows differ.
        var invoices = Summary("Invoices", StorageConnectionProvider.Ftps, favorite: true);
        await using var pane = await Pane(
            Summary("Studio Assets", StorageConnectionProvider.S3, folder: "Media"),
            Summary("Render farm output with a long name", StorageConnectionProvider.Sftp, folder: "Media"),
            invoices,
            Summary("Build Box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client));
        pane.Choose(pane.Cards.Single(card => card.ConnectionId == invoices.ConnectionId));
        var picker = pane.CreatePicker();
        picker.Move(1);

        var window = new Window
        {
            Content = new Border
            {
                Classes = { "surface" },
                Padding = new Thickness(8),
                Child = new ConnectionPickerView { DataContext = picker, Height = 520 }
            }
        };
        window.Show();
        window.Measure(new Size(400, 560));
        window.Arrange(new Rect(0, 0, 400, 560));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"connection-picker-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static async Task<BrowserPaneModel> Pane(params ConnectionSummary[] connections)
    {
        var pane = new BrowserPaneModel(new WorkspaceFakes.FakeBrowsingAgent(connections));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        return pane;
    }

    private static ConnectionSummary Summary(
        string name,
        StorageConnectionProvider provider,
        ConnectionProfileType type = ConnectionProfileType.Storage,
        string? folder = null,
        bool favorite = false) =>
        new(
            Guid.NewGuid(),
            name,
            provider,
            FolderPath: folder,
            Tags: [],
            IsFavorite: favorite,
            IsEnabled: true,
            IconKey: null,
            AccentColor: null,
            Version: 1,
            type);
}
