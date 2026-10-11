using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Connections, FTP Command Console: what is sent, what is refused before it is sent, what the
/// transcript shows of each, the history, and the window in both themes.
/// </summary>
public sealed class FtpConsoleWindowTests
{
    private static readonly Guid Connection = Guid.NewGuid();

    [AvaloniaFact]
    public async Task ACommandIsSentAndItsReplyShownLineByLine()
    {
        var agent = new FakeAgent(static request => request.Command switch
        {
            "FEAT" => new FtpConsoleReply("211", true, "211-Features:\n MDTM\n SIZE\n211 End"),
            _ => new FtpConsoleReply("500", false, "500 Command not understood.")
        });
        using var model = Model(agent);

        model.Command = "  FEAT ";
        await model.SendAsync();

        Assert.Equal(["FEAT"], agent.Sent);
        Assert.Equal(Connection, agent.Requests[0].ConnectionId);
        Assert.Equal(
            ["> FEAT", "211-Features:", " MDTM", " SIZE", "211 End"],
            model.Transcript.Select(static line => line.Text));
        Assert.True(model.Transcript[0].IsSent);
        Assert.All(model.Transcript.Skip(1), static line => Assert.Equal(FtpConsoleLineKind.Reply, line.Kind));
        Assert.Equal(string.Empty, model.Command);
        Assert.True(model.Status.IsSuccess);
        Assert.Equal(Ui.Format(Ui.Shell.FtpConsoleRepliedFormat, "211"), model.Status.Text);

        model.Command = "XYZZY";
        await model.SendAsync();
        Assert.True(model.Transcript[^1].IsFailed);
        Assert.True(model.Status.IsDanger);
    }

    [AvaloniaTheory]
    [InlineData("LIST", FtpConsoleRefusal.DataConnection)]
    [InlineData("stor upload.bin", FtpConsoleRefusal.DataConnection)]
    [InlineData("QUIT", FtpConsoleRefusal.Login)]
    [InlineData("AUTH TLS", FtpConsoleRefusal.Security)]
    [InlineData("CWD /var/www", FtpConsoleRefusal.SessionSetting)]
    public async Task ARefusedCommandIsAnsweredHereAndNeverSent(string command, FtpConsoleRefusal refusal)
    {
        var agent = new FakeAgent(static _ => throw new InvalidOperationException("Nothing should be sent."));
        using var model = Model(agent);

        model.Command = command;
        await model.SendAsync();

        Assert.Empty(agent.Sent);
        Assert.Equal("> " + command, model.Transcript[0].Text);
        Assert.Equal(FtpConsoleModel.RefusalText(refusal), model.Transcript[1].Text);
        Assert.True(model.Transcript[1].IsNote);
        Assert.True(model.Status.IsWarning);
        Assert.Equal([command], model.History.Entries);
    }

    /// <summary>What follows PASS is gone from the screen the moment Enter is pressed, and from the history.</summary>
    [AvaloniaFact]
    public async Task APasswordIsNeitherShownNorKept()
    {
        var agent = new FakeAgent(static _ => throw new InvalidOperationException("Nothing should be sent."));
        using var model = Model(agent);

        model.Command = "PASS hunter2";
        await model.SendAsync();

        Assert.Empty(agent.Sent);
        Assert.Equal("> PASS ••••••", model.Transcript[0].Text);
        Assert.DoesNotContain("hunter2", model.TranscriptText(), StringComparison.Ordinal);
        Assert.Empty(model.History.Entries);
        Assert.Equal(string.Empty, model.Command);
    }

    [AvaloniaFact]
    public async Task AFailureIsSaidInTheConsolesWordsAndATruncatedReplySaysSo()
    {
        var agent = new FakeAgent(static request => request.Command == "STAT"
            ? new FtpConsoleReply("211", true, "211 lots", Truncated: true)
            : null);
        using var model = Model(agent);

        model.Command = "SYST";
        await model.SendAsync();
        Assert.Equal(Ui.Shell.FtpConsoleTimedOut, model.Transcript[^1].Text);
        Assert.True(model.Status.IsDanger);

        model.Command = "STAT";
        await model.SendAsync();
        Assert.Equal(Ui.Shell.FtpConsoleTruncated, model.Transcript[^1].Text);
    }

