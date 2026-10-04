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
    public string? CameraSerial { get; init; }
    public string? LensMake { get; init; }
    public DateTime? DateTaken { get; init; }
    public string? ExposureTime { get; init; }
    public double? FNumber { get; init; }
    public string? ApertureDisplay { get; init; }
    public int? IsoSpeed { get; init; }
    public string? IsoDisplay { get; init; }
    public string? FocalLength { get; init; }
    public string? FocalLength35mm { get; init; }
    public string? ExposureBias { get; init; }
    public string? MaxAperture { get; init; }
    public string? MeteringMode { get; init; }
    public string? SubjectDistance { get; init; }
    public string? DigitalZoom { get; init; }
    public string? LightSource { get; init; }
    public string? Brightness { get; init; }
    public string? ProgramMode { get; init; }
    public string? LensModel { get; init; }
    public string? WhiteBalance { get; init; }
    public string? Flash { get; init; }
    public string? Software { get; init; }
    public string? Artist { get; init; }
    public string? Copyright { get; init; }
    public string? Title { get; init; }
    public string? Subject { get; init; }
    public string? Keywords { get; init; }
    public string? OrientationDisplay { get; init; }
    public int Orientation { get; init; } = 1;
    public string? SourceHint { get; init; }
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
        
        // Windows Property System first — same pipeline as Explorer's Details
        // pane (handlers reconcile EXIF/XMP/IPTC and format values for display).
        // MetadataExtractor fills gaps (formats without a Windows property handler,
        // or fields the handler left empty).
        var win = WindowsPropertyReader.Read(filePath);
        var exif = ExtractExif(filePath);

        string? Pick(string? windows, string? fallback) =>
            !string.IsNullOrWhiteSpace(windows) ? windows
            : !string.IsNullOrWhiteSpace(fallback) ? fallback
            : null;

        var orientation = win?.Orientation
            ?? ParseInt(exif.GetValueOrDefault("Orientation"))
            ?? 1;

        var sourceHint = win is { HasAnyValue: true }
            ? (win.CameraModel != null || win.ExposureTime != null || win.DateTaken != null
                ? "Windows Property System"
                : "Windows + MetadataExtractor")
            : "MetadataExtractor";

        return new ImageMetadata
        {
            FilePath = filePath,
            Width = width, Height = height,
            Format = format,
            FileSize = fi.Length,
            BitsPerPixel = bpp,
            HasAlpha = hasAlpha,
            ColorSpace = Pick(win?.ColorSpace, DetectColorSpace(filePath)) ?? "sRGB",
            IsHDR = DetectHDR(filePath),
            CameraMake = Pick(win?.CameraMake, exif.GetValueOrDefault("Make")),
            CameraModel = Pick(win?.CameraModel, exif.GetValueOrDefault("Model")),
            CameraSerial = Pick(win?.CameraSerial, exif.GetValueOrDefault("CameraSerial")),
            LensMake = Pick(win?.LensMake, exif.GetValueOrDefault("LensMake")),
            DateTaken = win?.DateTaken ?? ParseDate(exif.GetValueOrDefault("DateTaken")),
            ExposureTime = Pick(win?.ExposureTime, exif.GetValueOrDefault("ExposureTime")),
            FNumber = win?.FNumber ?? ParseDouble(exif.GetValueOrDefault("FNumber")),
            ApertureDisplay = Pick(win?.ApertureDisplay, FormatAperture(win?.FNumber ?? ParseDouble(exif.GetValueOrDefault("FNumber")))),
            IsoSpeed = win?.IsoSpeed ?? ParseInt(exif.GetValueOrDefault("ISO")),
            IsoDisplay = Pick(win?.IsoDisplay, FormatIso(win?.IsoSpeed ?? ParseInt(exif.GetValueOrDefault("ISO")))),
            FocalLength = Pick(win?.FocalLength, exif.GetValueOrDefault("FocalLength")),
            FocalLength35mm = Pick(win?.FocalLength35mm, exif.GetValueOrDefault("FocalLength35mm")),
            ExposureBias = Pick(win?.ExposureBias, exif.GetValueOrDefault("ExposureBias")),
            MaxAperture = Pick(win?.MaxAperture, exif.GetValueOrDefault("MaxAperture")),
            MeteringMode = Pick(win?.MeteringMode, exif.GetValueOrDefault("MeteringMode")),
            SubjectDistance = Pick(win?.SubjectDistance, exif.GetValueOrDefault("SubjectDistance")),
            DigitalZoom = Pick(win?.DigitalZoom, exif.GetValueOrDefault("DigitalZoom")),
            LightSource = Pick(win?.LightSource, exif.GetValueOrDefault("LightSource")),
            Brightness = Pick(win?.Brightness, exif.GetValueOrDefault("Brightness")),
            ProgramMode = Pick(win?.ProgramMode, exif.GetValueOrDefault("ProgramMode")),
            LensModel = Pick(win?.LensModel, exif.GetValueOrDefault("LensModel")),
            WhiteBalance = Pick(win?.WhiteBalance, exif.GetValueOrDefault("WhiteBalance")),
            Flash = Pick(win?.Flash, exif.GetValueOrDefault("Flash")),
            Software = Pick(win?.Software, exif.GetValueOrDefault("Software")),
            Artist = Pick(win?.Artist, exif.GetValueOrDefault("Artist")),
            Copyright = Pick(win?.Copyright, exif.GetValueOrDefault("Copyright")),
            Title = Pick(win?.Title, exif.GetValueOrDefault("Title")),
            Subject = Pick(win?.Subject, exif.GetValueOrDefault("Subject")),
            Keywords = Pick(win?.Keywords, exif.GetValueOrDefault("Keywords")),
            OrientationDisplay = Pick(win?.OrientationDisplay, null),
            Orientation = orientation,
            SourceHint = sourceHint,
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

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            var exifDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifDir != null)
            {
                if (exifDir.TryGetDouble(ExifDirectoryBase.TagExposureTime, out double et))
                    result["ExposureTime"] = FormatExposureTime(et);

                if (exifDir.TryGetDouble(ExifDirectoryBase.TagFNumber, out double fn))
                    result["FNumber"] = fn.ToString("F1");

                if (exifDir.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out int iso) && iso > 0)
                    result["ISO"] = iso.ToString();

                if (exifDir.TryGetDouble(ExifDirectoryBase.TagFocalLength, out double fl) && fl > 0)
                    result["FocalLength"] = fl.ToString("0.#") + " mm";

                if (exifDir.TryGetInt32(ExifDirectoryBase.Tag35MMFilmEquivFocalLength, out int fl35) && fl35 > 0)
                    result["FocalLength35mm"] = fl35.ToString() + " mm";

                if (exifDir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dt))
                    result["DateTaken"] = dt.ToString("yyyy:MM:dd HH:mm:ss");

                if (exifDir.TryGetInt32(ExifDirectoryBase.TagFlash, out int flash))
                    result["Flash"] = (flash & 1) == 1 ? "Flash fired" : "Flash did not fire";

                if (exifDir.TryGetInt32(ExifDirectoryBase.TagOrientation, out int orient))
                    result["Orientation"] = orient.ToString();

                if (exifDir.TryGetInt32(ExifDirectoryBase.TagWhiteBalance, out int wb))
                    result["WhiteBalance"] = wb == 1 ? "Manual" : "Auto";

                if (exifDir.TryGetDouble(ExifDirectoryBase.TagExposureBias, out double ev))
                    result["ExposureBias"] = (ev >= 0 ? "+" : "") + ev.ToString("0.0") + " EV";

                if (exifDir.TryGetDouble(ExifDirectoryBase.TagMaxAperture, out double maxAp))
                    result["MaxAperture"] = "f/" + Math.Pow(2, maxAp / 2.0).ToString("0.#");

                if (exifDir.TryGetInt32(ExifDirectoryBase.TagMeteringMode, out int metering))
                    result["MeteringMode"] = metering switch
                    {
                        1 => "Average",
                        2 => "CenterWeightedAverage",
                        3 => "Spot",
                        4 => "MultiSpot",
                        5 => "Pattern",
                        6 => "Partial",
                        255 => "Other",
                        _ => metering.ToString()
                    };

                if (exifDir.TryGetDouble(ExifDirectoryBase.TagSubjectDistance, out double dist) && dist > 0)
                    result["SubjectDistance"] = dist.ToString("0.##") + " m";

                if (exifDir.TryGetDouble(ExifDirectoryBase.TagDigitalZoomRatio, out double zoom) && zoom > 0)
                    result["DigitalZoom"] = zoom.ToString("0.##") + "x";

                if (exifDir.TryGetInt32(0x9208, out int light)) // LightSource (0x9208)
                    result["LightSource"] = light switch
                    {
                        0 => "Auto",
                        1 => "Daylight",
                        2 => "Fluorescent",
                        3 => "Tungsten",
                        10 => "Cloudy",
                        11 => "Shade",
                        _ => light.ToString()
                    };

                if (exifDir.TryGetDouble(ExifDirectoryBase.TagBrightnessValue, out double bv))
                    result["Brightness"] = bv.ToString("0.##");

                if (exifDir.TryGetInt32(ExifDirectoryBase.TagExposureProgram, out int prog))
                    result["ProgramMode"] = prog switch
                    {
                        1 => "Manual",
                        2 => "Program AE",
                        3 => "Aperture Priority",
                        4 => "Shutter Priority",
                        5 => "Creative",
                        6 => "Action",
                        7 => "Portrait",
                        8 => "Landscape",
                        _ => prog.ToString()
                    };

                var lens = exifDir.GetDescription(ExifDirectoryBase.TagLensModel);
                if (!string.IsNullOrEmpty(lens))
                    result["LensModel"] = lens;

                var lensMake = exifDir.GetDescription(ExifDirectoryBase.TagLensMake);
                if (!string.IsNullOrEmpty(lensMake))
                    result["LensMake"] = lensMake;

                var camOwner = exifDir.GetDescription(ExifDirectoryBase.TagCameraOwnerName);
                if (!string.IsNullOrEmpty(camOwner))
                    result["CameraOwner"] = camOwner;

                var camSerial = exifDir.GetDescription(ExifDirectoryBase.TagBodySerialNumber);
                if (!string.IsNullOrEmpty(camSerial))
                    result["CameraSerial"] = camSerial;
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

                var artist = ifd0Dir.GetDescription(ExifDirectoryBase.TagArtist);
                if (!string.IsNullOrEmpty(artist)) result["Artist"] = artist;

                var copyright = ifd0Dir.GetDescription(ExifDirectoryBase.TagCopyright);
                if (!string.IsNullOrEmpty(copyright)) result["Copyright"] = copyright;

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

            // IPTC / XMP common fields (title, subject, keywords)
            foreach (var dir in directories)
            {
                foreach (var tag in dir.Tags)
                {
                    if (string.IsNullOrEmpty(tag.Description))
                        continue;

                    if (tag.Name is "Object Name" or "Title" or "Headline")
                        result.TryAdd("Title", tag.Description);
                    else if (tag.Name is "Caption/Abstract" or "Description" or "Image Description" or "Description")
                        result.TryAdd("Subject", tag.Description);
                    else if (tag.Name is "Keywords" or "Keyword" or "Subject")
                        result.TryAdd("Keywords", tag.Description);
                }
            }

            ExtractLensInfo(directories, result);
        }
        catch { }
        return result;
    }
    
    private static void ExtractLensInfo(IReadOnlyList<MetadataExtractor.Directory> directories, Dictionary<string, string> result)
    {
        // Search all directories for lens-related tags
        foreach (var dir in directories)
        {
            foreach (var tag in dir.Tags)
            {
                var name = tag.Name?.ToLowerInvariant() ?? "";
                if ((name.Contains("lens") || name.Contains("镜头")) && !string.IsNullOrEmpty(tag.Description))
                {
                    result["LensModel"] = tag.Description;
                    return;
                }
            }
        }
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

    /// <summary>Matches Windows PSFormatForDisplay output for System.Photo.ExposureTime.</summary>
    private static string FormatExposureTime(double seconds)
    {
        if (seconds <= 0) return "";
        if (seconds < 1)
        {
            var denom = (int)Math.Round(1.0 / seconds);
            return denom > 0 ? $"1/{denom} sec" : $"{seconds:0.###} sec";
        }
        return $"{seconds:0.###} sec";
    }

    /// <summary>Matches Windows PSFormatForDisplay output for System.Photo.FNumber.</summary>
    private static string? FormatAperture(double? fNumber) =>
        fNumber is > 0 ? $"f/{fNumber:0.#}" : null;

    /// <summary>Matches Windows PSFormatForDisplay output for System.Photo.ISOSpeed.</summary>
    private static string? FormatIso(int? iso) =>
        iso is > 0 ? $"ISO-{iso}" : null;
}
