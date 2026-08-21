using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MIDImunger.Core;
using System.Windows;

namespace MIDImunger.W;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int MaximumLogEntries = 1_000;
    private readonly IMidiBackend _backend = new WinMmMidiBackend();
    private readonly MidiMonitor _monitor = new();
    private readonly SynchronizationContext _uiContext;
    private string _status = "Waiting for MIDI...";
    private bool _ignoreActiveSensing;
    private MidiEndpointItem[] _enabledOutputSnapshot = [];
    private HashSet<string>? _pendingEnabledInputNames;
    private HashSet<string>? _pendingEnabledOutputNames;

    public MainWindowViewModel()
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("The view model must be created on the UI thread.");
        Channels = new ObservableCollection<ChannelRow>(
            Enumerable.Range(1, 16).Select(number => new ChannelRow(number)));
        VisibleChannels.Add(Channels[0]);

        ControlChanges = new ObservableCollection<ControlChangeDisplay>(
            Enumerable.Range(0, 128).Select(number => new ControlChangeDisplay(number)));
        ControlChangeRows = new ObservableCollection<ControlChangeRow>(
            Enumerable.Range(0, 2).Select(rowIndex => new ControlChangeRow(
                ControlChanges.Skip(rowIndex * 8).Take(8).ToArray(),
                rowIndex < 2, rowIndex)));
        foreach (var row in ControlChangeRows)
        {
            row.RefreshVisibility();
        }

        var preferences = UserPreferencesService.Load();
        _ignoreActiveSensing = preferences?.IgnoreActiveSensing ?? false;
        _pendingEnabledInputNames = preferences?.EnabledInputNames is { } inputNames ? [.. inputNames] : null;
        _pendingEnabledOutputNames = preferences?.EnabledOutputNames is { } outputNames ? [.. outputNames] : null;

        _monitor.MessageReceived += OnMessageReceived;
        _backend.PacketReceived += OnPacketReceived;
        _backend.ErrorOccurred += OnBackendError;
    }

    public ObservableCollection<ChannelRow> Channels { get; }
    public ObservableCollection<ChannelRow> VisibleChannels { get; } = [];
    public ObservableCollection<ControlChangeDisplay> ControlChanges { get; }
    public ObservableCollection<ControlChangeRow> ControlChangeRows { get; }
    public ObservableCollection<EndpointRow> EndpointRows { get; } = [];
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

    public bool IgnoreActiveSensing
    {
        get => _ignoreActiveSensing;
        set
        {
            _ignoreActiveSensing = value;
            OnPropertyChanged();
            SavePreferences();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshEndpointsAsync()
    {
        var activeInputNames = Inputs.Where(input => input.IsEnabled).Select(input => input.Name).ToHashSet();
        var activeOutputNames = Outputs.Where(output => output.IsEnabled).Select(output => output.Name).ToHashSet();
        if (_pendingEnabledInputNames is { } pendingInputs)
        {
            activeInputNames.UnionWith(pendingInputs);
            _pendingEnabledInputNames = null;
        }

        if (_pendingEnabledOutputNames is { } pendingOutputs)
        {
            activeOutputNames.UnionWith(pendingOutputs);
            _pendingEnabledOutputNames = null;
        }

        try
        {
            var inputs = await _backend.GetInputEndpointsAsync();
            var outputs = await _backend.GetOutputEndpointsAsync();
            Inputs.Clear();
            foreach (var input in inputs)
            {
                Inputs.Add(new MidiEndpointItem(input, activeInputNames.Contains(input.Name)));
            }

            Outputs.Clear();
            foreach (var output in outputs)
            {
                Outputs.Add(new MidiEndpointItem(output, activeOutputNames.Contains(output.Name)));
            }

            RefreshEnabledOutputSnapshot();
            RebuildEndpointRows();

            foreach (var input in Inputs.Where(input => input.IsEnabled))
            {
                try
                {
                    await _backend.OpenInputAsync(input.Endpoint);
                }
                catch (Win32Exception exception)
                {
                    input.IsEnabled = false;
                    Status = $"Could not restore input {input.Name}: {exception.Message}";
                }
            }

            if (!Inputs.Any(input => input.IsEnabled))
            {
                Status = "Waiting for MIDI...";
            }
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

            SavePreferences();
        }
        catch (Win32Exception exception)
        {
            item.IsEnabled = !item.IsEnabled;
            Status = $"Could not change input {item.Name}: {exception.Message}";
        }
    }

    public void SetOutputEnabled(MidiEndpointItem item)
    {
        RefreshEnabledOutputSnapshot();
        Status = EnabledOutputs.Count == 0
            ? "Monitoring enabled inputs without MIDI Thru."
            : $"Forwarding enabled inputs to {EnabledOutputs.Count} MIDI Thru destination(s).";
        SavePreferences();
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

    public void ClearControlChanges()
    {
        foreach (var controlChange in ControlChanges)
        {
            controlChange.Clear();
        }

        foreach (var controlChangeRow in ControlChangeRows)
        {
            controlChangeRow.RefreshVisibility();
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
        _ = ForwardPacketToOutputsAsync(args);
        _uiContext.Post(_ => _monitor.ProcessPacket(args.Bytes.Span, args.SourceName, args.SourceId), null);
    }

    private void OnBackendError(object? sender, MidiBackendErrorEventArgs args) =>
        _uiContext.Post(_ => Status = $"{args.Operation} failed: {args.Exception.Message}", null);

    private void OnMessageReceived(object? sender, MidiMessage message)
    {
        if (IgnoreActiveSensing && IsActiveSensing(message))
        {
            return;
        }

        if (message.Channel is int channel)
        {
            var row = Channels[channel - 1];
            if (!VisibleChannels.Contains(row))
            {
                VisibleChannels.Add(row);
            }

            row.HasReceivedData = true;
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
                if (message.Bytes[1] <= 127)
                {
                    ControlChanges[message.Bytes[1]].Update(message.Bytes[2], channel);
                    var rowIndex = message.Bytes[1] / 8;
                    var ccRow = ControlChangeRows.FirstOrDefault(existing => existing.RowIndex == rowIndex);
                    if (ccRow is null)
                    {
                        ccRow = new ControlChangeRow(
                            ControlChanges.Skip(rowIndex * 8).Take(8).ToArray(),
                            false,
                            rowIndex);
                        ControlChangeRows.Add(ccRow);
                    }

                    ccRow.RefreshVisibility();
                }
            }
        }

        var sourceName = string.IsNullOrWhiteSpace(message.SourceName)
            ? "(Unknown Source)"
            : message.SourceName;
        LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {sourceName}  {message.Description}");
        if (LogEntries.Count > MaximumLogEntries)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }

        Status = $"{sourceName}  {message.Description}";
    }

    private static bool IsActiveSensing(MidiMessage message) =>
        message.Kind == MidiMessageKind.SystemRealtime && message.Bytes is [0xFE];

    private void SavePreferences()
    {
        var enabledInputNames = Inputs.Where(input => input.IsEnabled).Select(input => input.Name).ToArray();
        var enabledOutputNames = Outputs.Where(output => output.IsEnabled).Select(output => output.Name).ToArray();
        UserPreferencesService.Save(new UserPreferences(IgnoreActiveSensing, enabledInputNames, enabledOutputNames));
    }

    private async Task ForwardPacketToOutputsAsync(MidiPacketReceivedEventArgs args)
    {
        foreach (var output in _enabledOutputSnapshot)
        {
            if (!IsNotSameEndpoint(args.SourceId, output.Endpoint.Id))
            {
                continue;
            }

            try
            {
                await _backend.SendAsync(output.Endpoint, args.Bytes).ConfigureAwait(false);
            }
            catch (Win32Exception exception)
            {
                _uiContext.Post(_ => Status = $"Could not forward MIDI to {output.Name}: {exception.Message}", null);
            }
        }
    }

    private void RefreshEnabledOutputSnapshot() =>
        _enabledOutputSnapshot = Outputs.Where(output => output.IsEnabled).ToArray();

    private void RebuildEndpointRows()
    {
        var inputsByName = Inputs
            .GroupBy(input => input.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var outputsByName = Outputs
            .GroupBy(output => output.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var endpointNames = inputsByName.Keys
            .Concat(outputsByName.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);

        EndpointRows.Clear();
        foreach (var endpointName in endpointNames)
        {
            inputsByName.TryGetValue(endpointName, out var input);
            outputsByName.TryGetValue(endpointName, out var output);
            EndpointRows.Add(new EndpointRow(input, output));
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

public sealed class EndpointRow(MidiEndpointItem? input, MidiEndpointItem? output)
{
    public MidiEndpointItem? Input { get; } = input;
    public MidiEndpointItem? Output { get; } = output;
}

public sealed class ControlChangeDisplay(int controllerNumber) : INotifyPropertyChanged
{
    private bool _hasValue;
    private int _value;
    private int _channel;

    public int ControllerNumber { get; } = controllerNumber;
    public bool HasValue => _hasValue;
    public string ControllerLabel => $"CC{ControllerNumber}";
    public string ValueDisplay => _hasValue ? _value.ToString("D3") : string.Empty;
    public string ChannelDisplay => _hasValue ? _channel.ToString("D2") : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(int value, int channel)
    {
        _hasValue = true;
        _value = value;
        _channel = channel;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChannelDisplay)));
    }

    public void Clear()
    {
        _hasValue = false;
        _value = 0;
        _channel = 0;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChannelDisplay)));
    }
}

