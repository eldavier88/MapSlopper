using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MapSlopper.Gui;

public class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}