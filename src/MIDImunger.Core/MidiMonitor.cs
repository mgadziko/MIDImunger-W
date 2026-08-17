namespace MIDImunger.Core;

public sealed class MidiMonitor
{
    private readonly MidiByteStreamParser _parser = new();

    public event EventHandler<MidiMessage>? MessageReceived;

    public void ProcessPacket(ReadOnlySpan<byte> bytes, string? sourceName = null)
    {
        foreach (var message in _parser.Consume(bytes, sourceName))
        {
            MessageReceived?.Invoke(this, message);
        }
    }
}
