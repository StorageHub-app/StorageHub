using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>What a transcript line is, which decides how it is drawn.</summary>
internal enum FtpConsoleLineKind
{
    /// <summary>A command as it was sent, after the prompt.</summary>
    Sent,

    /// <summary>A reply line with a 1xx, 2xx or 3xx code.</summary>
    Reply,

    /// <summary>A reply line with a 4xx or 5xx code.</summary>
    ReplyFailed,

    /// <summary>Why a command was not sent, or could not be answered: the console's words, not the server's.</summary>
    Note
}

/// <summary>One line of the console's transcript.</summary>
internal sealed record FtpConsoleLine(string Text, FtpConsoleLineKind Kind)
{
    public bool IsSent => Kind == FtpConsoleLineKind.Sent;

    public bool IsFailed => Kind == FtpConsoleLineKind.ReplyFailed;

    public bool IsNote => Kind == FtpConsoleLineKind.Note;
}

/// <summary>
/// The FTP command console: a command line, its history, and a transcript of what was sent and
/// what the server said back.
/// </summary>
/// <remarks>
/// <para>
/// One command at a time. A command the policy refuses is answered here, in the transcript, and
/// never sent; the agent applies the same policy again. A command carrying a password is shown
/// with the password replaced and is not kept in the history, so nothing typed after PASS is on
/// screen a moment after Enter.
/// </para>
/// <para>
/// The transcript keeps the newest <see cref="MaximumLines"/> lines, which is many replies' worth
/// and bounds what a long session can hold.
/// </para>
/// </remarks>
internal sealed class FtpConsoleModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>How many transcript lines are kept; the oldest go first.</summary>
    internal const int MaximumLines = 2_000;

    private readonly Guid _connectionId;
    private readonly Func<FtpConsoleExecuteRequest, CancellationToken, Task<FtpConsoleExecuteResponse>> _execute;
    private readonly IClipboardService? _clipboard;
    private readonly FtpConsoleHistory _history = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RelayCommand _sendCommand;
    private readonly RelayCommand _clearCommand;
    private readonly RelayCommand _copyCommand;
    private string _command = string.Empty;
    private bool _isBusy;
    private StatusLine _status = StatusLine.Muted(Ui.Shell.FtpConsoleReady);

    internal FtpConsoleModel(
        Guid connectionId,
        string connectionName,
        Func<FtpConsoleExecuteRequest, CancellationToken, Task<FtpConsoleExecuteResponse>> execute,
        IClipboardService? clipboard)
    {
        if (connectionId == Guid.Empty) throw new ArgumentException("A saved connection is required.", nameof(connectionId));
        _connectionId = connectionId;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _clipboard = clipboard;
        ConnectionName = connectionName ?? string.Empty;
        _sendCommand = new RelayCommand(_ => _ = SendAsync(), _ => !_isBusy && !string.IsNullOrWhiteSpace(_command));
        _clearCommand = new RelayCommand(_ => Clear(), _ => Transcript.Count > 0);
        _copyCommand = new RelayCommand(_ => _ = CopyAsync(), _ => _clipboard is not null && Transcript.Count > 0);
    }

    public ObservableCollection<FtpConsoleLine> Transcript { get; } = [];

    internal string ConnectionName { get; }

    internal FtpConsoleHistory History => _history;

    public string Title => Ui.Format(Ui.Shell.FtpConsoleTitleFormat, ConnectionName);

    public static string Intro => Ui.Shell.FtpConsoleIntro;

    public static string CommandLabel => Ui.Shell.FtpConsoleCommandLabel;

    public static string CommandHint => Ui.Shell.FtpConsoleCommandHint;

    public static string TranscriptLabel => Ui.Shell.FtpConsoleTranscript;

    public static string SendLabel => Ui.Shell.FtpConsoleSend;

    public static string ClearLabel => Ui.Shell.FtpConsoleClear;

    public static string CopyLabel => Ui.Shell.FtpConsoleCopy;

    public static string CloseLabel => Ui.Dialogs.ButtonClose;

    /// <summary>The line being typed.</summary>
    public string Command
    {
        get => _command;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_command, value, StringComparison.Ordinal)) return;
            _command = value;
            Raise(nameof(Command));
            _sendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether a command is out and its reply not yet back.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            Raise(nameof(IsBusy));
            Raise(nameof(CanType));
            _sendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether the command line takes typing: not while a reply is awaited.</summary>
    public bool CanType => !_isBusy;

    public StatusLine Status
    {
        get => _status;
        private set
        {
            _status = value;
            Raise(nameof(Status));
        }
    }

    public ICommand SendCommand => _sendCommand;

    public ICommand ClearCommand => _clearCommand;

    public ICommand CopyCommand => _copyCommand;

    /// <summary>Raised after lines are added, so the view can scroll to the newest.</summary>
    internal event EventHandler? LinesAdded;

    /// <summary>Up: the earlier command, keeping what was being typed.</summary>
    internal void HistoryUp()
    {
        if (_history.Previous(_command) is { } previous) Command = previous;
    }

    /// <summary>Down: the later command, or back to what was being typed.</summary>
    internal void HistoryDown()
    {
        if (_history.Next() is { } next) Command = next;
    }

    /// <summary>Sends the command line, or says here why it is not sent.</summary>
    internal async Task SendAsync()
    {
        if (_isBusy) return;
        var command = _command.Trim();
        if (command.Length == 0) return;

        var secret = FtpConsoleCommandPolicy.CarriesSecret(command);
        Append(new FtpConsoleLine(
            "> " + (secret ? FtpConsoleCommandPolicy.VerbOf(command) + " ••••••" : command),
            FtpConsoleLineKind.Sent));
        _history.Add(command);
        Command = string.Empty;

        var refusal = FtpConsoleCommandPolicy.Check(command);
        if (refusal != FtpConsoleRefusal.None)
        {
            Append(new FtpConsoleLine(RefusalText(refusal), FtpConsoleLineKind.Note));
            Status = new StatusLine(Ui.Shell.FtpConsoleNotSent, MetricTone.Warning);
            return;
        }

        IsBusy = true;
        Status = StatusLine.Muted(Ui.Shell.FtpConsoleSending);
        try
        {
            var response = await _execute(
                new FtpConsoleExecuteRequest(FtpConsoleIpcContract.CurrentVersion, _connectionId, command),
                _lifetime.Token).ConfigureAwait(true);
            if (response.Reply is { } reply)
            {
                Show(reply);
            }
            else
            {
                var text = FailureText(response.Failure);
                Append(new FtpConsoleLine(text, FtpConsoleLineKind.Note));
                Status = new StatusLine(text, MetricTone.Danger);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error) when (error is TimeoutException or InvalidDataException)
        {
            // The client's own words, already localized: a timeout, or a reply it could not trust.
            Append(new FtpConsoleLine(error.Message, FtpConsoleLineKind.Note));
            Status = new StatusLine(error.Message, MetricTone.Danger);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or ObjectDisposedException or ArgumentException)
        {
            Append(new FtpConsoleLine(Ui.Shell.FtpConsoleAgentUnavailable, FtpConsoleLineKind.Note));
            Status = new StatusLine(Ui.Shell.FtpConsoleAgentUnavailable, MetricTone.Danger);
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested) IsBusy = false;
        }
    }

    private void Show(FtpConsoleReply reply)
    {
        var kind = reply.Succeeded ? FtpConsoleLineKind.Reply : FtpConsoleLineKind.ReplyFailed;
        var lines = reply.Text.Length == 0 ? [reply.Code] : reply.Text.Split('\n');
        foreach (var line in lines) Append(new FtpConsoleLine(line, kind), notify: false);
        if (reply.Truncated) Append(new FtpConsoleLine(Ui.Shell.FtpConsoleTruncated, FtpConsoleLineKind.Note), notify: false);
        LinesAdded?.Invoke(this, EventArgs.Empty);
        Status = new StatusLine(
            Ui.Format(Ui.Shell.FtpConsoleRepliedFormat, reply.Code),
            reply.Succeeded ? MetricTone.Success : MetricTone.Danger);
    }

    private void Clear()
    {
        Transcript.Clear();
        Status = StatusLine.Muted(Ui.Shell.FtpConsoleReady);
        _clearCommand.RaiseCanExecuteChanged();
        _copyCommand.RaiseCanExecuteChanged();
    }

    /// <summary>The transcript as plain text, a line per line, as it reads on screen.</summary>
    internal string TranscriptText()
    {
        var text = new StringBuilder();
        foreach (var line in Transcript) text.Append(line.Text).Append('\n');
        return text.ToString();
    }

    private async Task CopyAsync()
    {
        if (_clipboard is null || Transcript.Count == 0) return;
        await _clipboard.SetTextAsync(TranscriptText()).ConfigureAwait(true);
        Status = new StatusLine(Ui.Shell.FtpConsoleCopied, MetricTone.Success);
    }

    private void Append(FtpConsoleLine line, bool notify = true)
    {
        Transcript.Add(line);
        while (Transcript.Count > MaximumLines) Transcript.RemoveAt(0);
        if (Transcript.Count == 1)
        {
            _clearCommand.RaiseCanExecuteChanged();
            _copyCommand.RaiseCanExecuteChanged();
        }

        if (notify) LinesAdded?.Invoke(this, EventArgs.Empty);
    }

    internal static string RefusalText(FtpConsoleRefusal refusal) => refusal switch
    {
        FtpConsoleRefusal.DataConnection => Ui.Shell.FtpConsoleRefusedDataConnection,
        FtpConsoleRefusal.Login => Ui.Shell.FtpConsoleRefusedLogin,
        FtpConsoleRefusal.Security => Ui.Shell.FtpConsoleRefusedSecurity,
        FtpConsoleRefusal.SessionSetting => Ui.Shell.FtpConsoleRefusedSessionSetting,
        _ => Ui.Shell.FtpConsoleRefusedMalformed
    };

    /// <summary>
    /// The agent's failure in this language. Its codes are fixed and its own text is English, so
    /// the code chooses the words and the category is what is left for one this does not know.
    /// </summary>
    internal static string FailureText(StorageIpcFailure? failure) => failure?.Code switch
    {
        "ftp.console.refused.data_connection" => RefusalText(FtpConsoleRefusal.DataConnection),
        "ftp.console.refused.login" => RefusalText(FtpConsoleRefusal.Login),
        "ftp.console.refused.security" => RefusalText(FtpConsoleRefusal.Security),
        "ftp.console.refused.session_setting" => RefusalText(FtpConsoleRefusal.SessionSetting),
        "ftp.console.refused.malformed" => RefusalText(FtpConsoleRefusal.Malformed),
        _ => failure?.Category switch
        {
            StorageIpcFailureCategory.Timeout => Ui.Shell.FtpConsoleTimedOut,
            StorageIpcFailureCategory.Unavailable => Ui.Shell.FtpConsoleUnavailable,
            StorageIpcFailureCategory.Unauthorized => Ui.Shell.FtpConsoleUnauthorized,
            StorageIpcFailureCategory.Security => Ui.Shell.FtpConsoleSecurity,
            StorageIpcFailureCategory.NotFound => Ui.Shell.FtpConsoleNotFound,
            StorageIpcFailureCategory.Unsupported => Ui.Shell.FtpConsoleUnsupported,
            _ => Ui.Shell.FtpConsoleFailed
        }
    };

    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
