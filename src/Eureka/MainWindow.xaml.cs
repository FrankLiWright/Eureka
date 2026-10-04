using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Eureka;

public partial class MainWindow : Window
{
    private readonly ImageDecoder _decoder = new();
    private ImageMetadata? _metadata;
    private BitmapSource? _originalBitmap;
    private BitmapSource? _sdrCache;
    private bool _infoPanelVisible;
    
    public MainWindow()
    {
        InitializeComponent();
        
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            App.SetDarkMode(hwnd);
        };
        
        App.ThemeChanged += _ => InvalidateVisual();
        
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            Loaded += (_, _) => LoadImage(args[1]);
        }
    }
    
    private async void LoadImage(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Length > 100 * 1024 * 1024)
            {
                var warning = new LargeFileWarning(fi.Name, fi.Length);
                warning.Owner = this;
                warning.ShowDialog();
                if (!warning.ShouldOpen) return;
            }
            
            _sdrCache = null;
            
            Title = "Eureka - Loading...";
            FileInfoText.Text = "Loading...";
            Canvas.ImageSource = null;
            DropHint.Visibility = Visibility.Visible;
            var hint = DropHint.Children[0] as System.Windows.Controls.TextBlock;
            if (hint != null) hint.Text = "Loading...";
            
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            
            // 在后台线程同时获取元数据和解码图像
            var metaTask = Task.Run(() => _decoder.LoadMetadata(filePath));
            var imgTask = Task.Run(() => _decoder.DecodeImage(filePath));
            
            _metadata = await metaTask;
            Title = $"Eureka - {_metadata.FileName}";
            FileInfoText.Text = $"{_metadata.Width}x{_metadata.Height}  |  {_metadata.Format}  |  {FormatSize(_metadata.FileSize)}";
            
            _originalBitmap = await imgTask;
            if (_originalBitmap == null) { Title = "Eureka"; FileInfoText.Text = "No image loaded"; return; }
            
            if (_metadata.Orientation > 1)
                _originalBitmap = ApplyOrientation(_originalBitmap, _metadata.Orientation);
            
            if (_metadata.IsHDR)
                _sdrCache = await Task.Run(() => _decoder.ConvertHdrToSdr(_originalBitmap));
            
            await Dispatcher.InvokeAsync(() =>
            {
                if (_metadata.IsHDR)
                {
                    Canvas.ImageSource = _originalBitmap;
                    HdrToggle.IsChecked = true;
                    HdrToggle.Visibility = Visibility.Visible;
                    SdrLabel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    Canvas.ImageSource = _originalBitmap;
                    HdrToggle.IsChecked = false;
                    HdrToggle.Visibility = Visibility.Collapsed;
                    SdrLabel.Visibility = Visibility.Visible;
                }
                
                DropHint.Visibility = Visibility.Collapsed;
                var hintReset = DropHint.Children[0] as System.Windows.Controls.TextBlock;
                if (hintReset != null) hintReset.Text = "Drop image here or click Open";
                
                UpdateInfoPanel();
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open image: {ex.Message}", "Eureka", MessageBoxButton.OK, MessageBoxImage.Error);
            Title = "Eureka";
            FileInfoText.Text = "No image loaded";
        }
    }
    
    private static BitmapSource ApplyOrientation(BitmapSource bmp, int orientation)
    {
        try
        {
            var transformed = bmp;
            switch (orientation)
            {
                case 2: transformed = new TransformedBitmap(bmp, new ScaleTransform(-1, 1)); break; // Flip horizontal
                case 3: transformed = new TransformedBitmap(bmp, new RotateTransform(180)); break;
                case 4: transformed = new TransformedBitmap(bmp, new ScaleTransform(1, -1)); break; // Flip vertical
                case 5: transformed = new TransformedBitmap(bmp, new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } }); break;
                case 6: transformed = new TransformedBitmap(bmp, new RotateTransform(90)); break;
                case 7: transformed = new TransformedBitmap(bmp, new TransformGroup { Children = { new RotateTransform(-90), new ScaleTransform(-1, 1) } }); break;
                case 8: transformed = new TransformedBitmap(bmp, new RotateTransform(-90)); break;
                default: return bmp;
            }
            transformed.Freeze();
            return transformed;
        }
        catch { return bmp; }
    }
    
    private void UpdateInfoPanel()
    {
        if (_metadata == null) return;
        InfoContent.Children.Clear();
        
        AddInfoSection("File");
        AddInfoRow("Name", _metadata.FileName);
        AddInfoRow("Format", _metadata.Format);
        AddInfoRow("Size", FormatSize(_metadata.FileSize));
        AddInfoRow("Dimensions", $"{_metadata.Width} x {_metadata.Height}");
        AddInfoRow("Bit Depth", $"{_metadata.BitsPerPixel} bit");
        AddInfoRow("Color Space", _metadata.ColorSpace);
        AddInfoRow("Has Alpha", _metadata.HasAlpha ? "Yes" : "No");
        if (_metadata.ColorProfile != null) AddInfoRow("ICC Profile", _metadata.ColorProfile);
        if (_metadata.IsHDR) AddInfoRow("HDR", "Yes");
        
        AddInfoSection("EXIF");
        AddInfoIfNotNull("Camera", Combine(_metadata.CameraMake, _metadata.CameraModel));
        AddInfoIfNotNull("Serial", _metadata.CameraSerial);
        AddInfoIfNotNull("Lens", Combine(_metadata.LensMake, _metadata.LensModel));
        AddInfoIfNotNull("Date", _metadata.DateTaken?.ToString("yyyy-MM-dd HH:mm:ss"));
        AddInfoIfNotNull("Shutter", _metadata.ExposureTime);
        AddInfoIfNotNull("Aperture", _metadata.ApertureDisplay ?? (_metadata.FNumber != null ? $"f/{_metadata.FNumber:F1}" : null));
        AddInfoIfNotNull("ISO", _metadata.IsoDisplay ?? _metadata.IsoSpeed?.ToString());
        AddInfoIfNotNull("Focal", _metadata.FocalLength);
        AddInfoIfNotNull("Focal 35mm", _metadata.FocalLength35mm);
        AddInfoIfNotNull("Exposure", _metadata.ExposureBias);
        AddInfoIfNotNull("Max Aperture", _metadata.MaxAperture);
        AddInfoIfNotNull("Metering", _metadata.MeteringMode);
        AddInfoIfNotNull("Distance", _metadata.SubjectDistance);
        AddInfoIfNotNull("Zoom", _metadata.DigitalZoom);
        AddInfoIfNotNull("Light", _metadata.LightSource);
        AddInfoIfNotNull("Brightness", _metadata.Brightness);
        AddInfoIfNotNull("Program", _metadata.ProgramMode);
        AddInfoIfNotNull("WB", _metadata.WhiteBalance);
        AddInfoIfNotNull("Flash", _metadata.Flash);
        if (!string.IsNullOrEmpty(_metadata.OrientationDisplay))
            AddInfoIfNotNull("Orientation", _metadata.OrientationDisplay);
        AddInfoIfNotNull("Software", _metadata.Software);
        AddInfoIfNotNull("Artist", _metadata.Artist);
        AddInfoIfNotNull("Copyright", _metadata.Copyright);
        AddInfoIfNotNull("Title", _metadata.Title);
        AddInfoIfNotNull("Subject", _metadata.Subject);
        AddInfoIfNotNull("Keywords", _metadata.Keywords);
    }
    
    private void AddInfoSection(string title)
    {
        InfoContent.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("AccentBrush"),
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 8)
        });
    }
    
    private void AddInfoRow(string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("TextSecondaryBrush"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var v = new TextBlock { Text = value, Foreground = (Brush)FindResource("TextBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        InfoContent.Children.Add(g);
    }
    
    private void AddInfoIfNotNull(string label, string? value) { if (!string.IsNullOrEmpty(value)) AddInfoRow(label, value); }
    private static string? Combine(string? a, string? b) => string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : $"{a} {b}";
    private static string FormatSize(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:F1} KB" : $"{b / 1048576.0:F1} MB";
    
    private void ApplyHdrConversion()
    {
        if (_originalBitmap == null || _metadata == null) return;
        var converted = _decoder.ConvertHdrToSdr(_originalBitmap);
        if (converted != null) Canvas.ImageSource = converted;
    }
    
    private void OnZoomChanged(double zoom) => ZoomText.Text = $"{zoom * 100:F0}%";
    
    private void OnPixelHovered(int x, int y, Color c) 
    { 
        ColorSwatch.Background = new SolidColorBrush(c); 
        ColorText.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}  ({x},{y})"; 
    }
    
    private void OnFitToWindow(object s, RoutedEventArgs e) => Canvas.FitToWindow();
    private void OnOriginalSize(object s, RoutedEventArgs e) => Canvas.OriginalSize();
    private void OnRotateLeft(object s, RoutedEventArgs e) => Canvas.Rotate(-90);
    private void OnRotateRight(object s, RoutedEventArgs e) => Canvas.Rotate(90);
    
    private async void OnHdrToggle(object s, RoutedEventArgs e)
    {
        if (_metadata?.IsHDR != true || _originalBitmap == null) return;
        
        if (HdrToggle.IsChecked == true)
        {
            Canvas.ImageSource = _originalBitmap;
        }
        else
        {
            if (_sdrCache != null)
            {
                Canvas.ImageSource = _sdrCache;
            }
            else
            {
                Title = "Eureka - Converting...";
                _sdrCache = await Task.Run(() => _decoder.ConvertHdrToSdr(_originalBitmap));
                Canvas.ImageSource = _sdrCache ?? _originalBitmap;
                Title = $"Eureka - {_metadata.FileName}";
            }
        }
    }
    
    private void OnOpenFile(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Title = "Open Image",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif;*.webp;*.ico;*.avif;*.heif;*.heic;*.jxl|RAW Files|*.cr2;*.cr3;*.nef;*.nrw;*.arw;*.srf;*.sr2;*.orf;*.rw2;*.raf;*.dng;*.pef;*.raw|All Files|*.*"
        };
        if (d.ShowDialog() == true) LoadImage(d.FileName);
    }
    
    private void OnToggleInfo(object s, RoutedEventArgs e)
    {
        _infoPanelVisible = !_infoPanelVisible;
        InfoPanel.Visibility = _infoPanelVisible ? Visibility.Visible : Visibility.Collapsed;
    }
    
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0 && File.Exists(files[0]))
            {
                LoadImage(files[0]);
                e.Handled = true;
            }
        }
    }
    
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }
}
