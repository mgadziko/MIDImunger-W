using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MIDImunger.Core;

namespace MIDImunger.W;

public sealed class WinMmMidiBackend : IMidiBackend
{
    private const uint CallbackFunction = 0x00030000;
    private const uint MimData = 0x03C3;
    private const uint MimLongData = 0x03C4;
    private const uint MmSysErrNoError = 0;
    private const uint MidiErrStillPlaying = 65;
    private const int SysExBufferSize = 16 * 1024;
    private const int SysExBufferCount = 4;

    private readonly WinMmMidiInCallback _inputCallback;
    private readonly ConcurrentDictionary<string, OpenInput> _openInputs = [];
    private readonly Dictionary<uint, IntPtr> _openOutputs = [];
    private bool _disposed;

    public WinMmMidiBackend()
    {
        _inputCallback = HandleInput;
    }

    public event EventHandler<MidiPacketReceivedEventArgs>? PacketReceived;
    public event EventHandler<MidiBackendErrorEventArgs>? ErrorOccurred;

    // Each WinMM enumeration/open call gets its own dedicated thread with a hard timeout.
    // Thread-pool threads cannot be forcibly aborted in .NET 8; using a dedicated Thread
    // means a hung native call leaks at most one thread rather than blocking the pool.
    private static readonly TimeSpan NativeCallTimeout = TimeSpan.FromSeconds(5);

