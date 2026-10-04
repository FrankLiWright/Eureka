using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Eureka;

/// <summary>
/// Reads photo metadata through the Windows Property System — the same
/// schema handlers Explorer's Details pane uses (Shell item ExtendedProperty).
/// Values are formatted to match Explorer (e.g. "1/500 sec", "f/4", "ISO-1000").
/// </summary>
public static class WindowsPropertyReader
{
    public sealed class Result
    {
        public string? CameraMake { get; set; }
        public string? CameraModel { get; set; }
        public string? CameraSerial { get; set; }
        public string? LensMake { get; set; }
        public string? LensModel { get; set; }
        public DateTime? DateTaken { get; set; }
        public string? ExposureTime { get; set; }
        public double? FNumber { get; set; }
        public string? ApertureDisplay { get; set; }
        public int? IsoSpeed { get; set; }
        public string? IsoDisplay { get; set; }
        public string? FocalLength { get; set; }
        public string? FocalLength35mm { get; set; }
        public string? ExposureBias { get; set; }
        public string? MaxAperture { get; set; }
        public string? MeteringMode { get; set; }
        public string? SubjectDistance { get; set; }
        public string? DigitalZoom { get; set; }
        public string? LightSource { get; set; }
        public string? Brightness { get; set; }
        public string? WhiteBalance { get; set; }
        public string? Flash { get; set; }
        public string? ProgramMode { get; set; }
        public string? OrientationDisplay { get; set; }
        public int? Orientation { get; set; }
        public string? Software { get; set; }
        public string? Artist { get; set; }
        public string? Copyright { get; set; }
        public string? Title { get; set; }
        public string? Subject { get; set; }
        public string? Keywords { get; set; }
        public int? BitDepth { get; set; }
        public string? ColorSpace { get; set; }
        public string? Dimensions { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? ItemType { get; set; }
        public string? Compression { get; set; }
        public double? XResolution { get; set; }
        public double? YResolution { get; set; }

        public bool HasAnyValue =>
            CameraMake != null || CameraModel != null || ExposureTime != null
            || FNumber != null || IsoSpeed != null || DateTaken != null || LensModel != null
            || Width != null || BitDepth != null || Orientation != null
            || Dimensions != null || Software != null;
    }

    /// <summary>
    /// Reads Windows shell properties for the file. Returns null when the
    /// shell cannot be reached (caller falls back to raw parsers).
    /// </summary>
    public static Result? Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        Result? result = null;
        Exception? failure = null;

        // Shell.Application is apartment-threaded; keep the call on a dedicated STA thread.
        var thread = new Thread(() =>
        {
            try
            {
                result = ReadCore(filePath);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        return failure == null ? result : null;
    }

    private static Result? ReadCore(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath);
        if (dir == null || name.Length == 0)
            return null;

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null)
            return null;

        object? shell = null;
        object? folder = null;
        object? item = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null)
                return null;

            folder = Invoke(shell, "Namespace", dir);
            if (folder == null)
                return null;

            item = Invoke(folder, "ParseName", name);
            if (item == null)
                return null;

            string? Text(string key) => AsString(GetProp(item, key));
            int? Int(string key) => AsInt(GetProp(item, key));
            double? Dbl(string key) => AsDouble(GetProp(item, key));
            DateTime? Date(string key) => AsDate(GetProp(item, key));

            var exposure = Dbl("System.Photo.ExposureTime");
            var fNumber = Dbl("System.Photo.FNumber");
            var iso = Int("System.Photo.ISOSpeed") ?? Int("System.Photo.PhotographicSensitivity");
            var focal = Dbl("System.Photo.FocalLength");
            var maxAperture = Dbl("System.Photo.MaxAperture");
            var orientation = Int("System.Photo.Orientation");

            // System.Photo.Orientation is an enum in some handlers and a number in others.
            if (orientation == null)
            {
                var oText = Text("System.Photo.Orientation");
                if (int.TryParse(oText, out var o))
                    orientation = o;
            }

