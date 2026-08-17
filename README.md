# MIDImunger-W

Windows MIDI 1.0 monitoring and routing application, initially targeting .NET 8 and WPF.

## Current foundation

- A platform-neutral MIDI byte-stream parser handling channel messages, running status, system common, realtime traffic, and fragmented SysEx.
- A WinMM `IMidiBackend` for MIDI 1.0 endpoint discovery, input callbacks, short messages, and buffered SysEx.
- A WPF monitor with selectable inputs and MIDI Thru destinations, per-channel state, a bounded event log, All Notes Off, and the Yamaha DX100 `DX Play` recovery command.

## Running

Open `MIDImunger-W.sln` in Visual Studio 2022 or run:

```powershell
dotnet run --project src\MIDImunger.W
```

Use a virtual MIDI driver such as loopMIDI to test routing without hardware. This first backend uses the established WinMM MIDI 1.0 API; a Windows MIDI Services backend remains a future option for MIDI 2.0/UMP support.
