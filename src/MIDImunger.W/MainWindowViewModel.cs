using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MIDImunger.Core;

namespace MIDImunger.W;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int MaximumLogEntries = 1_000;
    private readonly IMidiBackend _backend = new WinMmMidiBackend();
    private readonly MidiMonitor _monitor = new();
    private readonly SynchronizationContext _uiContext;
    private string _status = "Discovering Windows MIDI devices...";

    public MainWindowViewModel()
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("The view model must be created on the UI thread.");
        Channels = new ObservableCollection<ChannelRow>(
            Enumerable.Range(1, 16).Select(number => new ChannelRow(number)));
        _monitor.MessageReceived += OnMessageReceived;
        _backend.PacketReceived += OnPacketReceived;
        _backend.ErrorOccurred += OnBackendError;
    }

    public ObservableCollection<ChannelRow> Channels { get; }
    public ObservableCollection<MidiEndpointItem> Inputs { get; } = [];
    public ObservableCollection<MidiEndpointItem> Outputs { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshEndpointsAsync()
    {
        var activeInputs = Inputs.Where(input => input.IsEnabled).Select(input => input.Endpoint.Id).ToHashSet();
        var activeOutputs = Outputs.Where(output => output.IsEnabled).Select(output => output.Endpoint.Id).ToHashSet();
        try
        {
            var inputs = await _backend.GetInputEndpointsAsync();
            var outputs = await _backend.GetOutputEndpointsAsync();
            Inputs.Clear();
            foreach (var input in inputs)
            {
                Inputs.Add(new MidiEndpointItem(input, activeInputs.Contains(input.Id)));
            }

            Outputs.Clear();
            foreach (var output in outputs)
            {
                Outputs.Add(new MidiEndpointItem(output, activeOutputs.Contains(output.Id)));
            }

            Status = $"Found {Inputs.Count} input(s) and {Outputs.Count} output(s).";
        }
        catch (Win32Exception exception)
        {
            Status = $"Could not enumerate MIDI devices: {exception.Message}";
        }
    }

    public async Task SetInputEnabledAsync(MidiEndpointItem item)
    {
        try
        {
            if (item.IsEnabled)
            {
                await _backend.OpenInputAsync(item.Endpoint);
                Status = $"Listening to {item.Name}.";
            }
            else
            {
                await _backend.CloseInputAsync(item.Endpoint.Id);
                Status = $"Stopped listening to {item.Name}.";
            }
        }
        catch (Win32Exception exception)
        {
            item.IsEnabled = !item.IsEnabled;
            Status = $"Could not change input {item.Name}: {exception.Message}";
        }
    }

    public void SetOutputEnabled(MidiEndpointItem item)
    {
        Status = EnabledOutputs.Count == 0
            ? "Monitoring enabled inputs without MIDI Thru."
            : $"Forwarding enabled inputs to {EnabledOutputs.Count} MIDI Thru destination(s).";
    }

    public async Task SendAllNotesOffAsync()
    {
        if (EnabledOutputs.Count == 0)
        {
            Status = "Select at least one MIDI Thru destination before sending All Notes Off.";
            return;
        }

        try
        {
            foreach (var output in EnabledOutputs)
            {
                for (var channel = 0; channel < 16; channel++)
                {
                    await _backend.SendAsync(output.Endpoint, new byte[] { (byte)(0xB0 | channel), 123, 0 });
                }
            }

            Status = $"Sent All Notes Off to {EnabledOutputs.Count} MIDI Thru destination(s).";
        }
        catch (Win32Exception exception)
        {
            Status = $"Could not send All Notes Off: {exception.Message}";
        }
    }

    public async Task SendDxPlayAsync()
    {
        if (EnabledOutputs.Count == 0)
        {
            Status = "Select at least one MIDI Thru destination before sending DX Play.";
            return;
        }

        try
        {
            foreach (var output in EnabledOutputs)
            {
                await _backend.SendAsync(output.Endpoint, Dx100Commands.CreatePlayPress());
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            foreach (var output in EnabledOutputs)
            {
                await _backend.SendAsync(output.Endpoint, Dx100Commands.CreatePlayRelease());
            }

            Status = $"Sent DX Play recovery command to {EnabledOutputs.Count} MIDI Thru destination(s).";
        }
        catch (Win32Exception exception)
        {
            Status = $"Could not send DX Play: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _backend.PacketReceived -= OnPacketReceived;
        _backend.ErrorOccurred -= OnBackendError;
        await _backend.DisposeAsync();
    }

    private void OnPacketReceived(object? sender, MidiPacketReceivedEventArgs args)
    {
        _uiContext.Post(async _ =>
        {
            _monitor.ProcessPacket(args.Bytes.Span, args.SourceName, args.SourceId);
            foreach (var output in EnabledOutputs.Where(output => IsNotSameEndpoint(args.SourceId, output.Endpoint.Id)))
            {
                try
                {
                    await _backend.SendAsync(output.Endpoint, args.Bytes);
                }
                catch (Win32Exception exception)
                {
                    Status = $"Could not forward MIDI to {output.Name}: {exception.Message}";
                }
            }
        }, null);
    }

    private void OnBackendError(object? sender, MidiBackendErrorEventArgs args) =>
        _uiContext.Post(_ => Status = $"{args.Operation} failed: {args.Exception.Message}", null);

    private void OnMessageReceived(object? sender, MidiMessage message)
    {
        if (message.Channel is int channel)
        {
            var row = Channels[channel - 1];
            row.LastMessage = message.Description;
            if (message.Kind is MidiMessageKind.NoteOn or MidiMessageKind.NoteOff)
            {
                row.LastNote = message.Bytes[1].ToString();
                row.LastVelocity = message.Bytes[2].ToString();
            }
            else if (message.Kind == MidiMessageKind.ProgramChange)
            {
                row.LastProgram = (message.Bytes[1] + 1).ToString();
            }
            else if (message.Kind == MidiMessageKind.ControlChange)
            {
                row.LastController = $"{message.Bytes[1]}: {message.Bytes[2]}";
            }
        }

        LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message.Description}");
        if (LogEntries.Count > MaximumLogEntries)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    private static bool IsNotSameEndpoint(string inputId, string outputId) =>
        !string.Equals(inputId[3..], outputId[4..], StringComparison.Ordinal);

    private List<MidiEndpointItem> EnabledOutputs => Outputs.Where(output => output.IsEnabled).ToList();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class MidiEndpointItem(MidiEndpoint endpoint, bool isEnabled = false) : INotifyPropertyChanged
{
    private bool _isEnabled = isEnabled;

    public MidiEndpoint Endpoint { get; } = endpoint;
    public string Name => Endpoint.Name;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ChannelRow(int number) : INotifyPropertyChanged
{
    private string _lastMessage = "Waiting for MIDI...";
    private string _lastNote = "-";
    private string _lastVelocity = "-";
    private string _lastProgram = "-";
    private string _lastController = "-";

    public int Number { get; } = number;
    public string LastMessage { get => _lastMessage; set => SetField(ref _lastMessage, value); }
    public string LastNote { get => _lastNote; set => SetField(ref _lastNote, value); }
    public string LastVelocity { get => _lastVelocity; set => SetField(ref _lastVelocity, value); }
    public string LastProgram { get => _lastProgram; set => SetField(ref _lastProgram, value); }
    public string LastController { get => _lastController; set => SetField(ref _lastController, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
