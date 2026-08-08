# letra200bsharp
C# library and code examples for printing images to a Dymo LetraTag 200B labelprinter.

## Repository contents
- **letra200bsharp** contains:
  -  Logic for resizing / converting an input image to a matrix representing the black and white pixels of the image
  - Protocol for communicating with the device
- **letra200bsharp.Console** is a console application that accepts a BLE mac address and an image path that can be sent to the printer
- **letra200bsharp.WinForms** is a WinForms application that easily lets you select a device and image for sending to the LetraTag 200B
- **letra200bsharp.Avalonia** is a cross-platform Avalonia application that easily lets you select a device and image for sending to the LetraTag 200B
  - **letra200bsharp.Avalonia.Desktop** is a Avalonia desktop application that easily lets you select a device and image for sending to the LetraTag 200B
  - **letra200bsharp.Avalonia.Android** is a Avalonia android mobile application that easily lets you select a device and image for sending to the LetraTag 200B

## Used libraries
- [SkiaSharp](https://github.com/mono/SkiaSharp)
- [InTheHand.BluetoothLE](https://github.com/inthehand/32feet)
- [CommandLineParser](https://github.com/commandlineparser/commandline)
- [Avalonia](https://avaloniaui.net/)

## Remarks
This library is based on the excellent Python example [lt200b](https://github.com/alexhorn/lt200b) and [dymo-bluetooth](https://github.com/ysfchn/dymo-bluetooth)