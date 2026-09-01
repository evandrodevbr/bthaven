using BTHaven.Core.Battery;
using BTHaven.Core.Audio;
using BTHaven.Core.Devices;

namespace BTHaven.Core.Tests;

public sealed class BluetoothInspectionModelsTests
{
    [Fact]
    public void Snapshot_preserves_state_provenance_and_battery_observations()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new BluetoothDeviceInspectionSnapshot
        {
            DeviceId = "ble-endpoint",
            Name = "Test phone",
            ContainerId = "container",
            IsPaired = true,
            IsConnected = false,
            IsPresent = true,
            DeviceProperties =
            [
                new BluetoothObservedProperty
                {
                    Key = "System.Devices.Aep.IsConnected",
                    Type = "Boolean",
                    Value = "False",
                },
            ],
            BatteryObservations =
            [
                new BluetoothBatteryObservation
                {
                    Source = "gatt-0x180f-0x2a19",
                    Status = "Unreachable",
                    ObservedAt = observedAt,
                    Message = "Battery service was not returned",
                },
            ],
            ProfileObservations =
            [
                new BluetoothProfileObservation
                {
                    Profile = "A2DP",
                    Source = "AudioPlaybackConnection",
                    Status = "Available",
                    ObservedAt = observedAt,
                },
            ],
            RemoteVolume = RemoteVolumeStatus.NotExposed("Windows.AVRCP", "not exposed"),
        };

        Assert.True(snapshot.IsPaired);
        Assert.False(snapshot.IsConnected);
        Assert.True(snapshot.IsPresent);
        Assert.Equal("Boolean", snapshot.DeviceProperties[0].Type);
        Assert.Equal("Unreachable", snapshot.BatteryObservations[0].Status);
        Assert.Equal("A2DP", snapshot.ProfileObservations[0].Profile);
        Assert.Equal(RemoteVolumeAvailability.NotExposed, snapshot.RemoteVolume?.Availability);
        Assert.Equal(observedAt, snapshot.BatteryObservations[0].ObservedAt);
    }

    [Fact]
    public void Battery_observation_can_express_unavailable_without_inventing_percentage()
    {
        var observation = new BluetoothBatteryObservation
        {
            Source = "windows-properties",
            Status = "Unavailable",
            Confidence = BatteryConfidence.Unknown,
        };

        Assert.Null(observation.Percentage);
        Assert.Null(observation.IsCharging);
        Assert.Equal(BatteryConfidence.Unknown, observation.Confidence);
    }
}
