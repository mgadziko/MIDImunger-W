namespace MIDImunger.Core;

public static class Dx100Commands
{
    public static byte[] CreatePlayPress() => [0xF0, 0x43, 0x10, 0x08, 27, 127, 0xF7];

    public static byte[] CreatePlayRelease() => [0xF0, 0x43, 0x10, 0x08, 27, 0, 0xF7];
}
