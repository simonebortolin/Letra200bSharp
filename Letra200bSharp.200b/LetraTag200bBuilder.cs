using Letra200bSharp;
using Letra200bSharp.LetraTag200b;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>Configures the Dymo LetraTag 200B driver - returned by <see cref="LetraTag200bLetraBuilderExtensions.AddLetraTag200b(LetraBuilder)"/>.</summary>
    public sealed class LetraTag200bBuilder
    {
        public LetraTag200bBuilder(IServiceCollection services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>The service collection the driver is registered in.</summary>
        public IServiceCollection Services { get; }

        /// <summary>Amends <see cref="LetraTag200bOptions"/>.</summary>
        public LetraTag200bBuilder Configure(Action<LetraTag200bOptions> configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            Services.Configure(configuration);
            return this;
        }

        /// <summary>Font family used for text whose label doesn't name one - see <see cref="LetraTag200bOptions.DefaultFontFamily"/>.</summary>
        public LetraTag200bBuilder SetDefaultFontFamily(string fontFamily)
        {
            ArgumentException.ThrowIfNullOrEmpty(fontFamily);
            return Configure(options => options.DefaultFontFamily = fontFamily);
        }

        /// <summary>See <see cref="LetraTag200bOptions.DeviceNamePrefix"/>.</summary>
        public LetraTag200bBuilder SetDeviceNamePrefix(string prefix)
        {
            ArgumentException.ThrowIfNullOrEmpty(prefix);
            return Configure(options => options.DeviceNamePrefix = prefix);
        }

        /// <summary>See <see cref="LetraTag200bOptions.ReplyTimeout"/>.</summary>
        public LetraTag200bBuilder SetReplyTimeout(TimeSpan timeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
            return Configure(options => options.ReplyTimeout = timeout);
        }

        /// <summary>See <see cref="LetraTag200bOptions.PacketDelay"/>.</summary>
        public LetraTag200bBuilder SetPacketDelay(TimeSpan delay)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
            return Configure(options => options.PacketDelay = delay);
        }

        /// <summary>See <see cref="LetraTag200bOptions.RequestedMtu"/>.</summary>
        public LetraTag200bBuilder SetRequestedMtu(int mtu)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mtu);
            return Configure(options => options.RequestedMtu = mtu);
        }
    }

    /// <summary>Registers the Dymo LetraTag 200B driver with Letra.</summary>
    public static class LetraTag200bLetraBuilderExtensions
    {
        /// <summary>Registers the Dymo LetraTag 200B driver and returns a builder to configure it.</summary>
        public static LetraTag200bBuilder AddLetraTag200b(this LetraBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddOptions<LetraTag200bOptions>();
            builder.AddDriver<LetraTag200bDriver>();
            return new LetraTag200bBuilder(builder.Services);
        }

        /// <summary>Registers the Dymo LetraTag 200B driver and configures it with <paramref name="configuration"/>.</summary>
        public static LetraBuilder AddLetraTag200b(this LetraBuilder builder, Action<LetraTag200bBuilder> configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            configuration(builder.AddLetraTag200b());
            return builder;
        }
    }
}
