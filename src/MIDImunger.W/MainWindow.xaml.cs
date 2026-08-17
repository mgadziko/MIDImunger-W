using System.Windows;

namespace MIDImunger.W;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private void AllNotesOff_Click(object sender, RoutedEventArgs e)
    {
        ((MainWindowViewModel)DataContext).SendAllNotesOff();
    }
}