using InTheHand.Bluetooth;

namespace Letra200bSharp
{
    /// <summary>
    /// Discovers and talks to a Dymo LetraTag 200B over Bluetooth LE. Service/characteristic
    /// UUIDs and status codes come from the reverse-engineered protocol documented at
    /// https://github.com/ysfchn/dymo-bluetooth.
    /// </summary>
    public static class LetraPrinter
    {
        private const string DeviceNamePrefix = "Letratag";

        private static readonly BluetoothUuid PrintRequestUuid = BluetoothUuid.FromGuid(new Guid("be3dd651-2b3d-42f1-99c1-f0f749dd0678"));
        private static readonly BluetoothUuid PrintReplyUuid = BluetoothUuid.FromGuid(new Guid("be3dd652-2b3d-42f1-99c1-f0f749dd0678"));

        private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Scans for nearby Dymo LetraTag 200B devices.
        /// </summary>
        /// <remarks>
        /// The <see cref="BluetoothLEScanFilter.NamePrefix"/> filter is passed to the platform's
        /// native BLE scan, but on some platforms (notably Android) it isn't reliably honored -
        /// unnamed devices or devices with an unrelated name can still come back. The results are
        /// filtered again here so only devices actually named "Letratag..." are ever surfaced.
        /// </remarks>
        public static async Task<IReadOnlyCollection<BluetoothDevice>> ScanForDevicesAsync()
        {
            var options = new RequestDeviceOptions { AcceptAllDevices = false };
            var filter = new BluetoothLEScanFilter
            {
                NamePrefix = DeviceNamePrefix
            };
            options.Filters.Add(filter);
            var devices = await Bluetooth.ScanForDevicesAsync(options);
            return devices
                .Where(d => !string.IsNullOrWhiteSpace(d.Name) && d.Name.StartsWith(DeviceNamePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Connects to the device with the given id (see <see cref="BluetoothDevice.Id"/>) and
        /// streams <paramref name="job"/> (as built by <see cref="LetraHelper.CreateJob(byte[], bool, bool)"/>
        /// or its overloads) to it.
        /// </summary>
        /// <exception cref="InvalidOperationException">The device, its service, or its characteristics could not be resolved.</exception>
        public static async Task<LetraPrintResult> PrintAsync(string deviceId, List<byte[]> job)
        {
            var device = await BluetoothDevice.FromIdAsync(deviceId)
                ?? throw new InvalidOperationException($"Unable to connect to bluetooth device '{deviceId}'.");
            return await PrintAsync(device, job);
        }

        /// <summary>
        /// Streams <paramref name="job"/> (as built by <see cref="LetraHelper.CreateJob(byte[], bool, bool)"/>
        /// or its overloads) to an already-resolved <paramref name="device"/>, and waits for
        /// the printer's "print reply" notification to report how it went.
        /// </summary>
        /// <exception cref="InvalidOperationException">The device's service or characteristics could not be resolved.</exception>
        public static async Task<LetraPrintResult> PrintAsync(BluetoothDevice device, List<byte[]> job)
        {
            var services = await device.Gatt.GetPrimaryServicesAsync();
            var uuid = services.FirstOrDefault(s => s.Uuid.ToString().Length == 36)?.Uuid;
            if (uuid.HasValue)
            {
                var serv = await device.Gatt.GetPrimaryServiceAsync(uuid.Value);

                var printRequest = await serv.GetCharacteristicAsync(PrintRequestUuid)
                    ?? throw new InvalidOperationException("Unable to find the print request characteristic.");
                var printReply = await serv.GetCharacteristicAsync(PrintReplyUuid)
                    ?? throw new InvalidOperationException("Unable to find the print reply characteristic.");

                var replyReceived = new TaskCompletionSource<byte>();

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

                // Use a CancellationTokenSource to prevent hanging background timeout threads
                using var cts = new CancellationTokenSource();

                try
                {
                    // 1. Android 16 Stabilization: Request MTU BEFORE starting notifications
                    try
                    {
                        await device.Gatt.RequestMtuAsync(512);
                        await Task.Delay(100); // Allow hardware handshake to finish
                    }
                    catch
                    {
                        // Silent fallback if specific firmware rejects MTU calls
                    }

                    // 2. Start notifications on the newly stabilized MTU size
                    await printReply.StartNotificationsAsync();
                    await Task.Delay(100);

                    // 3. Write data packets using your working 20ms throttling delay
                    foreach (var jobPart in job)
                    {
                        i++;
                        await printRequest.WriteValueWithoutResponseAsync(jobPart);
                        await Task.Delay(20);
                    }

                    // 4. Handle timeout safely without leaking tasks in background memory
                    var timeoutTask = Task.Delay(ReplyTimeout, cts.Token);
                    var completed = await Task.WhenAny(replyReceived.Task, timeoutTask);

                    if (completed != replyReceived.Task)
                    {
                        return new LetraPrintResult(null, false, "The printer didn't report a result within the timeout - the label may or may not have printed.");
                    }

                    // Instantly cancel and dispose of the unused timeout task
                    cts.Cancel();

                    return InterpretStatusCode(await replyReceived.Task);
                }
                catch (Exception e)
                {
                    // Retain useful diagnostics for troubleshooting multi-packet arrays
                    throw new Exception($"Error transmitting packet index {i} out of {job.Count}. Size: {job[i - 1].Length} bytes. Error: {e.Message}", e);
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
            else
            {
                throw new InvalidOperationException("Unable to determine UUID.");
            }
        }

        private static LetraPrintResult InterpretStatusCode(byte statusCode) => statusCode switch
        {
            0 => new LetraPrintResult(statusCode, true, "Printing completed (the printer can report this even when it didn't actually print, e.g. lid open or no cassette)."),
            1 => new LetraPrintResult(statusCode, true, "Printing completed."),
            2 => new LetraPrintResult(statusCode, false, "Printing failed for an unknown reason."),
            3 => new LetraPrintResult(statusCode, true, "Printing completed, but the battery is low - it may not print again with the same batteries."),
            4 => new LetraPrintResult(statusCode, false, "Printing was cancelled."),
            5 => new LetraPrintResult(statusCode, false, "Printing failed for an unknown reason."),
            6 => new LetraPrintResult(statusCode, false, "Printing failed because the battery is low."),
            7 => new LetraPrintResult(statusCode, false, "Printing failed because no cassette was inserted."),
            _ => new LetraPrintResult(statusCode, false, $"Printing failed with an unrecognized status code ({statusCode}).")
        };
    }

    /// <summary>
    /// The outcome of a print job, decoded from the printer's "print reply" notification.
    /// </summary>
    /// <param name="StatusCode">The raw status byte reported by the printer, or <c>null</c> if it never replied within the timeout.</param>
    /// <param name="Printed">
    /// Whether the label was (likely) printed. The printer is not always reliable about this
    /// - see <see cref="Message"/> for the specific caveat, if any.
    /// </param>
    /// <param name="Message">Human-readable explanation of the status.</param>
    public readonly record struct LetraPrintResult(byte? StatusCode, bool Printed, string Message);
}
