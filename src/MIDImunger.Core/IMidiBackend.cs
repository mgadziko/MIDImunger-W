namespace MIDImunger.Core;

public sealed record MidiEndpoint(string Id, string Name);

public interface IMidiBackend : IAsyncDisposable
{
    event EventHandler<MidiPacketReceivedEventArgs>? PacketReceived;
    event EventHandler<MidiBackendErrorEventArgs>? ErrorOccurred;

    Task<IReadOnlyList<MidiEndpoint>> GetInputEndpointsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MidiEndpoint>> GetOutputEndpointsAsync(CancellationToken cancellationToken = default);
    Task OpenInputAsync(MidiEndpoint endpoint, CancellationToken cancellationToken = default);
    Task CloseInputAsync(string endpointId, CancellationToken cancellationToken = default);
    Task SendAsync(MidiEndpoint endpoint, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
}

public sealed class MidiBackendErrorEventArgs(string operation, Exception exception) : EventArgs
{
    public string Operation { get; } = operation;
    public Exception Exception { get; } = exception;
}

public sealed class MidiPacketReceivedEventArgs(string sourceId, string sourceName, ReadOnlyMemory<byte> bytes) : EventArgs
{
    public string SourceId { get; } = sourceId;
    public string SourceName { get; } = sourceName;
    public ReadOnlyMemory<byte> Bytes { get; } = bytes;
}
