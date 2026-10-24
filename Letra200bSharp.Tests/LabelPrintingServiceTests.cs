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
    [Fact]
    public async Task A_custom_driver_registered_with_AddDriver_renders_and_prints()
    {
        var driver = new FakeDriver("Fake", true, "a");
        var service = LabelPrinting.Create(letra => letra.AddDriver(driver));

        var printer = Assert.Single(await service.DiscoverAsync(TestContext.Current.CancellationToken));
        var result = await service.PrintAsync(printer, new TextLabel("hello"), TestContext.Current.CancellationToken);

        Assert.True(result.Printed);
        var (printedOn, bitmap) = Assert.Single(driver.Printed);
        Assert.Equal(printer, printedOn);
        Assert.Equal(5, bitmap.Length);
        Assert.Equal(8, bitmap.HeadDots);
    }

    [Fact]
    public void AddDriver_of_T_resolves_the_driver_from_the_container()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new FakeDriver("FromContainer"));
        services.AddLetra().AddDriver<FakeDriver>().AddDriver<FakeDriver>();

        var service = services.BuildServiceProvider().GetRequiredService<ILabelPrintingService>();

        Assert.Equal("FromContainer", Assert.Single(service.Drivers).Name);
    }

    [Fact]
    public async Task Discovery_spans_every_available_driver_and_skips_unavailable_ones()
    {
        var service = LabelPrinting.Create(letra => letra
            .AddDriver(new FakeDriver("One", true, "1", "2"))
            .AddDriver(new FakeDriver("Off", false, "3"))
            .AddDriver(new FakeDriver("Two", true, "4")));

        var printers = await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "1", "2", "4" }, printers.Select(p => p.Id));
        Assert.True(await service.IsAnyDriverAvailableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Printing_goes_to_the_printers_own_driver()
    {
        var one = new FakeDriver("One", true, "1");
        var two = new FakeDriver("Two", true, "2");
        var service = LabelPrinting.Create(letra => letra.AddDriver(one).AddDriver(two));

        await service.PrintAsync(two.Printers[0], new TextLabel("x"), TestContext.Current.CancellationToken);

        Assert.Empty(one.Printed);
        Assert.Single(two.Printed);
    }

    [Fact]
    public void Previews_without_a_printer_use_the_default_driver()
    {
        var service = LabelPrinting.Create(letra => letra
            .AddDriver(new FakeDriver("One"))
            .AddDriver(new FakeDriver("Two"))
            .SetDefaultDriver("Two"));

        Assert.Equal("Two", service.DefaultDriver.Name);
        Assert.Equal("Two"u8.ToArray(), service.RenderPreview(new TextLabel("x")));
        Assert.Equal("One"u8.ToArray(), service.RenderPreview(new TextLabel("x"), new PrinterInfo("p", "p", "One")));
    }

    [Fact]
    public void Misconfiguration_is_reported_when_the_service_is_built()
    {
        Assert.Throws<InvalidOperationException>(() => LabelPrinting.Create(_ => { }));
        Assert.Throws<InvalidOperationException>(() => LabelPrinting.Create(letra => letra.AddDriver(new FakeDriver("A")).AddDriver(new FakeDriver("A"))));
        Assert.Throws<InvalidOperationException>(() => LabelPrinting.Create(letra => letra.AddDriver(new FakeDriver("A")).SetDefaultDriver("B")));
    }

    [Fact]
    public void Unknown_printer_drivers_are_rejected()
    {
        var service = LabelPrinting.Create(letra => letra.AddDriver(new FakeDriver("A")));

        Assert.Throws<InvalidOperationException>(() => service.GetDriver(new PrinterInfo("p", "p", "Missing")));
    }

    [Fact]
    public void AddLetraTag200b_registers_the_driver_with_its_options()
    {
        var services = new ServiceCollection();
        services.AddLetra().AddLetraTag200b(letraTag => letraTag
            .SetReplyTimeout(TimeSpan.FromSeconds(3))
            .SetRequestedMtu(256)
            .SetDefaultFontFamily("DejaVu Sans"));
        var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<ILabelPrintingService>();
        var options = provider.GetRequiredService<IOptions<LetraTag200bOptions>>().Value;

        Assert.Equal(LetraTag200bDriver.DriverName, Assert.Single(service.Drivers).Name);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ReplyTimeout);
        Assert.Equal(256, options.RequestedMtu);
        Assert.Equal("DejaVu Sans", options.DefaultFontFamily);
    }
}
