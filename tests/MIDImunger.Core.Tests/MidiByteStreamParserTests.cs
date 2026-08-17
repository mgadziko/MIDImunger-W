using MIDImunger.Core;

namespace MIDImunger.Core.Tests;

public sealed class MidiByteStreamParserTests
{
    [Fact]
    public void Consume_ParsesRunningStatusAcrossPackets()
    {
        var parser = new MidiByteStreamParser();

        var first = parser.Consume([0x90, 60]);
        var second = parser.Consume([100, 61, 110]);

        Assert.Empty(first);
        Assert.Collection(
            second,
            firstMessage =>
            {
                Assert.Equal(MidiMessageKind.NoteOn, firstMessage.Kind);
                Assert.Equal(1, firstMessage.Channel);
                Assert.Equal(new byte[] { 0x90, 60, 100 }, firstMessage.Bytes);
            },
            secondMessage => Assert.Equal(new byte[] { 0x90, 61, 110 }, secondMessage.Bytes));
    }

    [Fact]
    public void Consume_EmitsRealtimeWithoutInterruptingMessage()
    {
        var parser = new MidiByteStreamParser();

        var messages = parser.Consume([0x90, 60, 0xF8, 100]);

        Assert.Collection(
            messages,
            clock => Assert.Equal(MidiMessageKind.SystemRealtime, clock.Kind),
            note => Assert.Equal(new byte[] { 0x90, 60, 100 }, note.Bytes));
    }

    [Fact]
    public void Consume_CollectsFragmentedSysEx()
    {
        var parser = new MidiByteStreamParser();

        Assert.Empty(parser.Consume([0xF0, 0x43, 0x10]));
        var messages = parser.Consume([0x7F, 0xF7]);

        var message = Assert.Single(messages);
        Assert.Equal(MidiMessageKind.SystemExclusive, message.Kind);
        Assert.Equal(new byte[] { 0xF0, 0x43, 0x10, 0x7F, 0xF7 }, message.Bytes);
    }

    [Fact]
    public void Monitor_MaintainsSeparateParserStateForEachInput()
    {
        var monitor = new MidiMonitor();
        var messages = new List<MidiMessage>();
        monitor.MessageReceived += (_, message) => messages.Add(message);

        monitor.ProcessPacket([0x90, 60], sourceName: "Input A", sourceId: "a");
        monitor.ProcessPacket([0x90, 70, 100], sourceName: "Input B", sourceId: "b");
        monitor.ProcessPacket([100], sourceName: "Input A", sourceId: "a");

        Assert.Collection(
            messages,
            fromB => Assert.Equal(new byte[] { 0x90, 70, 100 }, fromB.Bytes),
            fromA => Assert.Equal(new byte[] { 0x90, 60, 100 }, fromA.Bytes));
    }

    [Fact]
    public void Dx100Commands_CreateExpectedPlayPressAndRelease()
    {
        Assert.Equal(
            new byte[] { 0xF0, 0x43, 0x10, 0x08, 27, 127, 0xF7 },
            Dx100Commands.CreatePlayPress());
        Assert.Equal(
            new byte[] { 0xF0, 0x43, 0x10, 0x08, 27, 0, 0xF7 },
            Dx100Commands.CreatePlayRelease());
    }
}
