using Windows.ApplicationModel.Calls;
using Windows.Foundation.Metadata;

namespace BTHaven.Windows.Telephony;

internal interface IHfpTransportDevice : IDisposable
{
    string DeviceId { get; }
    string Transport { get; }
    string AudioRoutingStatus { get; }
    bool InBandRingingEnabled { get; }
    Task<string> RequestAccessAsync();
    void RegisterApp();
    bool IsRegistered();
    Task<bool> ConnectAsync();
}

internal static class HfpTransportAdapters
{
    private const string TypeName = "Windows.ApplicationModel.Calls.PhoneLineTransportDevice";
    private const string ContractName = "Windows.ApplicationModel.Calls.CallsPhoneContract";

    public static bool IsTypePresent() => ApiInformation.IsTypePresent(TypeName);

    public static bool IsContractPresent() =>
        ApiInformation.IsApiContractPresent(ContractName, 5);

    public static bool IsSupported() => IsTypePresent() && IsContractPresent();

    public static string GetDeviceSelector() => PhoneLineTransportDevice.GetDeviceSelector();

    public static IHfpTransportDevice FromId(string deviceId)
    {
        var device = PhoneLineTransportDevice.FromId(deviceId);
        return new WinRtHfpTransportDevice(device);
    }

    private sealed class WinRtHfpTransportDevice : IHfpTransportDevice
    {
        private PhoneLineTransportDevice? device;

        public WinRtHfpTransportDevice(PhoneLineTransportDevice device)
        {
            this.device = device;
        }

        private PhoneLineTransportDevice Current =>
            device ?? throw new ObjectDisposedException(nameof(WinRtHfpTransportDevice));

        public string DeviceId => Current.DeviceId;
        public string Transport => Current.Transport.ToString();
        public string AudioRoutingStatus => Current.AudioRoutingStatus.ToString();
        public bool InBandRingingEnabled => Current.InBandRingingEnabled;

        public async Task<string> RequestAccessAsync()
        {
            var access = await Current.RequestAccessAsync();
            return access.ToString();
        }

        public void RegisterApp() => Current.RegisterApp();

        public bool IsRegistered() => Current.IsRegistered();

        public async Task<bool> ConnectAsync() => await Current.ConnectAsync();

        public void Dispose() => device = null;
    }
}
