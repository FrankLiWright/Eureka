using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Eureka;

public sealed class ImageMetadata
{
    public string FilePath { get; init; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public int Width { get; init; }
    public int Height { get; init; }
    public string Format { get; init; } = "";
    public long FileSize { get; init; }
    public int BitsPerPixel { get; init; }
    public bool HasAlpha { get; init; }
    public string ColorSpace { get; init; } = "sRGB";
    public string? ColorProfile { get; init; }
    public bool IsHDR { get; init; }
    
    public string? CameraMake { get; init; }
    public string? CameraModel { get; init; }
    public DateTime? DateTaken { get; init; }
    public string? ExposureTime { get; init; }
    public double? FNumber { get; init; }
    public int? IsoSpeed { get; init; }
    public string? FocalLength { get; init; }
    public string? LensModel { get; init; }
    public string? WhiteBalance { get; init; }
    public string? Flash { get; init; }
    public string? Software { get; init; }
    public string? Artist { get; init; }
    public string? Copyright { get; init; }
    public int Orientation { get; init; } = 1;
}

public sealed class ImageDecoder
{
    public ImageMetadata LoadMetadata(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Image not found", filePath);
        
        var fi = new FileInfo(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var format = DetectFormat(filePath);
        
        int width = 0, height = 0, bpp = 0;
        bool hasAlpha = false;
        
        if (IsRawFormat(ext))
        {
            try
            {
                using var image = new MagickImage();
                image.Ping(filePath); // Ping模式只读取元数据，速度更快
                width = image.Width;
                height = image.Height;
                bpp = 16; // RAW通常是16位
            }
            catch
            {
                var (w, h) = GetDimensionsFallback(filePath);
                width = w;
                height = h;
            }
        }
        else
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                bpp = frame.Format.BitsPerPixel;
                hasAlpha = bpp == 32 || bpp == 64 || bpp == 128;
            }
            catch
            {
                var (w, h) = GetDimensionsFallback(filePath);
                width = w;
                height = h;
            }
        }
        
        // EXIF数据在后台线程读取，不阻塞主线程
        var exif = ExtractExif(filePath);
        
        return new ImageMetadata
        {
            FilePath = filePath,
            Width = width, Height = height,
            Format = format,
            FileSize = fi.Length,
            BitsPerPixel = bpp,
            HasAlpha = hasAlpha,
            ColorSpace = DetectColorSpace(filePath),
            IsHDR = DetectHDR(filePath),
            CameraMake = exif.GetValueOrDefault("Make"),
            CameraModel = exif.GetValueOrDefault("Model"),
            DateTaken = ParseDate(exif.GetValueOrDefault("DateTaken")),
            ExposureTime = exif.GetValueOrDefault("ExposureTime"),
            FNumber = ParseDouble(exif.GetValueOrDefault("FNumber")),
            IsoSpeed = ParseInt(exif.GetValueOrDefault("ISO")),
            FocalLength = exif.GetValueOrDefault("FocalLength"),
            LensModel = exif.GetValueOrDefault("LensModel"),
            WhiteBalance = exif.GetValueOrDefault("WhiteBalance"),
            Flash = exif.GetValueOrDefault("Flash"),
            Software = exif.GetValueOrDefault("Software"),
            Orientation = ParseInt(exif.GetValueOrDefault("Orientation")) ?? 1
        };
    }
    