    [AvaloniaFact]
    public async Task AnAgentThatCannotBeReachedIsSaidSo()
    {
        using var model = new FtpConsoleModel(
            Connection,
            "Web Site",
            static (_, _) => Task.FromException<FtpConsoleExecuteResponse>(new IOException("pipe broken")),
            null);

        model.Command = "SYST";
        await model.SendAsync();

        Assert.Equal(Ui.Shell.FtpConsoleAgentUnavailable, model.Transcript[^1].Text);
        Assert.False(model.IsBusy);
    }

    [AvaloniaFact]
    public async Task CopyWritesTheTranscriptAndClearEmptiesIt()
    {
        var clipboard = new Clipboard();
        using var model = new FtpConsoleModel(
            Connection,
            "Web Site",
            new FakeAgent(static _ => new FtpConsoleReply("215", true, "215 UNIX Type: L8")).ExecuteAsync,
            clipboard);
        Assert.False(model.CopyCommand.CanExecute(null));

        model.Command = "SYST";
        await model.SendAsync();
        model.CopyCommand.Execute(null);

        Assert.Equal("> SYST\n215 UNIX Type: L8\n", clipboard.Text);
        Assert.True(model.ClearCommand.CanExecute(null));
        model.ClearCommand.Execute(null);
        Assert.Empty(model.Transcript);
    }

    /// <summary>Up and Down in the command line walk the history, as a shell's do.</summary>
    [AvaloniaFact]
    public async Task UpAndDownInTheCommandLineWalkTheHistory()
    {
        using var model = Model(new FakeAgent(static _ => new FtpConsoleReply("200", true, "200 OK")));
        var window = new FtpConsoleWindow { DataContext = model };
        window.Show();
        var box = window.FindControl<TextBox>("PART_Command")!;
        box.Focus();

        model.Command = "SYST";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Until(() => !model.IsBusy && model.Transcript.Count == 2);
        model.Command = "FEAT";
        await model.SendAsync();
        model.Command = "MDT";

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal("FEAT", box.Text);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal("SYST", box.Text);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal("MDT", box.Text);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheConsoleWindowCanBePhotographed(bool dark)
    {
        var agent = new FakeAgent(static request => request.Command switch
        {
            "SYST" => new FtpConsoleReply("215", true, "215 UNIX Type: L8"),
            "FEAT" => new FtpConsoleReply("211", true,
                "211-Features supported:\n EPRT\n EPSV\n MDTM\n MFMT\n MLST type*;size*;modify*;perm*;\n REST STREAM\n SIZE\n UTF8\n211 End FEAT."),
            "SITE CHMOD 640 private/notes.txt" => new FtpConsoleReply("200", true, "200 SITE CHMOD successful."),
            _ => new FtpConsoleReply("550", false, "550 /releases/old.zip: No such file or directory.")
        });
        using var model = Model(agent);
        foreach (var command in new[] { "SYST", "FEAT", "SITE CHMOD 640 private/notes.txt", "MDTM /releases/old.zip", "LIST" })
        {
            model.Command = command;
            await model.SendAsync();
        }

        model.Command = "STAT";
        Photograph(new FtpConsoleWindow { DataContext = model }, "ftp-console", dark);
    }

    private static FtpConsoleModel Model(FakeAgent agent) =>
        new(Connection, "Web Site (ftps.example.com)", agent.ExecuteAsync, null);

    private static async Task Until(Func<bool> done)
    {
        for (var attempt = 0; attempt < 200 && !done(); attempt++)
        {
            await Task.Delay(10);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.True(done());
    }

    private static void Photograph(Window window, string name, bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));
        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        window.Close();
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"{name}-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>The agent as the console sees it: a reply per command, or a timeout for a null.</summary>
    private sealed class FakeAgent(Func<FtpConsoleExecuteRequest, FtpConsoleReply?> answer)
    {
        internal List<FtpConsoleExecuteRequest> Requests { get; } = [];

        internal IEnumerable<string> Sent => Requests.Select(static request => request.Command);

        internal Task<FtpConsoleExecuteResponse> ExecuteAsync(FtpConsoleExecuteRequest request, CancellationToken cancellationToken)
        {
            Assert.True(request.HasValidBounds);
            var reply = answer(request);
            Requests.Add(request);
            return Task.FromResult(new FtpConsoleExecuteResponse(
                request.ContractVersion,
                request.ConnectionId,
                reply,
                reply is null
                    ? new StorageIpcFailure("ftp.console.timeout", StorageIpcFailureCategory.Timeout, "Too slow.", true)
                    : null));
        }
    }

    private sealed class Clipboard : IClipboardService
    {
        internal string? Text { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Text);
    }
}
