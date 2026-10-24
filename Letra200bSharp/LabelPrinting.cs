using Microsoft.Extensions.DependencyInjection;

namespace Letra200bSharp;

/// <summary>Creates an <see cref="ILabelPrintingService"/> without an application-level dependency injection container.</summary>
public static class LabelPrinting
{
    /// <summary>
    /// Builds a standalone <see cref="ILabelPrintingService"/>, e.g.
    /// <c>LabelPrinting.Create(letra => letra.AddDymoLetraTag())</c>. Applications that already
    /// use <c>Microsoft.Extensions.DependencyInjection</c> should call <c>services.AddLetra()</c>
    /// instead.
    /// </summary>
    public static ILabelPrintingService Create(Action<LetraBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var services = new ServiceCollection();
        services.AddLetra(configuration);

        // The provider only ever holds singletons, so it simply lives as long as the service.
        return services.BuildServiceProvider().GetRequiredService<ILabelPrintingService>();
    }
}