    public BitmapSource? DecodeImage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (IsStandardFormat(ext))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(filePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        
        if (ext is ".avif" or ".heif" or ".heic")
        {
            return DecodeWithMagick(filePath);
        }
        
        if (ext is ".jxl")
        {
            return DecodeWithImageSharp(filePath);
        }
        
        if (IsRawFormat(ext))
        {
            return DecodeWithMagick(filePath);
        }
        
        try
        {
            using var stream = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch
        {
            throw new NotSupportedException($"Cannot decode format: {ext}");
        }
    }
    
    public BitmapSource? ConvertHdrToSdr(BitmapSource hdrBitmap)
    {
        var width = hdrBitmap.PixelWidth;
        var height = hdrBitmap.PixelHeight;
        
        // Handle float HDR formats (Rgba128Float, Rgba64)
        if (hdrBitmap.Format == PixelFormats.Rgba128Float || hdrBitmap.Format == PixelFormats.Rgba64)
        {
            var stride = width * 4;
            var pixels = new float[width * height * 4];
            hdrBitmap.CopyPixels(pixels, stride, 0);
            
            var sdrPixels = new byte[width * height * 4];
            
            for (int i = 0; i < pixels.Length; i += 4)
            {
                var r = pixels[i];
                var g = pixels[i + 1];
                var b = pixels[i + 2];
                
                var lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                var mappedLum = lum / (1.0f + lum);
                var scale = mappedLum / Math.Max(lum, 0.001f);
                
                sdrPixels[i] = (byte)(Math.Clamp(Math.Pow(r * scale, 1.0 / 2.2) * 255, 0, 255));
                sdrPixels[i + 1] = (byte)(Math.Clamp(Math.Pow(g * scale, 1.0 / 2.2) * 255, 0, 255));
                sdrPixels[i + 2] = (byte)(Math.Clamp(Math.Pow(b * scale, 1.0 / 2.2) * 255, 0, 255));
                sdrPixels[i + 3] = 255;
            }
            
            var sdrBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, sdrPixels, width * 4);
            sdrBitmap.Freeze();
            return sdrBitmap;
        }
        
        // Handle 8-bit formats (from HEIC etc.) - apply SDR clipping curve
        if (hdrBitmap.Format == PixelFormats.Bgra32)
        {
            var stride = width * 4;
            var pixels = new byte[width * height * 4];
            hdrBitmap.CopyPixels(pixels, stride, 0);
            
            // Apply aggressive SDR curve (clip highlights, crush shadows)
            for (int i = 0; i < pixels.Length; i += 4)
            {
                var r = pixels[i] / 255.0;
                var g = pixels[i + 1] / 255.0;
                var b = pixels[i + 2] / 255.0;
                
                // SDR curve: more contrast, less DR
                r = Math.Pow(r, 1.2) * 1.1 - 0.05;
                g = Math.Pow(g, 1.2) * 1.1 - 0.05;
                b = Math.Pow(b, 1.2) * 1.1 - 0.05;
                
                pixels[i] = (byte)(Math.Clamp(r * 255, 0, 255));
                pixels[i + 1] = (byte)(Math.Clamp(g * 255, 0, 255));
                pixels[i + 2] = (byte)(Math.Clamp(b * 255, 0, 255));
            }
            
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        
        return null;
    }
    
    private static BitmapSource? DecodeWithImageSharp(string filePath)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(filePath);
            
            var width = image.Width;
            var height = image.Height;
            var pixels = new byte[width * height * 4];
            
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var src = row[x];
                        var dstOffset = (y * width + x) * 4;
                        pixels[dstOffset] = src.B;
                        pixels[dstOffset + 1] = src.G;
                        pixels[dstOffset + 2] = src.R;
                        pixels[dstOffset + 3] = src.A;
                    }
                }
            });
            
            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
    
    private static BitmapSource? DecodeWithMagick(string filePath)
    {
        try
        {
            using var image = new MagickImage();
            
            image.Read(filePath);
            
            image.ColorSpace = ColorSpace.sRGB;
            
            image.Depth = 8;
            
            image.FilterType = FilterType.Lanczos;
            image.Settings.Interlace = Interlace.NoInterlace;
            
            var width = image.Width;
            var height = image.Height;
            
            using var pixelsCollection = image.GetPixels();
            var pixelArray = pixelsCollection.ToByteArray(PixelMapping.BGRA);
            
            if (pixelArray == null) return null;
            
            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixelArray, width * 4);
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
    
    private static string DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "JPEG",
            ".png" => "PNG",
            ".bmp" => "BMP",
            ".gif" => "GIF",
            ".tiff" or ".tif" => "TIFF",
            ".webp" => "WebP",
            ".ico" => "ICO",
            ".avif" => "AVIF",
            ".heif" or ".heic" => "HEIF",
            ".jxl" => "JPEG XL",
            ".qoi" => "QOI",
            ".psd" => "Photoshop",
            ".cr2" or ".cr3" => "Canon RAW",
            ".nef" or ".nrw" => "Nikon RAW",
            ".arw" or ".srf" or ".sr2" => "Sony RAW",
            ".orf" => "Olympus RAW",
            ".rw2" => "Panasonic RAW",
            ".raf" => "Fujifilm RAW",
            ".dng" => "Adobe DNG",
            ".pef" => "Pentax RAW",
            ".raw" or ".rwl" or ".rwz" => "RAW",
            _ => ext.TrimStart('.').ToUpper()
        };
    }
    
    private static bool IsStandardFormat(string ext)
    {
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" or ".tif" or ".webp" or ".ico";
    }
    
    private static bool IsRawFormat(string ext)
    {
        return ext is ".cr2" or ".cr3" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" 
            or ".orf" or ".rw2" or ".raf" or ".dng" or ".pef" or ".raw" or ".rwl" or ".rwz";
    }
    
    private static string DetectColorSpace(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".avif" or ".heif" or ".heic")
            return "BT.2020 / PQ";
        return "sRGB";
    }
    
    private static bool DetectHDR(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".avif" or ".heif" or ".heic")
        {
            try
            {
                var dirs = MetadataExtractor.ImageMetadataReader.ReadMetadata(filePath);
                foreach (var dir in dirs)
                {
                    if (dir.ContainsTag(0x00A0)) // PixelXDimension
                    {
                    }
                    foreach (var tag in dir.Tags)
                    {
                        var name = tag.Name?.ToLower() ?? "";
                        if (name.Contains("bit") || name.Contains("depth"))
                        {
                            if (int.TryParse(tag.Description?.Replace(" bits", ""), out int bits) && bits > 8)
                                return true;
                        }
                    }
                }
                using var image = new MagickImage();
                image.Ping(filePath);
                return image.Depth > 8;
            }
            catch { }
        }
        return false;
    }
    
    private static Dictionary<string, string> ExtractExif(string filePath)
    {
        var result = new Dictionary<string, string>();
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (IsRawFormat(ext))
        {
            return ExtractExifFromRaw(filePath);
        }

        if (ext is ".avif" or ".heif" or ".heic")
        {
            return ExtractExifFromRaw(filePath);
        }
        
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(filePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            
            var meta = bmp.Metadata as BitmapMetadata;
            if (meta == null) return result;
            
            TryAdd(result, meta, "System.Photo.CameraManufacturer", "Make");
            TryAdd(result, meta, "System.Photo.CameraModel", "Model");
            TryAdd(result, meta, "System.Photo.DateTaken", "DateTaken");
            TryAdd(result, meta, "System.Photo.ExposureTime", "ExposureTime");
            TryAdd(result, meta, "System.Photo.FNumber", "FNumber");
            TryAdd(result, meta, "System.Photo.ISOSpeed", "ISO");
            TryAdd(result, meta, "System.Photo.FocalLength", "FocalLength");
            TryAdd(result, meta, "System.Photo.LensModel", "LensModel");
            TryAdd(result, meta, "System.Photo.WhiteBalance", "WhiteBalance");
            TryAdd(result, meta, "System.Photo.Flash", "Flash");
            TryAdd(result, meta, "System.Software.ProductName", "Software");
            
            try
            {
                var orient = meta.GetQuery("/app1/ifd/{ushort=274}");
                if (orient != null) result["Orientation"] = orient.ToString();
            }
            catch { }
        }
        catch { }
        return result;
    }
    
    private static Dictionary<string, string> ExtractExifFromRaw(string filePath)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            
            var exifDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifDir != null)
            {
                if (exifDir.TryGetDouble(ExifDirectoryBase.TagExposureTime, out double et))
                    result["ExposureTime"] = et < 1 ? $"1/{(int)(1.0 / et)}" : et.ToString("F1");
                
                if (exifDir.TryGetDouble(ExifDirectoryBase.TagFNumber, out double fn))
                    result["FNumber"] = fn.ToString("F1");
                
                if (exifDir.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out int iso) && iso > 0)
                    result["ISO"] = iso.ToString();
                
                if (exifDir.TryGetDouble(ExifDirectoryBase.TagFocalLength, out double fl) && fl > 0)
                    result["FocalLength"] = ((int)fl).ToString() + "mm";
                
                if (exifDir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dt))
                    result["DateTaken"] = dt.ToString("yyyy:MM:dd HH:mm:ss");
                
                if (exifDir.TryGetInt32(ExifDirectoryBase.TagFlash, out int flash))
                    result["Flash"] = (flash & 1) == 1 ? "Fired" : "No Flash";
                
                if (exifDir.TryGetInt32(ExifDirectoryBase.TagOrientation, out int orient))
                    result["Orientation"] = orient.ToString();
            }
            
            var ifd0Dir = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0Dir != null)
            {
                var make = ifd0Dir.GetDescription(ExifDirectoryBase.TagMake);
                if (!string.IsNullOrEmpty(make)) result["Make"] = make;
                
                var model = ifd0Dir.GetDescription(ExifDirectoryBase.TagModel);
                if (!string.IsNullOrEmpty(model)) result["Model"] = model;
                
                var software = ifd0Dir.GetDescription(ExifDirectoryBase.TagSoftware);
                if (!string.IsNullOrEmpty(software)) result["Software"] = software;
                
                if (!result.ContainsKey("Orientation"))
                {
                    if (ifd0Dir.TryGetInt32(ExifDirectoryBase.TagOrientation, out int orient))
                        result["Orientation"] = orient.ToString();
                }
                
                if (!result.ContainsKey("DateTaken"))
                {
                    if (ifd0Dir.TryGetDateTime(ExifDirectoryBase.TagDateTime, out DateTime dt))
                        result["DateTaken"] = dt.ToString("yyyy:MM:dd HH:mm:ss");
                }
            }
            
            var canonDir = directories.OfType<MetadataExtractor.Formats.Exif.Makernotes.CanonMakernoteDirectory>().FirstOrDefault();
            if (canonDir != null)
            {
                var lens = canonDir.GetDescription(MetadataExtractor.Formats.Exif.Makernotes.CanonMakernoteDirectory.TagLensModel);
                if (!string.IsNullOrEmpty(lens)) result["LensModel"] = lens;
            }
        }
        catch { }
        return result;
    }
    

    
    private static void TryAdd(Dictionary<string, string> d, BitmapMetadata m, string q, string k)
    {
        try { var v = m.GetQuery(q); if (v != null) d[k] = v.ToString() ?? ""; } catch { }
    }
    
    private static (int, int) GetDimensionsFallback(string filePath)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(filePath);
            return (info?.Width ?? 0, info?.Height ?? 0);
        }
        catch
        {
            return (0, 0);
        }
    }
    
    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(s, out d)) return d;
        return null;
    }
    
    private static double? ParseDouble(string? s) => double.TryParse(s, out var d) ? d : null;
    private static int? ParseInt(string? s) => int.TryParse(s, out var i) ? i : null;
}
