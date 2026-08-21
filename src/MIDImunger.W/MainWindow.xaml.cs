using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace MIDImunger.W;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainWindowViewModel();
        viewModel.SetDebugStatus("Startup: creating UI model...");
        DataContext = viewModel;

        ApplyWindowPlacement();
    }

    private async void AllNotesOff_Click(object sender, RoutedEventArgs e)
    {
        await ((MainWindowViewModel)DataContext).SendAllNotesOffAsync();
    }

    private async void DxPlay_Click(object sender, RoutedEventArgs e)
    {
        await ((MainWindowViewModel)DataContext).SendDxPlayAsync();
    }

    private void ClearCc_Click(object sender, RoutedEventArgs e)
    {
        ((MainWindowViewModel)DataContext).ClearControlChanges();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext;
        viewModel.SetDebugStatus("Startup: scheduling MIDI device refresh...");
        Debug.WriteLine("[MIDImunger-W] Window_Loaded: scheduling RefreshEndpointsAsync.");

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Debug.WriteLine("[MIDImunger-W] Background startup: RefreshEndpointsAsync begins.");
            _ = viewModel.RefreshEndpointsAsync();
        }));
    }

    public void ApplySystemTheme() =>
        ThemeResources.ApplyWindowTheme(this);

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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        WindowSettingsService.Save(new WindowPlacementSettings(bounds.Left, bounds.Top, bounds.Width, bounds.Height, isMaximized));
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        ApplySystemTheme();
    }

    private void ApplyWindowPlacement()
    {
        var settings = WindowSettingsService.Load();
        if (settings is null)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        if (IsOnScreen(settings.Left, settings.Top, settings.Width > 0 ? settings.Width : Width, settings.Height > 0 ? settings.Height : Height))
        {
            Left = settings.Left;
            Top = settings.Top;
        }

        if (!settings.IsMaximized)
        {
            if (settings.Width > 0)
            {
                Width = settings.Width;
            }

            if (settings.Height > 0)
            {
                Height = settings.Height;
            }
        }

        if (settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        return virtualScreen.IntersectsWith(new Rect(left, top, width, height));
    }
}