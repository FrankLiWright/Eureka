using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Eureka.Controls;

/// <summary>
/// High-performance image canvas with zoom/pan and color management
/// </summary>
public class ImageCanvas : Control
{
    private BitmapSource? _bitmap;
    private BitmapSource? _sampleBitmap;
    private double _zoom = 1.0;
    private Point _panOffset;
    private Point _lastMousePos;
    private bool _isPanning;

    public const double MaxZoom = 10.0;
    public const double MinZoom = 0.01;

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(BitmapSource), typeof(ImageCanvas),
            new PropertyMetadata(null, OnImageChanged));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(ImageCanvas),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Backdrop the eye sees through transparent pixels. Color picking
    /// composites onto this so hidden RGB under alpha is never reported.
    /// </summary>
    public static readonly DependencyProperty BackdropColorProperty =
        DependencyProperty.Register(nameof(BackdropColor), typeof(Color), typeof(ImageCanvas),
            new PropertyMetadata(Color.FromRgb(0x1E, 0x1E, 0x1E)));

    public event Action<double>? ZoomChanged;

    /// <summary>x, y, visible RGB (composited over backdrop), source alpha (255 = opaque).</summary>
    public event Action<int, int, Color, byte>? PixelHovered;

    public BitmapSource? ImageSource
    {
        get => (BitmapSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public Color BackdropColor
    {
        get => (Color)GetValue(BackdropColorProperty);
        set => SetValue(BackdropColorProperty, value);
    }
    
    public double MaxZoomLimit => MaxZoom;
    
    public ImageCanvas()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;
        
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += OnLeftUp;
        MouseMove += OnMouseMove;
        MouseRightButtonDown += OnRightDown;
        SizeChanged += (_, _) => { if (_bitmap != null) FitToWindow(); };
    }
    
    private static void OnImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ImageCanvas)d;
        c._bitmap = (BitmapSource?)e.NewValue;
        c._sampleBitmap = null;
        c.FitToWindow();
        c.InvalidateVisual();
    }
    
    public void FitToWindow()
    {
        if (_bitmap == null || ActualWidth == 0 || ActualHeight == 0) return;
        
        var sx = ActualWidth / _bitmap.PixelWidth;
        var sy = ActualHeight / _bitmap.PixelHeight;
        _zoom = Math.Min(sx, sy) * 0.95;
        
        CenterImage();
        ZoomChanged?.Invoke(_zoom);
        InvalidateVisual();
    }
    
    public void OriginalSize()
    {
        if (_bitmap == null) return;
        _zoom = 1.0;
        CenterImage();
        ZoomChanged?.Invoke(_zoom);
        InvalidateVisual();
    }
    
    public void SetZoom(double zoom, bool fromCenter = false, Point? mousePos = null)
    {
        if (_bitmap == null) return;
        
        var oldZoom = _zoom;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        
        if (Math.Abs(_zoom - oldZoom) < 0.001) return;
        
        Point center;
        if (fromCenter || !mousePos.HasValue)
        {
            center = new Point(ActualWidth / 2, ActualHeight / 2);
        }
        else
        {
            center = mousePos.Value;
        }
        
        _panOffset = new Point(
            center.X - (center.X - _panOffset.X) * _zoom / oldZoom,
            center.Y - (center.Y - _panOffset.Y) * _zoom / oldZoom);
        
        ZoomChanged?.Invoke(_zoom);
        InvalidateVisual();
    }
    
    public void Rotate(int degrees)
    {
        if (_bitmap == null) return;
        
        var transform = new RotateTransform(degrees);
        var rotated = new TransformedBitmap(_bitmap, transform);
        rotated.Freeze();
        _bitmap = rotated;
        ImageSource = _bitmap;
    }
    
    public void Flip(bool horizontal)
    {
        if (_bitmap == null) return;
        
        var transform = new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1);
        var flipped = new TransformedBitmap(_bitmap, transform);
        flipped.Freeze();
        _bitmap = flipped;
        ImageSource = _bitmap;
    }
    
    private void CenterImage()
    {
        if (_bitmap == null) return;
        _panOffset = new Point(
            (ActualWidth - _bitmap.PixelWidth * _zoom) / 2,
            (ActualHeight - _bitmap.PixelHeight * _zoom) / 2);
    }
    
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        
        if (_bitmap == null) return;
        
        var dest = new Rect(
            _panOffset.X, _panOffset.Y,
            _bitmap.PixelWidth * _zoom,
            _bitmap.PixelHeight * _zoom);
        
        var visible = new Rect(RenderSize);
        if (!dest.IntersectsWith(visible)) return;
        
        dc.PushClip(new RectangleGeometry(visible));
        
        RenderOptions.SetBitmapScalingMode(this,
            _zoom >= 8 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        
        dc.DrawImage(_bitmap, dest);
        
        if (_zoom >= 16)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)), 0.5);
            for (double x = dest.X; x < dest.Right; x += _zoom)
                dc.DrawLine(pen, new Point(x, dest.Top), new Point(x, dest.Bottom));
            for (double y = dest.Top; y < dest.Bottom; y += _zoom)
                dc.DrawLine(pen, new Point(dest.Left, y), new Point(dest.Right, y));
        }
        
        dc.Pop();
    }
    
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        SetZoom(_zoom * factor, mousePos: e.GetPosition(this));
    }
    
    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _lastMousePos = e.GetPosition(this);
        CaptureMouse();
        Focus();
    }
    
    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        ReleaseMouseCapture();
    }
    
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_isPanning)
        {
            _panOffset += pos - _lastMousePos;
            _lastMousePos = pos;
            InvalidateVisual();
        }

        if (_bitmap == null) return;

        var imgX = (int)((pos.X - _panOffset.X) / _zoom);
        var imgY = (int)((pos.Y - _panOffset.Y) / _zoom);

        if (imgX < 0 || imgY < 0 || imgX >= _bitmap.PixelWidth || imgY >= _bitmap.PixelHeight)
            return;

        var color = SampleVisiblePixel(imgX, imgY, out var alpha);
        PixelHovered?.Invoke(imgX, imgY, color, alpha);
    }

    /// <summary>
    /// Returns the color the eye actually sees at (x, y):
    /// source RGB composited onto <see cref="BackdropColor"/> through alpha,
    /// so RGB stored under transparent/hidden layer pixels is never reported.
    /// </summary>
    private Color SampleVisiblePixel(int x, int y, out byte alpha)
    {
        alpha = 255;
        var src = GetSampleSource();
        if (src == null)
            return BackdropColor;

        byte r, g, b, a;
        try
        {
            // 1×1 in the source's own format, then map channels correctly.
            var f = src.Format;
            int bpp = (f.BitsPerPixel + 7) / 8;
            if (bpp <= 0) bpp = 4;

            var buf = new byte[bpp];
            src.CopyPixels(new Int32Rect(x, y, 1, 1), buf, bpp, 0);

            if (!TryDecodePixel(f, buf, out r, out g, out b, out a))
                return BackdropColor;
        }
        catch
        {
            return BackdropColor;
        }

        alpha = a;

        // Fully opaque → raw color. Otherwise composite onto the backdrop
        // (what is visible through / around the layer's alpha).
        if (a == 255)
            return Color.FromRgb(r, g, b);

        var bg = BackdropColor;
        if (a == 0)
            return bg;

        double af = a / 255.0;
        double inv = 1.0 - af;
        return Color.FromRgb(
            (byte)Math.Clamp(r * af + bg.R * inv + 0.5, 0, 255),
            (byte)Math.Clamp(g * af + bg.G * inv + 0.5, 0, 255),
            (byte)Math.Clamp(b * af + bg.B * inv + 0.5, 0, 255));
    }

    /// <summary>
    /// BitmapSource for sampling. Converts to Bgra32 once when the native
    /// layout cannot be decoded reliably (rare formats).
    /// </summary>
    private BitmapSource? GetSampleSource()
    {
        if (_bitmap == null) return null;
        if (_sampleBitmap != null) return _sampleBitmap;

        var f = _bitmap.Format;
        bool known =
            f == PixelFormats.Bgr24 || f == PixelFormats.Bgr32 || f == PixelFormats.Bgra32 ||
            f == PixelFormats.Rgb24 || f == PixelFormats.Pbgra32 ||
            f == PixelFormats.Gray8 || f == PixelFormats.Gray16 ||
            f == PixelFormats.Bgr565 || f == PixelFormats.Bgr555 ||
            f == PixelFormats.BlackWhite || f == PixelFormats.Indexed1 ||
            f == PixelFormats.Indexed4 || f == PixelFormats.Indexed8;

        if (known)
            return _bitmap;

        try
        {
            var conv = new FormatConvertedBitmap(_bitmap, PixelFormats.Bgra32, null, 0);
            conv.Freeze();
            _sampleBitmap = conv;
        }
        catch
        {
            _sampleBitmap = _bitmap;
        }
        return _sampleBitmap;
    }

    private static bool TryDecodePixel(PixelFormat f, byte[] buf, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = 0;
        a = 255;

        if (f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32)
        {
            // BGRA, optionally premultiplied. Un-premultiply for the source
            // color; compositing below uses straight alpha.
            b = buf[0]; g = buf[1]; r = buf[2]; a = buf[3];
            if (f == PixelFormats.Pbgra32 && a is > 0 and < 255)
            {
                double af = a / 255.0;
                b = (byte)Math.Clamp(b / af + 0.5, 0, 255);
                g = (byte)Math.Clamp(g / af + 0.5, 0, 255);
                r = (byte)Math.Clamp(r / af + 0.5, 0, 255);
            }
            return true;
        }

        if (f == PixelFormats.Bgr32)
        {
            b = buf[0]; g = buf[1]; r = buf[2];
            return true;
        }

        if (f == PixelFormats.Bgr24)
        {
            b = buf[0]; g = buf[1]; r = buf[2];
            return true;
        }

        if (f == PixelFormats.Rgb24)
        {
            r = buf[0]; g = buf[1]; b = buf[2];
            return true;
        }

        if (f == PixelFormats.Gray8)
        {
            r = g = b = buf[0];
            return true;
        }

        if (f == PixelFormats.Gray16)
        {
            ushort v = (ushort)(buf[0] | (buf[1] << 8));
            r = g = b = (byte)(v >> 8);
            return true;
        }

        if (f == PixelFormats.Bgr565)
        {
            ushort v = (ushort)(buf[0] | (buf[1] << 8));
            int b5 = (v >> 0) & 0x1F;
            int g6 = (v >> 5) & 0x3F;
            int r5 = (v >> 11) & 0x1F;
            r = (byte)((r5 * 255 + 15) / 31);
            g = (byte)((g6 * 255 + 31) / 63);
            b = (byte)((b5 * 255 + 15) / 31);
            return true;
        }

        if (f == PixelFormats.Bgr555)
        {
            ushort v = (ushort)(buf[0] | (buf[1] << 8));
            int b5 = (v >> 0) & 0x1F;
            int g5 = (v >> 5) & 0x1F;
            int r5 = (v >> 10) & 0x1F;
            r = (byte)((r5 * 255 + 15) / 31);
            g = (byte)((g5 * 255 + 15) / 31);
            b = (byte)((b5 * 255 + 15) / 31);
            return true;
        }

        if (f == PixelFormats.BlackWhite)
        {
            r = g = b = (byte)((buf[0] & 0x80) != 0 ? 255 : 0);
            return true;
        }

        // Indexed formats: palette lookup would be needed; fall back to LUMA
        // approximation so we never return hidden layer RGB.
        if (f == PixelFormats.Indexed8)
        {
            r = g = b = buf[0];
            return true;
        }

        return false;
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        FitToWindow();
    }
}
