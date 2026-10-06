using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

// Integration check: load Eureka's ImageDecoder.LoadMetadata and print
// the merged metadata shown in the info panel.

var dllPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    @"..\..\..\..\..\src\Eureka\bin\Debug\net8.0-windows\win-x64\Eureka.dll"));

if (!File.Exists(dllPath))
    dllPath = Path.GetFullPath(@"src\Eureka\bin\Debug\net8.0-windows\win-x64\Eureka.dll");

Console.WriteLine($"Loading: {dllPath}");
var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);

var decoderType = asm.GetType("Eureka.ImageDecoder")!;
var decoder = Activator.CreateInstance(decoderType)!;
var load = decoderType.GetMethod("LoadMetadata", BindingFlags.Public | BindingFlags.Instance)!;

var paths = args.Length > 0
    ? args
    : new[]
    {
        @"C:\Users\Reve\Desktop\20260929音乐会\IMG_7888.jpg",
        @"C:\Users\Reve\Pictures\pexels-michael-pointner-134459625-25381396.jpg",
        @"C:\Users\Reve\Pictures\103713286_p0.jpg",
    };

foreach (var path in paths)
{
    Console.WriteLine();
    Console.WriteLine($"File: {path}");
    Console.WriteLine(new string('-', 60));
    object? meta;
    try
    {
        meta = load.Invoke(decoder, new object[] { path });
    }
    catch (Exception ex)
    {
        Console.WriteLine("LoadMetadata failed: " + ex.InnerException?.Message ?? ex.Message);
        continue;
    }

    if (meta == null)
    {
        Console.WriteLine("LoadMetadata returned null");
        continue;
    }

    foreach (var prop in meta.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        var v = prop.GetValue(meta);
        if (v == null) continue;
        if (prop.Name is "FilePath") continue;
        var display = v switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            double d => d.ToString("0.###"),
            _ => v.ToString()
        };
        Console.WriteLine($"{prop.Name,-20} = {display}");
    }
}
