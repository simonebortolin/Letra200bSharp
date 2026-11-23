using Letra200bSharp;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers Letra in an <see cref="IServiceCollection"/>.</summary>
public static class LetraServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ILabelPrintingService"/> and returns a <see cref="LetraBuilder"/> to
    /// register printer drivers with, e.g. <c>services.AddLetra().AddLetraTag200b()</c>.
    /// </summary>
    public static LetraBuilder AddLetra(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions();
        services.TryAddSingleton<ILabelPrintingService, LabelPrintingService>();
        return new LetraBuilder(services);
    }

    /// <summary>Registers <see cref="ILabelPrintingService"/> and configures it with <paramref name="configuration"/>.</summary>
    public static IServiceCollection AddLetra(this IServiceCollection services, Action<LetraBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration(services.AddLetra());
        return services;
    }
}