public sealed class ControlChangeRow : INotifyPropertyChanged
{
    private readonly bool _alwaysVisible;
    private bool _isVisible;

    public ControlChangeRow(IReadOnlyList<ControlChangeDisplay> displays, bool alwaysVisible, int rowIndex)
    {
        Displays = displays;
        RowIndex = rowIndex;
        _alwaysVisible = alwaysVisible;
        _isVisible = alwaysVisible;
    }

    public IReadOnlyList<ControlChangeDisplay> Displays { get; }
    public int RowIndex { get; }
    public Visibility Visibility => _isVisible ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshVisibility()
    {
        var isVisible = _alwaysVisible || Displays.Any(display => display.HasValue);
        if (_isVisible == isVisible)
        {
            return;
        }

        _isVisible = isVisible;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
    }
}

public sealed class ChannelRow(int number) : INotifyPropertyChanged
{
    private bool _hasReceivedData;
    private string _lastMessage = "Waiting for MIDI...";
    private string _lastNote = "-";
    private string _lastVelocity = "-";
    private string _lastProgram = "-";
    private string _lastController = "-";

    public int Number { get; } = number;
    public bool HasReceivedData { get => _hasReceivedData; set => SetField(ref _hasReceivedData, value); }
    public string LastMessage { get => _lastMessage; set => SetField(ref _lastMessage, value); }
    public string LastNote { get => _lastNote; set => SetField(ref _lastNote, value); }
    public string LastVelocity { get => _lastVelocity; set => SetField(ref _lastVelocity, value); }
    public string LastProgram { get => _lastProgram; set => SetField(ref _lastProgram, value); }
    public string LastController { get => _lastController; set => SetField(ref _lastController, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
