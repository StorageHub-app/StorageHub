namespace StorageHub.Ipc;

public sealed class IpcProtocolNegotiationException : IOException
{
    public IpcProtocolNegotiationException(string message)
        : base(message)
    {
    }

    public IpcProtocolNegotiationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
