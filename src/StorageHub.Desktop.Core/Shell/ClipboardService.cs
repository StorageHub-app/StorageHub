namespace StorageHub.Desktop.Shell;

/// <summary>
/// The system clipboard.
/// </summary>
/// <remarks>
/// <para>
/// Asynchronous, and scoped to a window rather than static, because that is what it is underneath.
/// <c>System.Windows.Forms.Clipboard</c> is a static synchronous call that hides both facts; on
/// X11 the clipboard is owner-based, so what a paste returns depends on another process still
/// being alive to answer, and the answer arrives on a round trip.
/// </para>
/// <para>
/// That ownership has a consequence worth knowing at every call site: on X11, text this process
/// copies disappears when it exits, because there is no clipboard to leave it in -- only a promise
/// to serve it. A clipboard manager, if one is running, keeps it. Nothing in the shell can make
/// that guarantee itself.
/// </para>
/// </remarks>
internal interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>The clipboard's text, or null when it holds none.</summary>
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}
