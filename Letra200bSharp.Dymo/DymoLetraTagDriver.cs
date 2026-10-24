using Microsoft.Extensions.Options;

namespace Letra200bSharp.Dymo
{
    /// <summary>Settings of the Dymo LetraTag 200B driver - configure them with <see cref="Microsoft.Extensions.DependencyInjection.DymoLetraTagBuilder"/>.</summary>
    public sealed class DymoLetraTagOptions
    {
        /// <summary>Font family used for text whose label doesn't name one.</summary>
        public string DefaultFontFamily { get; set; } = "Arial";

        /// <summary>Only Bluetooth devices whose advertised name starts with this are treated as printers.</summary>
        public string DeviceNamePrefix { get; set; } = "Letratag";

        /// <summary>How long to wait for the printer to report the outcome after the last packet.</summary>
        public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Pause between packets - the printer drops data that arrives too fast.</summary>
        public TimeSpan PacketDelay { get; set; } = TimeSpan.FromMilliseconds(20);

        /// <summary>MTU (bytes) requested from the printer before printing; firmware that rejects it is silently tolerated.</summary>
        public int RequestedMtu { get; set; } = 512;
    }

    /// <summary>
    /// <see cref="ILabelPrinterDriver"/> for the Dymo LetraTag 200B over Bluetooth LE. Register it with
    /// <c>services.AddLetra().AddDymoLetraTag()</c>.
    /// </summary>
    public sealed class DymoLetraTagDriver : ILabelPrinterDriver
    {
        /// <summary>The driver's <see cref="Name"/>, as stored in <see cref="PrinterInfo.Driver"/>.</summary>
        public const string DriverName = "Dymo.LetraTag200B";

        /// <summary><see cref="PrintStatistics.Details"/> key of the GATT service UUID the printer advertised.</summary>
        public const string ServiceUuidDetail = "Service UUID";

        /// <summary><see cref="PrintStatistics.Details"/> key describing the MTU negotiation.</summary>
        public const string MtuDetail = "MTU";

        private readonly LetraTagBluetoothTransport _transport;

        public DymoLetraTagDriver(IOptions<DymoLetraTagOptions> options)
        {
            var value = options.Value;
            _transport = new LetraTagBluetoothTransport(value);
            Renderer = new LetraTagLabelRenderer(value);
        }

        public string Name => DriverName;

        /// <summary>
        /// 32 head dots, of which the first and last don't reliably print; head-axis dots are
        /// twice the physical size of feed-axis dots, whose pitch was calibrated on real printouts.
        /// </summary>
        public PrinterCapabilities Capabilities { get; } = new()
        {
            Model = "Dymo LetraTag 200B",
            HeadDots = 32,
            PrintableHeadDots = 30,
            HeadDotsPerMm = RenderHelper.FeedAxisPixelsPerMm / 2f,
            FeedDotsPerMm = RenderHelper.FeedAxisPixelsPerMm
        };

        public ILabelRenderer Renderer { get; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => _transport.IsAvailableAsync();

        public async Task<IReadOnlyList<PrinterInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            var devices = await _transport.ScanAsync(cancellationToken);
            return devices.Select(device => new PrinterInfo(device.Id, device.Name, DriverName)).ToList();
        }

        public Task<PrintResult> PrintAsync(PrinterInfo printer, LabelBitmap bitmap, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(printer);
            ArgumentNullException.ThrowIfNull(bitmap);
            if (printer.Driver != DriverName)
            {
                throw new ArgumentException($"Printer '{printer.Name}' belongs to driver '{printer.Driver}', not '{DriverName}'.", nameof(printer));
            }

            if (bitmap.HeadDots != Capabilities.HeadDots)
            {
                throw new ArgumentException($"The LetraTag 200B prints {Capabilities.HeadDots}-dot-tall labels, but the bitmap is {bitmap.HeadDots} dots tall.", nameof(bitmap));
            }

            return _transport.PrintAsync(printer.Id, LetraTagProtocol.Encode(bitmap), cancellationToken);
        }
    }
}
