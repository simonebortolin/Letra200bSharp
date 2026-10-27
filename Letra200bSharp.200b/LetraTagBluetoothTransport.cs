using System.Collections.Concurrent;
using System.Diagnostics;
using InTheHand.Bluetooth;
using Letra200bSharp.LetraTag200b.Resources;

namespace Letra200bSharp.LetraTag200b
{
    /// <summary>
    /// Discovers and talks to Dymo LetraTag 200B printers over Bluetooth LE. Service/characteristic
    /// UUIDs and status codes come from the reverse-engineered protocol documented at
    /// https://github.com/ysfchn/dymo-bluetooth.
    /// </summary>
    internal sealed class LetraTagBluetoothTransport
    {
        private static readonly BluetoothUuid PrintRequestUuid = BluetoothUuid.FromGuid(new Guid("be3dd651-2b3d-42f1-99c1-f0f749dd0678"));
        private static readonly BluetoothUuid PrintReplyUuid = BluetoothUuid.FromGuid(new Guid("be3dd652-2b3d-42f1-99c1-f0f749dd0678"));

        private readonly LetraTag200bOptions _options;

        /// <summary>Devices returned by the last scans, by id - reused when printing, so a just-discovered device doesn't need resolving again.</summary>
        private readonly ConcurrentDictionary<string, BluetoothDevice> _knownDevices = new();

        public LetraTagBluetoothTransport(LetraTag200bOptions options)
        {
            _options = options;
        }

        public async Task<bool> IsAvailableAsync() => await Bluetooth.GetAvailabilityAsync();

        /// <summary>Scans for nearby LetraTag printers.</summary>
        /// <remarks>
        /// The <see cref="BluetoothLEScanFilter.NamePrefix"/> filter is passed to the platform's
        /// native BLE scan, but on some platforms (notably Android) it isn't reliably honored -
        /// unnamed devices or devices with an unrelated name can still come back. The results are
        /// filtered again here so only devices actually named with the prefix are ever surfaced.
        /// </remarks>
        public async Task<IReadOnlyList<BluetoothDevice>> ScanAsync(CancellationToken cancellationToken)
        {
            var prefix = _options.DeviceNamePrefix;
            var options = new RequestDeviceOptions { AcceptAllDevices = false };
            options.Filters.Add(new BluetoothLEScanFilter { NamePrefix = prefix });
            var devices = await Bluetooth.ScanForDevicesAsync(options, cancellationToken);
            var printers = devices
                .Where(d => !string.IsNullOrWhiteSpace(d.Name) && d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var printer in printers)
            {
                _knownDevices[printer.Id] = printer;
            }

            return printers;
        }

