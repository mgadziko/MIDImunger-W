namespace MIDImunger.Core;

public sealed class MidiMonitor
{
    private readonly Dictionary<string, MidiByteStreamParser> _parsers = [];

    public event EventHandler<MidiMessage>? MessageReceived;

    public void ProcessPacket(ReadOnlySpan<byte> bytes, string? sourceName = null, string sourceId = "default")
    {
        if (!_parsers.TryGetValue(sourceId, out var parser))
        {
            parser = new MidiByteStreamParser();
            _parsers.Add(sourceId, parser);
        }

        foreach (var message in parser.Consume(bytes, sourceName))
        {
            MessageReceived?.Invoke(this, message);
        }
    }
}
