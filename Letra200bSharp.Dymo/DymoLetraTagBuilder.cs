using Letra200bSharp;
using Letra200bSharp.Dymo;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>Configures the Dymo LetraTag 200B driver - returned by <see cref="DymoLetraTagLetraBuilderExtensions.AddDymoLetraTag(LetraBuilder)"/>.</summary>
    public sealed class DymoLetraTagBuilder
    {
        public DymoLetraTagBuilder(IServiceCollection services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>The service collection the driver is registered in.</summary>
        public IServiceCollection Services { get; }

        /// <summary>Amends <see cref="DymoLetraTagOptions"/>.</summary>
        public DymoLetraTagBuilder Configure(Action<DymoLetraTagOptions> configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            Services.Configure(configuration);
            return this;
        }

        /// <summary>Font family used for text whose label doesn't name one - see <see cref="DymoLetraTagOptions.DefaultFontFamily"/>.</summary>
        public DymoLetraTagBuilder SetDefaultFontFamily(string fontFamily)
        {
            ArgumentException.ThrowIfNullOrEmpty(fontFamily);
            return Configure(options => options.DefaultFontFamily = fontFamily);
        }

        /// <summary>See <see cref="DymoLetraTagOptions.DeviceNamePrefix"/>.</summary>
        public DymoLetraTagBuilder SetDeviceNamePrefix(string prefix)
        {
            ArgumentException.ThrowIfNullOrEmpty(prefix);
            return Configure(options => options.DeviceNamePrefix = prefix);
        }

        /// <summary>See <see cref="DymoLetraTagOptions.ReplyTimeout"/>.</summary>
        public DymoLetraTagBuilder SetReplyTimeout(TimeSpan timeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
            return Configure(options => options.ReplyTimeout = timeout);
        }

        /// <summary>See <see cref="DymoLetraTagOptions.PacketDelay"/>.</summary>
        public DymoLetraTagBuilder SetPacketDelay(TimeSpan delay)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
            return Configure(options => options.PacketDelay = delay);
        }

        /// <summary>See <see cref="DymoLetraTagOptions.RequestedMtu"/>.</summary>
        public DymoLetraTagBuilder SetRequestedMtu(int mtu)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mtu);
            return Configure(options => options.RequestedMtu = mtu);
        }
    }

    /// <summary>Registers the Dymo LetraTag 200B driver with Letra.</summary>
    public static class DymoLetraTagLetraBuilderExtensions
    {
        /// <summary>Registers the Dymo LetraTag 200B driver and returns a builder to configure it.</summary>
        public static DymoLetraTagBuilder AddDymoLetraTag(this LetraBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddOptions<DymoLetraTagOptions>();
            builder.AddDriver<DymoLetraTagDriver>();
            return new DymoLetraTagBuilder(builder.Services);
        }

        /// <summary>Registers the Dymo LetraTag 200B driver and configures it with <paramref name="configuration"/>.</summary>
        public static LetraBuilder AddDymoLetraTag(this LetraBuilder builder, Action<DymoLetraTagBuilder> configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            configuration(builder.AddDymoLetraTag());
            return builder;
        }
    }
}