        /// <summary>
        /// Streams <paramref name="job"/> (see <see cref="LetraTagProtocol.Encode"/>) to the device with
        /// the given id and waits for the printer's "print reply" notification to report how it went.
        /// </summary>
        /// <exception cref="InvalidOperationException">The device, its service, or its characteristics could not be resolved.</exception>
        public async Task<PrintResult> PrintAsync(string deviceId, List<byte[]> job, CancellationToken cancellationToken)
        {
            if (!_knownDevices.TryGetValue(deviceId, out var device))
            {
                device = await BluetoothDevice.FromIdAsync(deviceId)
                    ?? throw new InvalidOperationException($"Unable to connect to bluetooth device '{deviceId}'.");
            }

            var stopwatch = Stopwatch.StartNew();
            var services = await device.Gatt.GetPrimaryServicesAsync();
            var uuid = services?.FirstOrDefault(s => s.Uuid.ToString().Length == 36)?.Uuid
                ?? throw new InvalidOperationException("Unable to determine UUID.");

            var serv = await device.Gatt.GetPrimaryServiceAsync(uuid)
                ?? throw new InvalidOperationException($"Unable to open the printer's GATT service {uuid}.");

            var printRequest = await serv.GetCharacteristicAsync(PrintRequestUuid)
                ?? throw new InvalidOperationException("Unable to find the print request characteristic.");
            var printReply = await serv.GetCharacteristicAsync(PrintReplyUuid)
                ?? throw new InvalidOperationException("Unable to find the print reply characteristic.");

            var replyReceived = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnValueChanged(object? sender, GattCharacteristicValueChangedEventArgs e)
            {
                // The printer notifies "1B 52 <status>" (ESC 'R' <status>) once printing is done.
                var value = e.Value;
                if (value != null && value.Length >= 3 && value[0] == 0x1B && value[1] == 0x52)
                {
                    replyReceived.TrySetResult(value[2]);
                }
            }

            printReply.CharacteristicValueChanged += OnValueChanged;
            int i = 0;

            // Cancels the reply timeout once the reply arrives, so it doesn't linger in the background.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            bool mtuNegotiated;
            try
            {
                // 1. Android 16 stabilization: request the MTU BEFORE starting notifications.
                try
                {
                    await device.Gatt.RequestMtuAsync(_options.RequestedMtu);
                    await Task.Delay(100, cancellationToken); // Allow the hardware handshake to finish
                    mtuNegotiated = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Silent fallback if specific firmware rejects MTU calls
                    mtuNegotiated = false;
                }

                // 2. Start notifications on the newly stabilized MTU size.
                await printReply.StartNotificationsAsync();
                await Task.Delay(100, cancellationToken);

                // 3. Write the packets, throttled - the printer drops data sent too fast.
                foreach (var jobPart in job)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    i++;
                    await printRequest.WriteValueWithoutResponseAsync(jobPart);
                    await Task.Delay(_options.PacketDelay, cancellationToken);
                }

                // 4. Wait for the reply, or give up after the timeout.
                var timeoutTask = Task.Delay(_options.ReplyTimeout, timeoutCts.Token);
                var completed = await Task.WhenAny(replyReceived.Task, timeoutTask);
                cancellationToken.ThrowIfCancellationRequested();

                var stats = new PrintStatistics
                {
                    TotalBytes = job.Sum(p => p.Length),
                    PacketCount = job.Count,
                    Elapsed = stopwatch.Elapsed,
                    Details = new Dictionary<string, string>
                    {
                        [LetraTag200bDriver.ServiceUuidDetail] = uuid.ToString(),
                        [LetraTag200bDriver.MtuDetail] = $"{_options.RequestedMtu} requested, {(mtuNegotiated ? "negotiated" : "not confirmed")}"
                    }
                };

                if (completed != replyReceived.Task)
                {
                    return new PrintResult { Printed = false, Message = Strings.PrintResult_Timeout, Statistics = stats };
                }

                timeoutCts.Cancel();

                return InterpretStatusCode(await replyReceived.Task) with { Statistics = stats };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // Retain useful diagnostics for troubleshooting multi-packet jobs
                var size = i > 0 ? $" Size: {job[i - 1].Length} bytes." : string.Empty;
                throw new Exception($"Error transmitting packet index {i} out of {job.Count}.{size} Error: {e.Message}", e);
            }
            finally
            {
                printReply.CharacteristicValueChanged -= OnValueChanged;
                try
                {
                    await printReply.StopNotificationsAsync();
                }
                catch
                {
                    // Catch silent failures here if the printer disconnects abruptly
                }
            }
        }

        internal static PrintResult InterpretStatusCode(byte statusCode)
        {
            var (printed, message) = statusCode switch
            {
                0 => (true, Strings.PrintResult_StatusCompletedMaybe),
                1 => (true, Strings.PrintResult_StatusCompleted),
                2 => (false, Strings.PrintResult_StatusUnknownFailure),
                3 => (true, Strings.PrintResult_StatusLowBatteryCompleted),
                4 => (false, Strings.PrintResult_StatusCancelled),
                5 => (false, Strings.PrintResult_StatusUnknownFailure),
                6 => (false, Strings.PrintResult_StatusLowBatteryFailed),
                7 => (false, Strings.PrintResult_StatusNoCassette),
                _ => (false, string.Format(Strings.PrintResult_StatusUnrecognized, statusCode))
            };

            return new PrintResult { Printed = printed, Message = message, StatusCode = statusCode };
        }
    }
}
