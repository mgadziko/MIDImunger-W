namespace MIDImunger.Core;

public enum MidiMessageKind
{
    NoteOff,
    NoteOn,
    PolyphonicAftertouch,
    ControlChange,
    ProgramChange,
    ChannelAftertouch,
    PitchBend,
    SystemCommon,
    SystemRealtime,
    SystemExclusive
}

public sealed record MidiMessage(
    MidiMessageKind Kind,
    byte[] Bytes,
    int? Channel,
    string Description,
    string? SourceName = null)
{
    public bool IsChannelMessage => Channel.HasValue;
}
