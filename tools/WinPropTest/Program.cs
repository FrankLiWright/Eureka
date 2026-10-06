using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

// Verifies color picking returns the visible composited color, never
// RGB hidden under transparent layer pixels (WebP-style alpha).

var outDir = Path.Combine(Path.GetTempPath(), "eureka-picker-test");
Directory.CreateDirectory(outDir);
var webpPath = Path.Combine(outDir, "hidden-layer.webp");

// Build a 2×2 WebP:
//   (0,0) opaque red
//   (1,0) transparent (A=0) but RGB = green  ← "hidden layer" pixel
//   (0,1) half-transparent blue (A=128)
//   (1,1) opaque white
using (var img = new MagickImage(MagickColors.Transparent, 2, 2))
{
    img.ColorSpace = ColorSpace.sRGB;

    // Straight RGBA via byte-based color ctor (Q16-safe).
    Paint(img, 0, 0, MagickColor.FromRgba(255, 0, 0, 255));       // opaque red
    Paint(img, 1, 0, MagickColor.FromRgba(0, 255, 0, 0));         // hidden green under A=0
    Paint(img, 0, 1, MagickColor.FromRgba(0, 0, 255, 128));       // 50% blue
    Paint(img, 1, 1, MagickColor.FromRgba(255, 255, 255, 255));   // opaque white

    // Lossless WebP — lossy destroys a 2×2 test pattern.
    img.Format = MagickFormat.WebP;
    img.Settings.SetDefine("webp:lossless", "true");
    img.Settings.SetDefine("webp:exact", "true");
    img.Write(webpPath);
}

Console.WriteLine($"Wrote test image: {webpPath}");

// Load Eureka assembly
var dllPath = Path.GetFullPath(@"src\Eureka\bin\Debug\net8.0-windows\win-x64\Eureka.dll");
if (!File.Exists(dllPath))
    dllPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        @"..\..\..\..\..\src\Eureka\bin\Debug\net8.0-windows\win-x64\Eureka.dll"));

var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
var decoderType = asm.GetType("Eureka.ImageDecoder")!;
var decoder = Activator.CreateInstance(decoderType)!;
var decode = decoderType.GetMethod("DecodeImage", BindingFlags.Public | BindingFlags.Instance)!;

var bmp = (BitmapSource?)decode.Invoke(decoder, new object[] { webpPath });
if (bmp == null)
{
    Console.WriteLine("FAIL: DecodeImage returned null");
    return 1;
}

Console.WriteLine($"Decoded: {bmp.PixelWidth}x{bmp.PixelHeight} format={bmp.Format}");
if (bmp.PixelWidth < 2 || bmp.PixelHeight < 2)
{
    Console.WriteLine("FAIL: unexpected dimensions");
    return 1;
}

// Sample like ImageCanvas does: 1×1 CopyPixels + channel map + alpha composite.
var backdrop = Color.FromRgb(0x1E, 0x1E, 0x1E);

bool fail = false;
void Check(string name, int x, int y, byte expR, byte expG, byte expB, byte expA)
{
    var (r, g, b, a) = Sample(bmp, x, y);
    var visible = Composite(r, g, b, a, backdrop);
    var ok = a == expA
        && Math.Abs(visible.R - expR) <= 2
        && Math.Abs(visible.G - expG) <= 2
        && Math.Abs(visible.B - expB) <= 2;

    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: raw=({r},{g},{b},A={a}) visible=#{visible.R:X2}{visible.G:X2}{visible.B:X2} expected=#{expR:X2}{expG:X2}{expB:X2} A={expA}");
    if (!ok) fail = true;
}

// (0,0) opaque red — raw and visible must both be red
Check("opaque red", 0, 0, 0xFF, 0x00, 0x00, 255);

// (1,0) hidden green under A=0 — visible must be backdrop, NOT green
Check("hidden green → backdrop", 1, 0, backdrop.R, backdrop.G, backdrop.B, 0);

// (0,1) 50% blue over dark backdrop — blended, not pure blue
{
    var (r, g, b, a) = Sample(bmp, 0, 1);
    var visible = Composite(r, g, b, a, backdrop);
    bool blueish = visible.B > 80 && visible.B < 160 && a == 128;
    Console.WriteLine($"{(blueish ? "PASS" : "FAIL")} half blue: raw=({r},{g},{b},A={a}) visible=#{visible.R:X2}{visible.G:X2}{visible.B:X2}");
    if (!blueish) fail = true;
}

// (1,1) opaque white
Check("opaque white", 1, 1, 0xFF, 0xFF, 0xFF, 255);

Console.WriteLine(fail ? "\nRESULT: FAIL" : "\nRESULT: PASS");
return fail ? 1 : 0;

static void Paint(MagickImage img, int x, int y, MagickColor color)
{
    new Drawables().FillColor(color).Rectangle(x, y, x, y).Draw(img);
}

static (byte r, byte g, byte b, byte a) Sample(BitmapSource bmp, int x, int y)
{
    var f = bmp.Format;
    int bpp = (f.BitsPerPixel + 7) / 8;
    var buf = new byte[bpp];
    bmp.CopyPixels(new Int32Rect(x, y, 1, 1), buf, bpp, 0);

    byte r = 0, g = 0, b = 0, a = 255;
    if (f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32)
    {
        b = buf[0]; g = buf[1]; r = buf[2]; a = buf[3];
    }
    else if (f == PixelFormats.Bgr32)
    {
        b = buf[0]; g = buf[1]; r = buf[2];
    }
    else if (f == PixelFormats.Bgr24)
    {
        b = buf[0]; g = buf[1]; r = buf[2];
    }
    else if (f == PixelFormats.Rgb24)
    {
        r = buf[0]; g = buf[1]; b = buf[2];
    }
    return (r, g, b, a);
}

static Color Composite(byte r, byte g, byte b, byte a, Color bg)
{
    if (a == 255) return Color.FromRgb(r, g, b);
    if (a == 0) return bg;
    double af = a / 255.0, inv = 1 - af;
    return Color.FromRgb(
        (byte)Math.Clamp(r * af + bg.R * inv + 0.5, 0, 255),
        (byte)Math.Clamp(g * af + bg.G * inv + 0.5, 0, 255),
        (byte)Math.Clamp(b * af + bg.B * inv + 0.5, 0, 255));
}
