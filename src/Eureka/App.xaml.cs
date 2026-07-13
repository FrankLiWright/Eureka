using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Eureka;

public partial class App : Application
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    
    public static bool IsDarkTheme { get; private set; } = true;
    
    public static event Action<bool>? ThemeChanged;
    
    private void App_Startup(object sender, StartupEventArgs e)
    {
        // Detect system theme
        IsDarkTheme = GetSystemTheme();
        
        // Listen for theme changes
        SystemEvents.UserPreferenceChanged += (s, args) =>
        {
            var newTheme = GetSystemTheme();
            if (newTheme != IsDarkTheme)
            {
                IsDarkTheme = newTheme;
                Dispatcher.Invoke(() =>
                {
                    ApplyTheme(IsDarkTheme);
                    ThemeChanged?.Invoke(IsDarkTheme);
                });
            }
        };
        
        ApplyTheme(IsDarkTheme);
        
        // Handle file argument (drag to exe)
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            // Will be handled by MainWindow
        }
    }
    
    private static bool GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int v && v == 0; // 0 = dark, 1 = light
        }
        catch
        {
            return true; // Default to dark
        }
    }
    
    private void ApplyTheme(bool isDark)
    {
        var dict = Resources.MergedDictionaries[0];
        
        if (isDark)
        {
            dict["BackgroundBrush"] = new SolidColorBrush((Color)dict["DarkBackground"]);
            dict["SurfaceBrush"] = new SolidColorBrush((Color)dict["DarkSurface"]);
            dict["SurfaceAltBrush"] = new SolidColorBrush((Color)dict["DarkSurfaceAlt"]);
            dict["BorderBrush"] = new SolidColorBrush((Color)dict["DarkBorder"]);
            dict["TextBrush"] = new SolidColorBrush((Color)dict["DarkText"]);
            dict["TextSecondaryBrush"] = new SolidColorBrush((Color)dict["DarkTextSecondary"]);
            dict["TextTertiaryBrush"] = new SolidColorBrush((Color)dict["DarkTextTertiary"]);
            dict["AccentBrush"] = new SolidColorBrush((Color)dict["DarkAccent"]);
            dict["AccentHoverBrush"] = new SolidColorBrush((Color)dict["DarkAccentHover"]);
        }
        else
        {
            dict["BackgroundBrush"] = new SolidColorBrush((Color)dict["LightBackground"]);
            dict["SurfaceBrush"] = new SolidColorBrush((Color)dict["LightSurface"]);
            dict["SurfaceAltBrush"] = new SolidColorBrush((Color)dict["LightSurfaceAlt"]);
            dict["BorderBrush"] = new SolidColorBrush((Color)dict["LightBorder"]);
            dict["TextBrush"] = new SolidColorBrush((Color)dict["LightText"]);
            dict["TextSecondaryBrush"] = new SolidColorBrush((Color)dict["LightTextSecondary"]);
            dict["TextTertiaryBrush"] = new SolidColorBrush((Color)dict["LightTextTertiary"]);
            dict["AccentBrush"] = new SolidColorBrush((Color)dict["LightAccent"]);
            dict["AccentHoverBrush"] = new SolidColorBrush((Color)dict["LightAccentHover"]);
        }
    }
    
    public static void SetDarkMode(IntPtr hwnd)
    {
        int value = IsDarkTheme ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }
}
