using System.Globalization;

namespace MIDImunger.Core;

public sealed class MidiByteStreamParser
{
    private byte? _runningStatus;
    private byte? _currentStatus;
    private readonly List<byte> _currentData = [];
    private int _expectedDataLength;
    private List<byte>? _sysExBytes;

    public IReadOnlyList<MidiMessage> Consume(ReadOnlySpan<byte> bytes, string? sourceName = null)
    {
        var messages = new List<MidiMessage>();

        foreach (var value in bytes)
        {
            if (value >= 0xF8)
            {
                messages.Add(CreateSystemMessage(MidiMessageKind.SystemRealtime, [value], RealtimeName(value), sourceName));
                continue;
            }

            if (_sysExBytes is not null)
            {
                _sysExBytes.Add(value);
                if (value == 0xF7)
                {
                    var completed = _sysExBytes.ToArray();
                    messages.Add(CreateSystemMessage(
                        MidiMessageKind.SystemExclusive,
                        completed,
                        $"SysEx manufacturer {completed.ElementAtOrDefault(1):X2}, {completed.Length} bytes",
                        sourceName));
                    _sysExBytes = null;
                    ResetCurrentMessage();
                }

                continue;
            }

            if ((value & 0x80) != 0)
            {
                StartStatus(value, messages, sourceName);
                continue;
            }

            if (_currentStatus is null && _runningStatus is not null)
            {
                _currentStatus = _runningStatus;
                _expectedDataLength = ExpectedDataLength(_runningStatus.Value);
            }

            if (_currentStatus is null)
            {
                continue;
            }

            _currentData.Add(value);
            if (_currentData.Count == _expectedDataLength)
            {
                var complete = new byte[_currentData.Count + 1];
                complete[0] = _currentStatus.Value;
                _currentData.CopyTo(complete, 1);
                messages.Add(Describe(complete, sourceName));

                _currentData.Clear();
                if (_currentStatus < 0xF0)
                {
                    _currentStatus = _runningStatus;
                    _expectedDataLength = ExpectedDataLength(_runningStatus ?? 0);
                }
                else
                {
                    ResetCurrentMessage();
                }
            }
        }

        return messages;
    }

    private void StartStatus(byte status, ICollection<MidiMessage> messages, string? sourceName)
    {
        ResetCurrentMessage();
        if (status == 0xF0)
        {
            _runningStatus = null;
            _sysExBytes = [status];
            return;
        }

        _currentStatus = status;
        _expectedDataLength = ExpectedDataLength(status);
        _runningStatus = status < 0xF0 ? status : null;
        if (_expectedDataLength == 0)
        {
            messages.Add(CreateSystemMessage(MidiMessageKind.SystemCommon, [status], SystemCommonName(status), sourceName));
            ResetCurrentMessage();
        }
    }

    private void ResetCurrentMessage()
    {
        _currentStatus = null;
        _currentData.Clear();
        _expectedDataLength = 0;
    }

    private static MidiMessage Describe(byte[] bytes, string? sourceName)
    {
        var status = bytes[0];
        if (status >= 0xF0)
        {
            return CreateSystemMessage(MidiMessageKind.SystemCommon, bytes, SystemCommonDescription(bytes), sourceName);
        }

        var channel = (status & 0x0F) + 1;
        var data1 = bytes[1];
        var data2 = bytes.Length > 2 ? bytes[2] : (byte)0;
        return (status & 0xF0) switch
        {
            0x80 => new(MidiMessageKind.NoteOff, bytes, channel, $"Note Off: note {data1}, velocity {data2}", sourceName),
            0x90 => new(data2 == 0 ? MidiMessageKind.NoteOff : MidiMessageKind.NoteOn, bytes, channel, $"{(data2 == 0 ? "Note Off" : "Note On")}: note {data1}, velocity {data2}", sourceName),
            0xA0 => new(MidiMessageKind.PolyphonicAftertouch, bytes, channel, $"Polyphonic Aftertouch: note {data1}, pressure {data2}", sourceName),
            0xB0 => new(MidiMessageKind.ControlChange, bytes, channel, $"Control Change: {data1}, value {data2}", sourceName),
            0xC0 => new(MidiMessageKind.ProgramChange, bytes, channel, $"Program Change: program {data1 + 1}", sourceName),
            0xD0 => new(MidiMessageKind.ChannelAftertouch, bytes, channel, $"Channel Aftertouch: pressure {data1}", sourceName),
            0xE0 => new(MidiMessageKind.PitchBend, bytes, channel, $"Pitch Bend: value {(data2 << 7) | data1}", sourceName),
            _ => new(MidiMessageKind.SystemCommon, bytes, channel, "Unknown MIDI message", sourceName)
        };
    }

    private static MidiMessage CreateSystemMessage(MidiMessageKind kind, byte[] bytes, string description, string? sourceName) =>
        new(kind, bytes, null, description, sourceName);

    private static int ExpectedDataLength(byte status) => status switch
    {
        >= 0x80 and <= 0xBF => 2,
        >= 0xC0 and <= 0xDF => 1,
        >= 0xE0 and <= 0xEF => 2,
        0xF1 or 0xF3 => 1,
        0xF2 => 2,
        _ => 0
    };

    private static string SystemCommonDescription(byte[] bytes) => bytes[0] switch
    {
        0xF1 => $"MIDI Time Code quarter frame {bytes[1]}",
        0xF2 => $"Song Position Pointer {bytes[1] | (bytes[2] << 7)}",
        0xF3 => $"Song Select {bytes[1]}",
        _ => SystemCommonName(bytes[0])
    };

    private static string SystemCommonName(byte status) => status switch
    {
        0xF6 => "Tune Request",
        0xF7 => "End of SysEx",
        0xF4 or 0xF5 => "Undefined system common",
        _ => $"System message {status.ToString("X2", CultureInfo.InvariantCulture)}"
    };

    private static string RealtimeName(byte status) => status switch
    {
        0xF8 => "Timing Clock",
        0xFA => "Start",
        0xFB => "Continue",
        0xFC => "Stop",
        0xFE => "Active Sensing",
        0xFF => "System Reset",
        _ => $"Realtime {status:X2}"
    };
}
