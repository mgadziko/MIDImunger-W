using System.Windows;

namespace MIDImunger.W;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private async void AllNotesOff_Click(object sender, RoutedEventArgs e)
    {
        await ((MainWindowViewModel)DataContext).SendAllNotesOffAsync();
    }

    private async void DxPlay_Click(object sender, RoutedEventArgs e)
    {
        await ((MainWindowViewModel)DataContext).SendDxPlayAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ((MainWindowViewModel)DataContext).RefreshEndpointsAsync();
    }

    private async void RefreshEndpoints_Click(object sender, RoutedEventArgs e)
    {
        await ((MainWindowViewModel)DataContext).RefreshEndpointsAsync();
    }

    private async void InputEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MidiEndpointItem endpoint })
        {
            await ((MainWindowViewModel)DataContext).SetInputEnabledAsync(endpoint);
        }
    }

    private void OutputEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MidiEndpointItem endpoint })
        {
            ((MainWindowViewModel)DataContext).SetOutputEnabled(endpoint);
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        await ((MainWindowViewModel)DataContext).DisposeAsync();
    }
}