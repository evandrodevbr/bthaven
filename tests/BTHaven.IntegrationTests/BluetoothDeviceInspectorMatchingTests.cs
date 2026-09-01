using BTHaven.Core.Devices;
using BTHaven.Windows.Bluetooth;
using BTHaven.Core.Calls;
using BTHaven.Windows.Audio;


namespace BTHaven.IntegrationTests;

public sealed class BluetoothDeviceInspectorMatchingTests
{
    [Fact]
    public void Explicit_classic_and_ble_endpoint_ids_win_over_shared_container_id()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "container:CONTAINER-01",
            ContainerId = "container-01",
            Name = "Phone",
            Address = "80:54:2D:51:3B:D6",
            Transport = BluetoothTransport.DualMode,
            Endpoints =
            [
                new BluetoothEndpointReference
                {
                    Id = "classic-real-id",
                    Transport = BluetoothTransport.Classic,
                    ContainerId = "container-01",
                    Address = "80:54:2D:51:3B:D6",
                },
                new BluetoothEndpointReference
                {
                    Id = "ble-real-id",
                    Transport = BluetoothTransport.LowEnergy,
                    ContainerId = "container-01",
                    Address = "80:54:2D:51:3B:D6",
                },
            ],
        };
        var candidates = new[]
        {
            Candidate("classic-real-id", "Phone", "container-01", "80-54-2d-51-3b-d6"),
            Candidate("ble-real-id", "Phone", "container-01", "80:54:2D:51:3B:D6"),
            Candidate("unrelated-container-id", "Other interface", "container-01", "AA:BB:CC:DD:EE:FF"),
        };

        var result = BluetoothDeviceInspector.MatchCandidates(model, candidates);

        Assert.Equal(BluetoothInspectionMatchStatus.Matched, result.Status);
        Assert.Equal(2, result.Matches.Count);
        Assert.Contains(result.Matches, match => match.Id == "classic-real-id");
        Assert.Contains(result.Matches, match => match.Id == "ble-real-id");
    }

    [Fact]
    public void Equal_name_without_identity_or_endpoint_match_is_ambiguous()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "endpoint:Unknown:logical-endpoint",
            Name = "Phone",
            Transport = BluetoothTransport.Unknown,
        };
        var candidates = new[]
        {
            Candidate("candidate-a", "Phone", null, null),
            Candidate("candidate-b", "Phone", null, null),
        };

        var result = BluetoothDeviceInspector.MatchCandidates(model, candidates);

        Assert.Equal(BluetoothInspectionMatchStatus.Ambiguous, result.Status);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Unique_name_remains_a_valid_last_resort_match()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "endpoint:Unknown:logical-endpoint",
            Name = "Phone",
            Transport = BluetoothTransport.Unknown,
        };
        var candidates = new[]
        {
            Candidate("candidate-a", "Phone", null, null),
            Candidate("candidate-b", "Keyboard", null, null),
        };

        var result = BluetoothDeviceInspector.MatchCandidates(model, candidates);

        Assert.Equal(BluetoothInspectionMatchStatus.Matched, result.Status);
        Assert.Collection(result.Matches, match => Assert.Equal("candidate-a", match.Id));
    }

    [Fact]
    public void Exact_endpoint_match_beats_shared_container_and_name()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "container:CONTAINER-01",
            ContainerId = "container-01",
            Name = "Phone",
            Address = "80:54:2D:51:3B:D6",
            Transport = BluetoothTransport.DualMode,
            Endpoints =
            [
                new BluetoothEndpointReference
                {
                    Id = "exact-endpoint",
                    Transport = BluetoothTransport.Classic,
                    ContainerId = "container-01",
                    Address = "80:54:2D:51:3B:D6",
                },
            ],
        };
        var candidates = new[]
        {
            new RemoteAudioDeviceInfo("exact-endpoint", "Different", "container-01", null),
            new RemoteAudioDeviceInfo("other-endpoint", "Phone", "container-01", null),
        };

        var matches = BluetoothDeviceCorrelation.FindMatches(model, candidates);

        Assert.Collection(matches, target => Assert.Equal("exact-endpoint", target.Id));
    }

    [Fact]
    public void Address_match_normalizes_separators_and_case()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "address:80542D513BD6",
            Name = "Phone",
            Address = "80:54:2d:51:3b:d6",
            Transport = BluetoothTransport.Classic,
        };
        var candidates = new[]
        {
            new RemoteAudioDeviceInfo("audio-80542d513bd6", "Other", null, null),
        };

        var matches = BluetoothDeviceCorrelation.FindMatches(model, candidates);

        Assert.Collection(matches, target => Assert.Equal("audio-80542d513bd6", target.Id));
    }

    [Fact]
    public void Unique_name_is_the_last_resort_match()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "endpoint:Unknown:logical-endpoint",
            Name = "Phone",
            Transport = BluetoothTransport.Unknown,
        };
        var candidates = new[]
        {
            new RemoteAudioDeviceInfo("candidate-a", "Phone", null, null),
            new RemoteAudioDeviceInfo("candidate-b", "Keyboard", null, null),
        };

        var matches = BluetoothDeviceCorrelation.FindMatches(model, candidates);

        Assert.Collection(matches, target => Assert.Equal("candidate-a", target.Id));
    }

    [Fact]
    public void Hfp_device_id_is_an_exact_endpoint_match()
    {
        var model = new BluetoothDeviceModel
        {
            Id = "container:CONTAINER-01",
            ContainerId = "container-01",
            Name = "Phone",
            Address = "80:54:2D:51:3B:D6",
            Transport = BluetoothTransport.DualMode,
            Endpoints =
            [
                new BluetoothEndpointReference
                {
                    Id = "phone-line-device-id",
                    Transport = BluetoothTransport.Classic,
                },
            ],
        };
        var candidates = new[]
        {
            new PhoneLineTransportModel
            {
                Id = "transport-id",
                DeviceId = "phone-line-device-id",
                Name = "Different",
            },
            new PhoneLineTransportModel
            {
                Id = "other-transport",
                Name = "Phone",
            },
        };

        var matches = BluetoothDeviceCorrelation.FindMatches(model, candidates);

        Assert.Collection(matches, target => Assert.Equal("transport-id", target.Id));
    }

    private static BluetoothInspectionCandidate Candidate(
        string id,
        string name,
        string? containerId,
        string? address) => new(id, name, containerId, address);
}
