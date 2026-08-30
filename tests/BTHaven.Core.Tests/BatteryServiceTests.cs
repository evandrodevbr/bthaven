using BTHaven.Core.Battery;
using BTHaven.Core.Devices;
using BTHaven.Core.Contracts;

namespace BTHaven.Core.Tests;

public sealed class BatteryServiceTests
{
    [Fact]
    public async Task Aggregator_uses_the_first_provider_that_returns_real_data()
    {
        var device = new BluetoothDeviceModel { Id = "device", Name = "Device" };
        var first = new StubBatteryProvider("first", BatteryState.Unavailable("first"));
        var secondState = new BatteryState
        {
            Percentage = 76,
            IsCharging = false,
            Source = "second",
            LastUpdated = DateTimeOffset.UtcNow,
            Confidence = BatteryConfidence.High,
        };
        var second = new StubBatteryProvider("second", secondState);
        var service = new BatteryService([first, second]);

        var result = await service.GetBatteryAsync(device);

        Assert.Same(secondState, result);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task Aggregator_continues_after_a_provider_error_and_returns_unavailable_when_all_fail()
    {
        var device = new BluetoothDeviceModel { Id = "device", Name = "Device" };
        var failing = new StubBatteryProvider("failing", new InvalidOperationException("probe failure"));
        var unavailable = new StubBatteryProvider("empty", BatteryState.Unavailable("empty"));
        var service = new BatteryService([failing, unavailable]);

        var result = await service.GetBatteryAsync(device);

        Assert.Null(result.Percentage);
        Assert.Null(result.IsCharging);
        Assert.Equal("unavailable", result.Source);
        Assert.Equal(1, failing.Calls);
        Assert.Equal(1, unavailable.Calls);
    }

    private sealed class StubBatteryProvider : IBatteryProvider
    {
        private readonly object result;
        public StubBatteryProvider(string name, object result) { Name = name; this.result = result; }
        public string Name { get; }
        public int Calls { get; private set; }

        public Task<BatteryState> GetBatteryAsync(BluetoothDeviceModel device, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (result is Exception exception)
            {
                throw exception;
            }

            return Task.FromResult((BatteryState)result);
        }
    }
}