            return new Result
            {
                CameraMake = Text("System.Photo.CameraManufacturer"),
                CameraModel = Text("System.Photo.CameraModel"),
                CameraSerial = Text("System.Photo.CameraSerialNumber"),
                LensMake = Text("System.Photo.LensManufacturer"),
                LensModel = Text("System.Photo.LensModel"),
                DateTaken = Date("System.Photo.DateTaken"),
                ExposureTime = FormatExposureTime(exposure) ?? Text("System.Photo.ExposureTime"),
                FNumber = fNumber,
                ApertureDisplay = FormatAperture(fNumber) ?? FormatAperture(maxAperture) ?? Text("System.Photo.FNumber"),
                IsoSpeed = iso,
                IsoDisplay = FormatIso(iso),
                FocalLength = FormatMm(focal) ?? Text("System.Photo.FocalLength"),
                FocalLength35mm = FormatMm(Dbl("System.Photo.FocalLengthInFilm")) ?? Text("System.Photo.FocalLengthInFilm"),
                ExposureBias = FormatEv(Dbl("System.Photo.ExposureBias")) ?? Text("System.Photo.ExposureBias"),
                MaxAperture = FormatAperture(maxAperture) ?? Text("System.Photo.MaxAperture"),
                MeteringMode = MapMetering(Int("System.Photo.MeteringMode")) ?? Text("System.Photo.MeteringMode"),
                SubjectDistance = FormatMeters(Dbl("System.Photo.SubjectDistance")) ?? Text("System.Photo.SubjectDistance"),
                DigitalZoom = FormatRatio(Dbl("System.Photo.DigitalZoomRatio")) ?? Text("System.Photo.DigitalZoomRatio"),
                LightSource = MapLightSource(Int("System.Photo.LightSource")) ?? Text("System.Photo.LightSource"),
                Brightness = FormatNum(Dbl("System.Photo.BrightnessValue")) ?? Text("System.Photo.BrightnessValue"),
                WhiteBalance = MapWhiteBalance(Int("System.Photo.WhiteBalance")) ?? Text("System.Photo.WhiteBalance"),
                Flash = MapFlash(Int("System.Photo.Flash")) ?? Text("System.Photo.Flash"),
                ProgramMode = MapProgram(Int("System.Photo.ProgramMode")) ?? Text("System.Photo.ProgramMode"),
                OrientationDisplay = MapOrientation(orientation) ?? Text("System.Photo.Orientation"),
                Orientation = orientation,
                Software = Text("System.ApplicationName") ?? Text("System.Software"),
                Artist = Text("System.Author") ?? Text("System.Photo.CameraOwner"),
                Copyright = Text("System.Copyright"),
                Title = Text("System.Title"),
                Subject = Text("System.Subject"),
                Keywords = Text("System.Keywords"),
                BitDepth = Int("System.Image.BitDepth"),
                ColorSpace = MapColorSpace(Int("System.Image.ColorSpace")) ?? Text("System.Image.ColorSpace"),
                Dimensions = Text("System.Image.Dimensions"),
                Width = Int("System.Image.HorizontalSize"),
                Height = Int("System.Image.VerticalSize"),
                ItemType = Text("System.ItemTypeText") ?? Text("System.ItemType"),
                Compression = Text("System.Image.Compression"),
                XResolution = Dbl("System.Image.XResolution"),
                YResolution = Dbl("System.Image.YResolution"),
            };
        }
        finally
        {
            ReleaseCom(item);
            ReleaseCom(folder);
            ReleaseCom(shell);
        }
    }

    private static object? GetProp(object item, string canonicalName)
    {
        try
        {
            return item.GetType().InvokeMember(
                "ExtendedProperty",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, item, new object[] { canonicalName });
        }
        catch
        {
            return null;
        }
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        try
        {
            return target.GetType().InvokeMember(
                method,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, target, args);
        }
        catch
        {
            return null;
        }
    }

    private static void ReleaseCom(object? com)
    {
        if (com == null) return;
        try
        {
            if (Marshal.IsComObject(com))
                Marshal.FinalReleaseComObject(com);
        }
        catch { }
    }

    // --- value coercion ---------------------------------------------------------

    private static string? AsString(object? v)
    {
        if (v is null) return null;
        var s = v as string ?? (v is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : v.ToString());
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static int? AsInt(object? v)
    {
        switch (v)
        {
            case null: return null;
            case int i: return i;
            case uint u: return (int)u;
            case long l: return (int)l;
            case ulong ul: return (int)ul;
            case short sh: return sh;
            case ushort us: return us;
            case byte b: return b;
            case double d: return (int)Math.Round(d);
            case float f: return (int)Math.Round(f);
            case string s when int.TryParse(s, out var p): return p;
            default: return null;
        }
    }

    private static double? AsDouble(object? v)
    {
        switch (v)
        {
            case null: return null;
            case double d: return d;
            case float f: return f;
            case int i: return i;
            case uint u: return u;
            case long l: return l;
            case ulong ul: return ul;
            case short sh: return sh;
            case ushort us: return us;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p): return p;
            default: return null;
        }
    }

    private static DateTime? AsDate(object? v) => v switch
    {
        null => null,
        DateTime dt => dt,
        string s when DateTime.TryParse(s, out var p) => p,
        _ => null
    };

    // --- Explorer-style formatting ---------------------------------------------

    private static string? FormatExposureTime(double? seconds)
    {
        if (seconds is null or <= 0) return null;
        if (seconds < 1)
        {
            var denom = (int)Math.Round(1.0 / seconds.Value);
            return denom > 0 ? $"1/{denom} sec" : $"{seconds.Value:0.###} sec";
        }
        return $"{seconds.Value:0.###} sec";
    }

    private static string? FormatAperture(double? fNumber) =>
        fNumber is > 0 ? $"f/{fNumber:0.#}" : null;

    private static string? FormatIso(int? iso) =>
        iso is > 0 ? $"ISO-{iso}" : null;

    private static string? FormatMm(double? mm) =>
        mm is > 0 ? $"{mm:0.#} mm" : null;

    private static string? FormatMeters(double? m) =>
        m is > 0 ? $"{m:0.##} m" : null;

    private static string? FormatRatio(double? ratio) =>
        ratio is > 0 ? $"{ratio:0.##}x" : null;

    private static string? FormatNum(double? v) =>
        v is null ? null : v.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string? FormatEv(double? ev) =>
        ev is null ? null : (ev >= 0 ? "+" : "") + ev.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " EV";

    private static string? MapMetering(int? v) => v switch
    {
        0 => "Unknown",
        1 => "Average",
        2 => "CenterWeightedAverage",
        3 => "Spot",
        4 => "MultiSpot",
        5 => "Pattern",
        6 => "Partial",
        255 => "Other",
        _ => null
    };

    private static string? MapLightSource(int? v) => v switch
    {
        0 => "Auto",
        1 => "Daylight",
        2 => "Fluorescent",
        3 => "Tungsten",
        4 => "Flash",
        9 => "FineWeather",
        10 => "CloudyWeather",
        11 => "Shade",
        12 => "DaylightFluorescent",
        13 => "DayWhiteFluorescent",
        14 => "CoolWhiteFluorescent",
        15 => "WhiteFluorescent",
        17 => "StandardLightA",
        18 => "StandardLightB",
        19 => "StandardLightC",
        20 => "D55",
        21 => "D65",
        22 => "D75",
        23 => "D50",
        24 => "TungstenStandard",
        255 => "Other",
        _ => null
    };

    private static string? MapWhiteBalance(int? v) => v switch
    {
        0 => "Auto",
        1 => "Manual",
        _ => null
    };

    private static string? MapFlash(int? v)
    {
        if (v is null) return null;
        // EXIF Flash bit0 = fired; keep the short Explorer-style phrase.
        return (v.Value & 1) == 1 ? "Flash fired" : "Flash did not fire";
    }

    private static string? MapProgram(int? v) => v switch
    {
        0 => "NotDefined",
        1 => "Manual",
        2 => "ProgramAE",
        3 => "AperturePriority",
        4 => "ShutterPriority",
        5 => "CreativeProgram",
        6 => "ActionProgram",
        7 => "PortraitMode",
        8 => "LandscapeMode",
        _ => null
    };

    private static string? MapOrientation(int? v) => v switch
    {
        1 => "Horizontal (normal)",
        2 => "Mirror horizontal",
        3 => "Rotate 180",
        4 => "Mirror vertical",
        5 => "Mirror horizontal and rotate 270 CW",
        6 => "Rotate 90 CW",
        7 => "Mirror horizontal and rotate 90 CW",
        8 => "Rotate 270 CW",
        _ => null
    };

    // System.Image.ColorSpace: 1 = sRGB, 2 = Adobe RGB (matches Explorer).
    private static string? MapColorSpace(int? v) => v switch
    {
        1 => "sRGB",
        2 => "Adobe RGB",
        _ => null
    };
}
