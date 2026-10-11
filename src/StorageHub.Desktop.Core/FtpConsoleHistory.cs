using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// The FTP console's earlier commands, walked with Up and Down as a shell's history is.
/// </summary>
/// <remarks>
/// <para>
/// Up from the line being typed keeps it as a draft, so Down past the newest entry brings it back
/// rather than losing it. A command repeated straight after itself is kept once. A command that
/// carries a password (PASS, ACCT) is never kept, sent or not.
/// </para>
/// <para>
/// In memory and per window only: the history goes when the console closes, so nothing typed into
/// it is written anywhere.
/// </para>
/// </remarks>
public sealed class FtpConsoleHistory
{
    /// <summary>How many commands are kept; the oldest go first.</summary>
    public const int Capacity = 100;

    private readonly List<string> _entries = [];
    private int _position;
    private string _draft = string.Empty;

    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Adds a command as the newest, and goes back to the line being typed.</summary>
    public void Add(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var trimmed = command.Trim();
        if (trimmed.Length > 0 && !FtpConsoleCommandPolicy.CarriesSecret(trimmed) &&
            (_entries.Count == 0 || !string.Equals(_entries[^1], trimmed, StringComparison.Ordinal)))
        {
            _entries.Add(trimmed);
            if (_entries.Count > Capacity) _entries.RemoveAt(0);
        }

        _position = _entries.Count;
        _draft = string.Empty;
    }

    /// <summary>The command before the one shown, or null at the oldest.</summary>
    /// <param name="current">What the command line holds now, kept as the draft when leaving it.</param>
    public string? Previous(string current)
    {
        if (_position == 0) return null;
        if (_position == _entries.Count) _draft = current ?? string.Empty;
        _position--;
        return _entries[_position];
    }

    /// <summary>The command after the one shown, the draft past the newest, or null already there.</summary>
    public string? Next()
    {
        if (_position >= _entries.Count) return null;
        _position++;
        return _position == _entries.Count ? _draft : _entries[_position];
    }
}
