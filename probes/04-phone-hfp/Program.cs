using BTHaven.Probes.Common;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;

ProbeLog.Header("04-phone-hfp");
var arguments = ProbeArguments.Parse(args);
const string typeName = "Windows.ApplicationModel.Calls.PhoneLineTransportDevice";
const string contractName = "Windows.ApplicationModel.Calls.CallsPhoneContract";
var typePresent = ApiInformation.IsTypePresent(typeName);
var contractV5Present = ApiInformation.IsApiContractPresent(contractName, 5);
ProbeLog.Event("HFP.ApiPresence", new { typeName, typePresent, contractName, contractV5Present });

string selector;
try
{
    selector = PhoneLineTransportDevice.GetDeviceSelector();
    ProbeLog.Event("HFP.Selector.Created", new { selector });
}
catch (Exception exception)
{
    ProbeLog.Error("HFP.GetDeviceSelector", exception);
    return;
}

try
{
    var devices = await DeviceInformation.FindAllAsync(selector);
    ProbeLog.Event("HFP.TransportDevicesFound", new { count = devices.Count });
    if (devices.Count == 0)
    {
        ProbeLog.Event("HFP.TransportUnavailable", new { reason = "No PhoneLineTransportDevice was returned for the current user/system state" });
        if (arguments.Has("--request-access") || arguments.Has("--register") || arguments.Has("--connect"))
        {
            ProbeLog.Event("HFP.TransportOperation.NotRun", new
            {
                reason = "No transport device was available; access, registration, and connection APIs require a concrete transport device",
                requestedAccess = arguments.Has("--request-access"),
                requestedRegistration = arguments.Has("--register"),
                requestedConnect = arguments.Has("--connect"),
            });
        }
    }

    foreach (var info in devices)
    {
        PhoneLineTransportDevice? transportDevice = null;
        try
        {
            transportDevice = PhoneLineTransportDevice.FromId(info.Id);
            ProbeLog.Event("HFP.TransportObserved", new
            {
                info.Id,
                info.Name,
                deviceId = transportDevice.DeviceId,
                transport = transportDevice.Transport.ToString(),
                audioRoutingStatus = transportDevice.AudioRoutingStatus.ToString(),
                inBandRingingEnabled = transportDevice.InBandRingingEnabled,
                isRegistered = transportDevice.IsRegistered(),
            });

            if (arguments.Has("--request-access"))
            {
                var access = await transportDevice.RequestAccessAsync();
                ProbeLog.Event("HFP.AccessResult", new { info.Id, access = access.ToString() });
            }

            if (arguments.Has("--register"))
            {
                transportDevice.RegisterApp();
                ProbeLog.Event("HFP.Registered", new { info.Id, isRegistered = transportDevice.IsRegistered() });
            }

            if (arguments.Has("--connect"))
            {
                var connected = await transportDevice.ConnectAsync();
                ProbeLog.Event("HFP.ConnectResult", new { info.Id, connected, audioRoutingStatus = transportDevice.AudioRoutingStatus.ToString() });
            }
        }
        catch (Exception exception)
        {
            ProbeLog.Error("HFP.TransportOperation", exception, new
            {
                info.Id,
                info.Name,
                requestedAccess = arguments.Has("--request-access"),
                requestedRegistration = arguments.Has("--register"),
                requestedConnect = arguments.Has("--connect"),
            });
        }
    }
}
catch (Exception exception)
{
    ProbeLog.Error("HFP.EnumerateTransportDevices", exception, new { selector });
}

ProbeLog.Event("HFP.ProbeBoundary", new
{
    genericHfpHandsFreeRoleProven = false,
    note = "PhoneLineTransportDevice evidence is not, by itself, proof that a third-party desktop app can register as a generic HFP Hands-Free Unit for an arbitrary phone.",
});
ProbeLog.Event("Probe.Completed");
