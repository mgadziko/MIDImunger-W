using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MIDImunger.Core;

namespace MIDImunger.W;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int MaximumLogEntries = 1_000;
    private readonly MidiMonitor _monitor = new();
    private string _status = "No Windows MIDI backend configured yet.";

    public MainWindowViewModel()
    {
        Channels = new ObservableCollection<ChannelRow>(
            Enumerable.Range(1, 16).Select(number => new ChannelRow(number)));
        _monitor.MessageReceived += OnMessageReceived;
        _monitor.ProcessPacket([0x90, 60, 100], "Demo input");
    }

    public ObservableCollection<ChannelRow> Channels { get; }
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

    public void SendAllNotesOff()
    {
        Status = "All Notes Off is unavailable until an output endpoint is selected.";
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
