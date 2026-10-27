using Letra200bSharp.LetraTag200b;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Letra200bSharp.Tests;

/// <summary>A printer added purely by implementing <see cref="ILabelPrinterDriver"/> - what a new printer model looks like.</summary>
internal sealed class FakeDriver : ILabelPrinterDriver, ILabelRenderer
{
    public FakeDriver(string name = "Fake", bool available = true, params string[] printerIds)
    {
        Name = name;
        Available = available;
        Printers = printerIds.Select(id => new PrinterInfo(id, $"{name} {id}", name)).ToList();
    }

    public string Name { get; }

    public bool Available { get; }

    public List<PrinterInfo> Printers { get; }

    public List<(PrinterInfo Printer, LabelBitmap Bitmap)> Printed { get; } = new();

    public PrinterCapabilities Capabilities { get; } = new()
    {
        Model = "Fake 8-dot",
        HeadDots = 8,
        PrintableHeadDots = 8,
        HeadDotsPerMm = 8,
        FeedDotsPerMm = 8
    };

    public ILabelRenderer Renderer => this;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);

    public Task<IReadOnlyList<PrinterInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PrinterInfo>>(Printers);

    public Task<PrintResult> PrintAsync(PrinterInfo printer, LabelBitmap bitmap, CancellationToken cancellationToken = default)
    {
        Printed.Add((printer, bitmap));
        return Task.FromResult(new PrintResult { Printed = true, Message = "ok" });
    }

    public LabelBitmap Render(Label label) => new(label is TextLabel text ? text.Text.Length : 1, 8, new byte[(label is TextLabel t ? t.Text.Length : 1) * 8]);

    public byte[] RenderPreview(Label label) => System.Text.Encoding.UTF8.GetBytes(Name);

    public byte[] RenderContent(Label label) => RenderPreview(label);
}

public class LabelPrintingServiceTests
{
    [Test]
    public async Task A_custom_driver_registered_with_AddDriver_renders_and_prints()
    {
        var driver = new FakeDriver("Fake", true, "a");
        var service = LabelPrinting.Create(letra => letra.AddDriver(driver));

        var printers = await service.DiscoverAsync();
        await Assert.That(printers.Count).IsEqualTo(1);
        var result = await service.PrintAsync(printers[0], new TextLabel("hello"));

        await Assert.That(result.Printed).IsTrue();
        await Assert.That(driver.Printed.Count).IsEqualTo(1);
        var (printedOn, bitmap) = driver.Printed[0];
        await Assert.That(printedOn).IsEqualTo(printers[0]);
        await Assert.That(bitmap.Length).IsEqualTo(5);
        await Assert.That(bitmap.HeadDots).IsEqualTo(8);
    }

    [Test]
    public async Task AddDriver_of_T_resolves_the_driver_from_the_container()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new FakeDriver("FromContainer"));
        services.AddLetra().AddDriver<FakeDriver>().AddDriver<FakeDriver>();

        var service = services.BuildServiceProvider().GetRequiredService<ILabelPrintingService>();

        await Assert.That(service.Drivers.Count).IsEqualTo(1);
        await Assert.That(service.Drivers[0].Name).IsEqualTo("FromContainer");
    }

    [Test]
    public async Task Discovery_spans_every_available_driver_and_skips_unavailable_ones()
    {
        var service = LabelPrinting.Create(letra => letra
            .AddDriver(new FakeDriver("One", true, "1", "2"))
            .AddDriver(new FakeDriver("Off", false, "3"))
            .AddDriver(new FakeDriver("Two", true, "4")));

        var printers = await service.DiscoverAsync();

        await Assert.That(string.Join(",", printers.Select(p => p.Id))).IsEqualTo("1,2,4");
        await Assert.That(await service.IsAnyDriverAvailableAsync()).IsTrue();
    }

    [Test]
    public async Task Printing_goes_to_the_printers_own_driver()
    {
        var one = new FakeDriver("One", true, "1");
        var two = new FakeDriver("Two", true, "2");
        var service = LabelPrinting.Create(letra => letra.AddDriver(one).AddDriver(two));

        await service.PrintAsync(two.Printers[0], new TextLabel("x"));

        await Assert.That(one.Printed.Count).IsEqualTo(0);
        await Assert.That(two.Printed.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Previews_without_a_printer_use_the_default_driver()
    {
        var service = LabelPrinting.Create(letra => letra
            .AddDriver(new FakeDriver("One"))
            .AddDriver(new FakeDriver("Two"))
            .SetDefaultDriver("Two"));

        await Assert.That(service.DefaultDriver.Name).IsEqualTo("Two");
        await Assert.That(System.Text.Encoding.UTF8.GetString(service.RenderPreview(new TextLabel("x")))).IsEqualTo("Two");
        await Assert.That(System.Text.Encoding.UTF8.GetString(service.RenderPreview(new TextLabel("x"), new PrinterInfo("p", "p", "One")))).IsEqualTo("One");
    }

    [Test]
    public async Task Misconfiguration_is_reported_when_the_service_is_built()
    {
        await Assert.That(() => LabelPrinting.Create(_ => { })).Throws<InvalidOperationException>();
        await Assert.That(() => LabelPrinting.Create(letra => letra.AddDriver(new FakeDriver("A")).AddDriver(new FakeDriver("A")))).Throws<InvalidOperationException>();
        await Assert.That(() => LabelPrinting.Create(letra => letra.AddDriver(new FakeDriver("A")).SetDefaultDriver("B"))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Unknown_printer_drivers_are_rejected()
    {
        var service = LabelPrinting.Create(letra => letra.AddDriver(new FakeDriver("A")));

        await Assert.That(() => service.GetDriver(new PrinterInfo("p", "p", "Missing"))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddLetraTag200b_registers_the_driver_with_its_options()
    {
        var services = new ServiceCollection();
        services.AddLetra().AddLetraTag200b(letraTag => letraTag
            .SetReplyTimeout(TimeSpan.FromSeconds(3))
            .SetRequestedMtu(256)
            .SetDefaultFontFamily("DejaVu Sans"));
        var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<ILabelPrintingService>();
        var options = provider.GetRequiredService<IOptions<LetraTag200bOptions>>().Value;

        await Assert.That(service.Drivers.Count).IsEqualTo(1);
        await Assert.That(service.Drivers[0].Name).IsEqualTo(LetraTag200bDriver.DriverName);
        await Assert.That(options.ReplyTimeout).IsEqualTo(TimeSpan.FromSeconds(3));
        await Assert.That(options.RequestedMtu).IsEqualTo(256);
        await Assert.That(options.DefaultFontFamily).IsEqualTo("DejaVu Sans");
    }
}
