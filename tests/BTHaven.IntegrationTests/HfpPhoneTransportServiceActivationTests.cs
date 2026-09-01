using BTHaven.Core.Calls;
using BTHaven.Windows.Telephony;

namespace BTHaven.IntegrationTests;

public sealed class HfpPhoneTransportServiceActivationTests
{
    [Fact]
    public async Task Access_denied_does_not_register_or_connect()
    {
        var device = new FakeTransportDevice("transport-denied") { AccessStatus = "Denied" };
        await using var service = CreateService(device);

        var result = await service.ActivateAsync(device.DeviceId);

        Assert.False(result.Succeeded);
        Assert.Equal("AccessDenied", result.Status);
        Assert.Equal("Denied", result.AccessStatus);
        Assert.False(result.IsRegistered);
        Assert.False(result.IsConnected);
        Assert.Equal(CallState.Error, service.State);
        Assert.Equal(0, device.RegisterCalls);
        Assert.Equal(0, device.ConnectCalls);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task Allowed_access_without_registration_stops_before_connect()
    {
        var device = new FakeTransportDevice("transport-unregistered") { AccessStatus = "Allowed" };
        await using var service = CreateService(device);

        var result = await service.ActivateAsync(device.DeviceId);

        Assert.False(result.Succeeded);
        Assert.Equal("RegistrationFailed", result.Status);
        Assert.True(result.AccessStatus == "Allowed");
        Assert.False(result.IsRegistered);
        Assert.False(result.IsConnected);
        Assert.Equal(1, device.RegisterCalls);
        Assert.Equal(0, device.ConnectCalls);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task Registered_transport_with_connect_false_finishes_connection_failed()
    {
        var device = new FakeTransportDevice("transport-connect-failed")
        {
            AccessStatus = "Allowed",
            RegisterResult = true,
            ConnectResult = false,
        };
        await using var service = CreateService(device);

        var result = await service.ActivateAsync(device.DeviceId);

        Assert.False(result.Succeeded);
        Assert.Equal("ConnectionFailed", result.Status);
        Assert.True(result.IsRegistered);
        Assert.False(result.IsConnected);
        Assert.Equal(1, device.RegisterCalls);
        Assert.Equal(1, device.ConnectCalls);
        Assert.Equal(1, device.DisposeCount);
        Assert.Equal(CallState.Error, service.State);
    }

    [Fact]
    public async Task Successful_activation_reports_registered_connected_and_transport_flags()
    {
        var device = new FakeTransportDevice("transport-success")
        {
            AccessStatus = "Allowed",
            RegisterResult = true,
            ConnectResult = true,
            AudioRoutingStatus = "Active",
            InBandRingingEnabled = true,
        };
        await using var service = CreateService(device);

        var result = await service.ActivateAsync(device.DeviceId);

        Assert.True(result.Succeeded);
        Assert.Equal("Connected", result.Status);
        Assert.Equal("Allowed", result.AccessStatus);
        Assert.True(result.IsRegistered);
        Assert.True(result.IsConnected);
        Assert.Equal(CallState.Connected, service.State);
        Assert.Equal(1, device.RegisterCalls);
        Assert.Equal(1, device.ConnectCalls);
        Assert.Equal(0, device.DisposeCount);
    }

    [Fact]
    public async Task Capability_probe_failure_does_not_create_a_transport_device()
    {
        var device = new FakeTransportDevice("transport-not-supported");
        var service = new HfpPhoneTransportService(_ => device, () => false);
        await using (service)
        {
            var result = await service.ActivateAsync(device.DeviceId);

            Assert.False(result.Succeeded);
            Assert.Equal("NotSupported", result.Status);
            Assert.Equal(0, device.RegisterCalls);
            Assert.Equal(0, device.ConnectCalls);
            Assert.Equal(0, device.DisposeCount);
        }
    }

    [Fact]
    public async Task Activation_exception_returns_exception_status_and_cleans_candidate()
    {
        var exception = new InvalidOperationException("access failure");
        var device = new FakeTransportDevice("transport-exception")
        {
            AccessException = exception,
        };
        await using var service = CreateService(device);

        var result = await service.ActivateAsync(device.DeviceId);

        Assert.False(result.Succeeded);
        Assert.Equal("Exception", result.Status);
        Assert.Equal($"0x{exception.HResult:X8}", result.HResult);
        Assert.Equal(CallState.Error, service.State);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task Cancellation_during_access_propagates_and_cleans_candidate()
    {
        var device = new FakeTransportDevice("transport-cancel") { AccessStatus = "Allowed", WaitForAccess = true };
        await using var service = CreateService(device);
        using var cancellation = new CancellationTokenSource();

        var activation = service.ActivateAsync(device.DeviceId, cancellation.Token);
        await device.AccessStarted.Task;
        cancellation.Cancel();
        device.AllowAccess();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await activation);

        Assert.Equal(CallState.Error, service.State);
        Assert.Equal(0, device.RegisterCalls);
        Assert.Equal(0, device.ConnectCalls);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public async Task Concurrent_activations_serialize_and_dispose_the_previous_active_transport()
    {
        var first = new FakeTransportDevice("transport-first")
        {
            AccessStatus = "Allowed",
            RegisterResult = true,
            ConnectResult = true,
        };
        var second = new FakeTransportDevice("transport-second")
        {
            AccessStatus = "Allowed",
            RegisterResult = true,
            ConnectResult = true,
            WaitForAccess = true,
        };
        await using var service = CreateService(first, second);

        var firstResult = await service.ActivateAsync(first.DeviceId);
        Assert.True(firstResult.Succeeded);
        var secondActivation = service.ActivateAsync(second.DeviceId);
        await second.AccessStarted.Task;
        second.AllowAccess();
        var secondResult = await secondActivation;

        Assert.True(secondResult.Succeeded);
        Assert.Equal(CallState.Connected, service.State);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
    }

    [Fact]
    public async Task Disconnect_releases_local_transport_without_claiming_remote_disconnect()
    {
        var device = new FakeTransportDevice("transport-disconnect")
        {
            AccessStatus = "Allowed",
            RegisterResult = true,
            ConnectResult = true,
        };
        await using var service = CreateService(device);
        Assert.True((await service.ActivateAsync(device.DeviceId)).Succeeded);

        await service.DisconnectAsync();

        Assert.Equal(CallState.Disconnected, service.State);
        Assert.Equal(1, device.DisposeCount);
    }

    private static HfpPhoneTransportService CreateService(params FakeTransportDevice[] devices)
    {
        var byId = devices.ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
        return new HfpPhoneTransportService(deviceId => byId[deviceId], () => true);
    }

    private sealed class FakeTransportDevice : IHfpTransportDevice
    {
        private readonly TaskCompletionSource accessStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allowAccess =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeTransportDevice(string deviceId)
        {
            DeviceId = deviceId;
        }

        public string DeviceId { get; }
        public string Transport { get; set; } = "Bluetooth";
        public string AudioRoutingStatus { get; set; } = "Unknown";
        public bool InBandRingingEnabled { get; set; }
        public string AccessStatus { get; set; } = "Allowed";
        public bool RegisterResult { get; set; }
        public bool ConnectResult { get; set; }
        public bool WaitForAccess { get; set; }
        public Exception? AccessException { get; set; }
        public int RegisterCalls { get; private set; }
        public int ConnectCalls { get; private set; }
        public int DisposeCount { get; private set; }
        public TaskCompletionSource AccessStarted => accessStarted;
        public bool IsRegistered() => RegisterResult;

        public async Task<string> RequestAccessAsync()
        {
            accessStarted.TrySetResult();
            if (WaitForAccess)
            {
                await allowAccess.Task;
            }
            if (AccessException is not null)
            {
                throw AccessException;
            }
            return AccessStatus;
        }

        public void RegisterApp() => RegisterCalls++;

        public Task<bool> ConnectAsync()
        {
            ConnectCalls++;
            return Task.FromResult(ConnectResult);
        }

        public void AllowAccess() => allowAccess.TrySetResult();

        public void Dispose() => DisposeCount++;
    }
}
