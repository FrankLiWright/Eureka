<div align="center">

<img src="Eureka.png" width="128" height="128" alt="Eureka Logo">

# Eureka

A modern, minimal image viewer for Windows with HDR support.

一个现代化的轻量级 Windows 图片查看器，支持 HDR。

[English](#features) | [中文](#功能特性)

</div>

---

## Features

- **Wide Format Support**: JPG, PNG, WebP, BMP, GIF, TIFF, AVIF, HEIC/HEIF, JXL, and RAW formats (CR2, CR3, NEF, ARW, ORF, RW2, RAF, DNG, PEF)
- **HDR Support**: Automatic HDR detection with SDR tone mapping toggle
- **EXIF Metadata**: Camera, lens, exposure, ISO, focal length, and more
- **Auto Rotation**: Reads EXIF orientation and auto-rotates images
- **Zoom & Pan**: Mouse wheel zoom, drag to pan, fit-to-window, 1:1 view
- **Color Picker**: Hover to see pixel color values
- **Dark/Light Theme**: Follows system theme
- **Large File Handling**: Graceful handling of 100MB+ images
- **Single Executable**: Self-contained, no dependencies required

## 功能特性

- **广泛的格式支持**：JPG、PNG、WebP、BMP、GIF、TIFF、AVIF、HEIC/HEIF、JXL，以及 RAW 格式（CR2、CR3、NEF、ARW、ORF、RW2、RAF、DNG、PEF）
- **HDR 支持**：自动识别 HDR 图片，支持 SDR 色调映射切换
- **EXIF 元数据**：相机、镜头、曝光、ISO、焦距等信息
- **自动旋转**：读取 EXIF 方向信息并自动旋转图片
- **缩放与平移**：鼠标滚轮缩放、拖拽平移、适应窗口、1:1 视图
- **取色器**：鼠标悬停查看像素颜色值
- **深色/浅色主题**：跟随系统主题
- **大文件处理**：优雅处理 100MB 以上的图片
- **单文件可执行**：自包含，无需额外依赖

## Download / 下载

Download the latest release from the [Releases](../../releases) page.

从 [Releases](../../releases) 页面下载最新版本。

## Requirements / 系统要求

- Windows 10/11 (x64)
- .NET 8.0 Runtime (included in self-contained build)

## Build from Source / 从源码构建

```bash
cd src/Eureka
dotnet restore
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output will be in `publish/Eureka.exe`.

输出文件位于 `publish/Eureka.exe`。

## Supported RAW Formats / 支持的 RAW 格式

| Camera / 相机 | Extensions / 扩展名 |
|------|-------------|
| Canon | .cr2, .cr3 |
| Nikon | .nef, .nrw |
| Sony | .arw, .srf, .sr2 |
| Fujifilm | .raf |
| Panasonic | .rw2 |
| Olympus | .orf |
| Pentax | .pef |
| Adobe | .dng |

## Dependencies / 依赖库

- [Magick.NET](https://github.com/dlemstra/Magick.NET) - Image processing / 图像处理
- [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) - EXIF reading / EXIF 读取
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) - Additional format support / 额外格式支持

## License / 许可证

MIT

## Author / 作者

[FrankLiWright](https://github.com/FrankLiWright)
