# MIDImunger-W

Windows MIDI 1.0 monitoring and routing application, initially targeting .NET 8 and WPF.

## Current foundation

- A platform-neutral MIDI byte-stream parser handling channel messages, running status, system common, realtime traffic, and fragmented SysEx.
- A testable `IMidiBackend` boundary for Windows endpoint enumeration, input callbacks, and output delivery.
- A WPF monitor shell with per-channel state and a bounded MIDI event log.

## Next implementation milestone

Implement an `IMidiBackend` with Windows MIDI Services or WinMM, then bind discovered input/output endpoints to the monitor and routing controls.
