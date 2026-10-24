using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Letra200bSharp;

/// <summary>Options shared by every driver - configure them with <see cref="LetraBuilder.Configure"/>.</summary>
public sealed class LetraOptions
{
    /// <summary>
    /// <see cref="ILabelPrinterDriver.Name"/> of the driver used when no printer is involved (see
    /// <see cref="ILabelPrintingService.DefaultDriver"/>), or <c>null</c> for the first registered.
    /// </summary>
    public string? DefaultDriver { get; set; }
}

/// <summary>
/// Configures Letra: returned by <c>services.AddLetra()</c>, it's where drivers are registered,
/// either through a driver package's own extension (e.g. <c>AddLetraTag200b()</c>) or with
/// <see cref="AddDriver{TDriver}"/> for a driver of your own.
/// </summary>
public sealed class LetraBuilder
{
    public LetraBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>The service collection Letra is being registered in - driver packages add their own services here.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Amends <see cref="LetraOptions"/>.</summary>
    public LetraBuilder Configure(Action<LetraOptions> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Services.Configure(configuration);
        return this;
    }

    /// <summary>Registers a printer driver, resolved from the container (so it can take its own dependencies). Registering the same type twice has no effect.</summary>
    public LetraBuilder AddDriver<TDriver>() where TDriver : class, ILabelPrinterDriver
    {
        Services.TryAddSingleton<TDriver>();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILabelPrinterDriver, TDriver>(provider => provider.GetRequiredService<TDriver>()));
        return this;
    }

    /// <summary>Registers an already-built printer driver instance.</summary>
    public LetraBuilder AddDriver(ILabelPrinterDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        Services.AddSingleton(driver);
        return this;
    }

    /// <summary>Uses the driver named <paramref name="name"/> when no printer is involved - see <see cref="LetraOptions.DefaultDriver"/>.</summary>
    public LetraBuilder SetDefaultDriver(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Configure(options => options.DefaultDriver = name);
    }
}