    private static Task<T> RunOnDedicatedThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        })
        { IsBackground = true };
        thread.Start();
        return tcs.Task;
    }

    private static async Task<T> WithNativeTimeout<T>(Func<T> work, T fallback, string operationName)
    {
        var workerTask = RunOnDedicatedThread(work);
        if (await Task.WhenAny(workerTask, Task.Delay(NativeCallTimeout)).ConfigureAwait(false) == workerTask)
        {
            return await workerTask.ConfigureAwait(false);
        }

        Debug.WriteLine($"[MIDImunger-W] WinMM '{operationName}' timed out after {NativeCallTimeout.TotalSeconds}s; using fallback.");
        return fallback;
    }

    public Task<IReadOnlyList<MidiEndpoint>> GetInputEndpointsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return WithNativeTimeout<IReadOnlyList<MidiEndpoint>>(
            () =>
            {
                var endpoints = new List<MidiEndpoint>();
                for (uint deviceId = 0; deviceId < midiInGetNumDevs(); deviceId++)
                {
                    ThrowIfError(midiInGetDevCaps(deviceId, out var capabilities, (uint)Marshal.SizeOf<MidiInCaps>()));
                    endpoints.Add(new MidiEndpoint($"in:{deviceId}", capabilities.Name));
                }
                return endpoints;
            },
            fallback: [],
            operationName: "GetInputEndpoints");
    }

    public Task<IReadOnlyList<MidiEndpoint>> GetOutputEndpointsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return WithNativeTimeout<IReadOnlyList<MidiEndpoint>>(
            () =>
            {
                var endpoints = new List<MidiEndpoint>();
                for (uint deviceId = 0; deviceId < midiOutGetNumDevs(); deviceId++)
                {
                    ThrowIfError(midiOutGetDevCaps(deviceId, out var capabilities, (uint)Marshal.SizeOf<MidiOutCaps>()));
                    endpoints.Add(new MidiEndpoint($"out:{deviceId}", capabilities.Name));
                }
                return endpoints;
            },
            fallback: [],
            operationName: "GetOutputEndpoints");
    }

    public Task OpenInputAsync(MidiEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_openInputs.ContainsKey(endpoint.Id))
        {
            return Task.CompletedTask;
        }

        return WithNativeTimeout<bool>(
            () =>
            {
                if (_openInputs.ContainsKey(endpoint.Id))
                {
                    return true;
                }

                var deviceId = ParseDeviceId(endpoint, "in");
                ThrowIfError(midiInOpen(out var handle, deviceId, _inputCallback, IntPtr.Zero, CallbackFunction));
                var input = new OpenInput(endpoint, handle);
                try
                {
                    for (var index = 0; index < SysExBufferCount; index++)
                    {
                        input.Buffers.Add(PrepareSysExBuffer(handle));
                    }

                    if (!_openInputs.TryAdd(endpoint.Id, input))
                    {
                        throw new InvalidOperationException($"Input endpoint '{endpoint.Name}' is already open.");
                    }

                    ThrowIfError(midiInStart(handle));
                    return true;
                }
                catch
                {
                    _openInputs.TryRemove(endpoint.Id, out _);
                    input.Dispose();
                    midiInClose(handle);
                    throw;
                }
            },
            fallback: false,
            operationName: $"OpenInput({endpoint.Name})").ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
    }

    public Task CloseInputAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_openInputs.TryRemove(endpointId, out var input))
        {
            return Task.CompletedTask;
        }

        ThrowIfError(midiInStop(input.Handle));
        ThrowIfError(midiInReset(input.Handle));
        input.Dispose();
        ThrowIfError(midiInClose(input.Handle));
        return Task.CompletedTask;
    }

    public async Task SendAsync(MidiEndpoint endpoint, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.IsEmpty)
        {
            return;
        }

        var deviceId = ParseDeviceId(endpoint, "out");
        var handle = GetOrOpenOutput(deviceId);
        if (bytes.Length <= 3 && bytes.Span[0] != 0xF0)
        {
            uint packed = bytes.Span[0];
            if (bytes.Length > 1) packed |= (uint)bytes.Span[1] << 8;
            if (bytes.Length > 2) packed |= (uint)bytes.Span[2] << 16;
            var result = midiOutShortMsg(handle, packed);
            if (result == MmSysErrInvalHandle)
            {
                // Evict the dead handle so it doesn't contaminate future calls.
                _openOutputs.Remove(deviceId);
            }
            ThrowIfError(result);
            return;
        }

        if (bytes.Span[0] != 0xF0 || bytes.Span[^1] != 0xF7)
        {
            throw new ArgumentException("Long output messages must be complete SysEx messages.", nameof(bytes));
        }

        using var buffer = new SysExBuffer(bytes.ToArray());
        ThrowIfError(midiOutPrepareHeader(handle, buffer.HeaderPointer, (uint)Marshal.SizeOf<MidiHeader>()));
        var headerPrepared = true;
        try
        {
            ThrowIfError(midiOutLongMsg(handle, buffer.HeaderPointer, (uint)Marshal.SizeOf<MidiHeader>()));
            uint result;
            do
            {
                result = midiOutUnprepareHeader(handle, buffer.HeaderPointer, (uint)Marshal.SizeOf<MidiHeader>());
                if (result == MidiErrStillPlaying)
                {
                    await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                }
            }
            while (result == MidiErrStillPlaying);

            ThrowIfError(result);
            headerPrepared = false;
        }
        finally
        {
            if (headerPrepared)
            {
                midiOutUnprepareHeader(handle, buffer.HeaderPointer, (uint)Marshal.SizeOf<MidiHeader>());
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var endpointId in _openInputs.Keys.ToArray())
        {
            await CloseInputAsync(endpointId).ConfigureAwait(false);
        }

        foreach (var handle in _openOutputs.Values)
        {
            midiOutReset(handle);
            midiOutClose(handle);
        }

        _openOutputs.Clear();
    }

    private void HandleInput(IntPtr handle, uint message, IntPtr instance, IntPtr parameter1, IntPtr parameter2)
    {
        var input = _openInputs.Values.FirstOrDefault(candidate => candidate.Handle == handle);
        if (input is null)
        {
            return;
        }

        if (message == MimData)
        {
            var packed = unchecked((uint)parameter1.ToInt64());
            var length = ShortMessageLength((byte)packed);
            var bytes = new byte[length];
            for (var index = 0; index < length; index++)
            {
                bytes[index] = (byte)(packed >> (index * 8));
            }

            PacketReceived?.Invoke(this, new MidiPacketReceivedEventArgs(input.Endpoint.Id, input.Endpoint.Name, bytes));
        }
        else if (message == MimLongData)
        {
            var header = Marshal.PtrToStructure<MidiHeader>(parameter1);
            var bytes = new byte[checked((int)header.BytesRecorded)];
            Marshal.Copy(header.Data, bytes, 0, bytes.Length);
            PacketReceived?.Invoke(this, new MidiPacketReceivedEventArgs(input.Endpoint.Id, input.Endpoint.Name, bytes));
            var result = midiInAddBuffer(handle, parameter1, (uint)Marshal.SizeOf<MidiHeader>());
            if (result != MmSysErrNoError)
            {
                ErrorOccurred?.Invoke(this, new MidiBackendErrorEventArgs(
                    "Requeue SysEx input buffer",
                    new Win32Exception((int)result, $"WinMM MIDI operation failed with error {result}.")));
            }
        }
    }

    private const uint MmSysErrInvalHandle = 6;

    private IntPtr GetOrOpenOutput(uint deviceId)
    {
        if (_openOutputs.TryGetValue(deviceId, out var handle))
        {
            return handle;
        }

        ThrowIfError(midiOutOpen(out handle, deviceId, IntPtr.Zero, IntPtr.Zero, 0));
        _openOutputs.Add(deviceId, handle);
        return handle;
    }

    public void CloseOutput(MidiEndpoint endpoint)
    {
        var deviceId = ParseDeviceId(endpoint, "out");
        if (!_openOutputs.TryGetValue(deviceId, out var handle))
        {
            return;
        }

        _openOutputs.Remove(deviceId);
        midiOutReset(handle);
        midiOutClose(handle);
    }

    private static SysExBuffer PrepareSysExBuffer(IntPtr inputHandle)
    {
        var buffer = new SysExBuffer(SysExBufferSize);
        try
        {
            ThrowIfError(midiInPrepareHeader(inputHandle, buffer.HeaderPointer, (uint)Marshal.SizeOf<MidiHeader>()));
            ThrowIfError(midiInAddBuffer(inputHandle, buffer.HeaderPointer, (uint)Marshal.SizeOf<MidiHeader>()));
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static uint ParseDeviceId(MidiEndpoint endpoint, string direction)
    {
        var prefix = $"{direction}:";
        if (!endpoint.Id.StartsWith(prefix, StringComparison.Ordinal) ||
            !uint.TryParse(endpoint.Id[prefix.Length..], out var deviceId))
        {
            throw new ArgumentException($"Endpoint ID must have the format '{prefix}<deviceId>'.", nameof(endpoint));
        }

        return deviceId;
    }

    private static int ShortMessageLength(byte status) => status switch
    {
        >= 0xC0 and <= 0xDF => 2,
        >= 0xF8 => 1,
        0xF1 or 0xF3 => 2,
        0xF2 => 3,
        _ => 3
    };

    private static void ThrowIfError(uint result)
    {
        if (result != MmSysErrNoError)
        {
            throw new Win32Exception((int)result, $"WinMM MIDI operation failed with error {result}.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class OpenInput(MidiEndpoint endpoint, IntPtr handle) : IDisposable
    {
        public MidiEndpoint Endpoint { get; } = endpoint;
        public IntPtr Handle { get; } = handle;
        public List<SysExBuffer> Buffers { get; } = [];

        public void Dispose()
        {
            foreach (var buffer in Buffers)
            {
                midiInUnprepareHeader(Handle, buffer.HeaderPointer, (uint)Marshal.SizeOf<MidiHeader>());
                buffer.Dispose();
            }
        }
    }

    private sealed class SysExBuffer : IDisposable
    {
        public SysExBuffer(int length)
        {
            DataPointer = Marshal.AllocHGlobal(length);
            HeaderPointer = Marshal.AllocHGlobal(Marshal.SizeOf<MidiHeader>());
            Marshal.StructureToPtr(new MidiHeader { Data = DataPointer, BufferLength = (uint)length }, HeaderPointer, false);
        }

        public SysExBuffer(byte[] bytes) : this(bytes.Length)
        {
            Marshal.Copy(bytes, 0, DataPointer, bytes.Length);
        }

        public IntPtr DataPointer { get; }
        public IntPtr HeaderPointer { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(HeaderPointer);
            Marshal.FreeHGlobal(DataPointer);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinMmMidiInCallback(IntPtr handle, uint message, IntPtr instance, IntPtr parameter1, IntPtr parameter2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
        public uint Support;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public IntPtr Next;
        public IntPtr Reserved;
        public uint Offset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public IntPtr[]? ReservedArray;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiInGetDevCaps(uint deviceId, out MidiInCaps capabilities, uint size);
    [DllImport("winmm.dll")]
    private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll")]
    private static extern uint midiInOpen(out IntPtr handle, uint deviceId, WinMmMidiInCallback callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")]
    private static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")]
    private static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")]
    private static extern uint midiInReset(IntPtr handle);
    [DllImport("winmm.dll")]
    private static extern uint midiInClose(IntPtr handle);
    [DllImport("winmm.dll")]
    private static extern uint midiInPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")]
    private static extern uint midiInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")]
    private static extern uint midiInAddBuffer(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiOutGetDevCaps(uint deviceId, out MidiOutCaps capabilities, uint size);
    [DllImport("winmm.dll")]
    private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll")]
    private static extern uint midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")]
    private static extern uint midiOutClose(IntPtr handle);
    [DllImport("winmm.dll")]
    private static extern uint midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")]
    private static extern uint midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")]
    private static extern uint midiOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")]
    private static extern uint midiOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")]
    private static extern uint midiOutLongMsg(IntPtr handle, IntPtr header, uint size);
}
