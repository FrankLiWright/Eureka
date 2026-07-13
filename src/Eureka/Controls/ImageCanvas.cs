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
    private double _zoom = 1.0;
    private Point _panOffset;
    private Point _lastMousePos;
    private bool _isPanning;
    
    public const double MaxZoom = 10.0; // 1000%
    public const double MinZoom = 0.01; // 1%
    
    // Dependency properties
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(BitmapSource), typeof(ImageCanvas),
            new PropertyMetadata(null, OnImageChanged));
    
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(ImageCanvas),
            new PropertyMetadata(1.0));
    
    // Events
    public event Action<double>? ZoomChanged;
    public event Action<int, int, Color>? PixelHovered;
    
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
        c.FitToWindow();
        c.InvalidateVisual();
    }
    
    public void FitToWindow()
    {
        if (_bitmap == null || ActualWidth == 0 || ActualHeight == 0) return;
        
        var sx = ActualWidth / _bitmap.PixelWidth;
        var sy = ActualHeight / _bitmap.PixelHeight;
        _zoom = Math.Min(sx, sy) * 0.95; // 95% to leave some margin
        
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
        
        // Check if image is visible
        var visible = new Rect(RenderSize);
        if (!dest.IntersectsWith(visible)) return;
        
        dc.PushClip(new RectangleGeometry(visible));
        
        // Use high quality for normal zoom, nearest neighbor for pixel art
        RenderOptions.SetBitmapScalingMode(this, 
            _zoom >= 8 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        
        dc.DrawImage(_bitmap, dest);
        
        // Draw pixel grid at high zoom
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
        
        // Update pixel info
        if (_bitmap != null)
        {
            var imgX = (int)((pos.X - _panOffset.X) / _zoom);
            var imgY = (int)((pos.Y - _panOffset.Y) / _zoom);
            
            if (imgX >= 0 && imgY >= 0 && imgX < _bitmap.PixelWidth && imgY < _bitmap.PixelHeight)
            {
                var pixels = new byte[4];
                _bitmap.CopyPixels(new Int32Rect(imgX, imgY, 1, 1), pixels, 4, 0);
                var color = Color.FromRgb(pixels[2], pixels[1], pixels[0]);
                PixelHovered?.Invoke(imgX, imgY, color);
            }
        }
    }
    
    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        FitToWindow();
    }
}
