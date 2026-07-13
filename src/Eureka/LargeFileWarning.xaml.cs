using System.Windows;

namespace Eureka;

public partial class LargeFileWarning : Window
{
    public bool ShouldOpen { get; private set; }
    
    public LargeFileWarning(string fileName, long fileSize)
    {
        InitializeComponent();
        
        var sizeMB = fileSize / (1024.0 * 1024.0);
        MessageText.Text = $"\"{fileName}\" is {sizeMB:F0} MB.\n\nOpening this file may use significant memory and take longer to load.";
        
        // Apply dark mode
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            App.SetDarkMode(hwnd);
        };
    }
    
    private void OnOpen(object s, RoutedEventArgs e)
    {
        ShouldOpen = true;
        Close();
    }
    
    private void OnCancel(object s, RoutedEventArgs e)
    {
        Close();
    }
}
