# Letra200bSharp
C# library and mobile and desktop applications for printing images to a Dymo LetraTag 200B labelprinter, fork of [letra200bsharp](https://github.com/brz/letra200bsharp).

## Repository contents
- **Letra200bSharp** contains:
  - Logic for resizing / converting an input image to a matrix representing the black and white pixels of the image
  - Protocol for communicating with the device
- **Letra200bSharp.Avalonia** is a cross-platform Avalonia application that easily lets you select a device and an image, text or barcode label for sending to the LetraTag 200B
  - **Letra200bSharp.Avalonia.Desktop** is the Windows/Linux desktop app. Launched with no arguments it opens the GUI; launched with arguments it instead runs headless as a CLI, with one verb per tab - `image`, `text`, `barcode` - plus `list-devices` to scan for nearby printers (run with `--help`, or `<verb> --help`, for the full option list)
  - **Letra200bSharp.Avalonia.Android** is a Avalonia android mobile application that easily lets you select a device and an image, text or barcode label for sending to the LetraTag 200B

## Used libraries
- [SkiaSharp](https://github.com/mono/SkiaSharp)
- [InTheHand.BluetoothLE](https://github.com/inthehand/32feet)
- [CommandLineParser](https://github.com/commandlineparser/commandline)
- [Avalonia](https://avaloniaui.net/)
- [ZXing.Net](https://github.com/micjahn/ZXing.Net) for barcode encoding

## Remarks
This library is based on the excellent Python example [lt200b](https://github.com/alexhorn/lt200b) and [dymo-bluetooth](https://github.com/ysfchn/dymo-bluetooth)