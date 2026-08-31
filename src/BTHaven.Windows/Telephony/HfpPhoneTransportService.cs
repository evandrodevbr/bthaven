using BTHaven.Core.Calls;
using BTHaven.Core.Contracts;
using BTHaven.Windows.Diagnostics;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;

namespace BTHaven.Windows.Telephony;

public sealed class HfpPhoneTransportService : IPhoneTransport, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly IWindowsDiagnosticLogger logger;
    private PhoneLineTransportDevice? activeDevice;
    private CallState state = CallState.Disconnected;

    public HfpPhoneTransportService(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public CallState State
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    public async Task<IReadOnlyList<PhoneLineTransportModel>> GetAvailableDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string typeName = "Windows.ApplicationModel.Calls.PhoneLineTransportDevice";
        const string contractName = "Windows.ApplicationModel.Calls.CallsPhoneContract";
        var typePresent = ApiInformation.IsTypePresent(typeName);
        var contractPresent = ApiInformation.IsApiContractPresent(contractName, 5);
        logger.Info("HFP.Discovery.Started", new Dictionary<string, object?>
        {
            ["typeName"] = typeName,
            ["typePresent"] = typePresent,
            ["contractName"] = contractName,
            ["contractV5Present"] = contractPresent,
        });

        try
        {
            var selector = PhoneLineTransportDevice.GetDeviceSelector();
            logger.Debug("HFP.Selector.Created", new Dictionary<string, object?>
            {
                ["selector"] = selector,
            });
            var devices = await DeviceInformation.FindAllAsync(selector);
            var result = new List<PhoneLineTransportModel>(devices.Count);
            foreach (var info in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var transportDevice = PhoneLineTransportDevice.FromId(info.Id);
                    var model = new PhoneLineTransportModel
                    {
                        Id = info.Id,
                        Name = info.Name,
                        DeviceId = transportDevice.DeviceId,
                        Transport = transportDevice.Transport.ToString(),
                        AudioRoutingStatus = transportDevice.AudioRoutingStatus.ToString(),
                        InBandRingingEnabled = transportDevice.InBandRingingEnabled,
                        IsRegistered = transportDevice.IsRegistered(),
                    };
                    result.Add(model);
                    logger.Info("HFP.Transport.Observed", new Dictionary<string, object?>
                    {
                        ["deviceId"] = model.Id,
                        ["name"] = model.Name,
                        ["transport"] = model.Transport,
                        ["audioRoutingStatus"] = model.AudioRoutingStatus,
                        ["inBandRingingEnabled"] = model.InBandRingingEnabled,
                        ["isRegistered"] = model.IsRegistered,
                    });
                }
                catch (Exception exception)
                {
                    logger.Error("HFP.Transport.FromIdFailed", exception, new Dictionary<string, object?>
                    {
                        ["deviceId"] = info.Id,
                        ["name"] = info.Name,
                    });
                }
            }

            logger.Info("HFP.Discovery.Completed", new Dictionary<string, object?>
            {
                ["count"] = result.Count,
                ["selector"] = selector,
            });
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.Info("HFP.Discovery.Cancelled");
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("HFP.Discovery.Failed", exception);
            throw;
        }
    }

    public async Task<PhoneLineTransportActivationResult> ActivateAsync(
        string transportDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportDeviceId);
        cancellationToken.ThrowIfCancellationRequested();
        logger.Info("HFP.Activation.Requested", new Dictionary<string, object?>
        {
            ["deviceId"] = transportDeviceId,
        });

        PhoneLineTransportDevice? candidate = null;
        try
        {
            candidate = PhoneLineTransportDevice.FromId(transportDeviceId);
            var access = await candidate.RequestAccessAsync();
            var accessStatus = access.ToString();
            logger.Info("HFP.Access.Result", new Dictionary<string, object?>
            {
                ["deviceId"] = transportDeviceId,
                ["access"] = accessStatus,
            });
            if (!string.Equals(accessStatus, "Allowed", StringComparison.OrdinalIgnoreCase))
            {
                SetState(CallState.Error);
                return new PhoneLineTransportActivationResult
                {
                    Succeeded = false,
                    Status = "AccessDenied",
                    Message = "O Windows negou o acesso ao transporte telefônico.",
                    AccessStatus = accessStatus,
                    IsRegistered = candidate.IsRegistered(),
                    IsConnected = false,
                };
            }

            candidate.RegisterApp();
            var isRegistered = candidate.IsRegistered();
            logger.Info("HFP.Registration.Result", new Dictionary<string, object?>
            {
                ["deviceId"] = transportDeviceId,
                ["isRegistered"] = isRegistered,
            });
            if (!isRegistered)
            {
                SetState(CallState.Error);
                return new PhoneLineTransportActivationResult
                {
                    Succeeded = false,
                    Status = "RegistrationFailed",
                    Message = "O transporte não confirmou o registro do aplicativo.",
                    AccessStatus = accessStatus,
                    IsRegistered = false,
                    IsConnected = false,
                };
            }

            SetState(CallState.Connecting);
            var connected = await candidate.ConnectAsync();
            logger.Info("HFP.Connection.Result", new Dictionary<string, object?>
            {
                ["deviceId"] = transportDeviceId,
                ["connected"] = connected,
                ["audioRoutingStatus"] = candidate.AudioRoutingStatus.ToString(),
            });
            if (!connected)
            {
                SetState(CallState.Error);
                return new PhoneLineTransportActivationResult
                {
                    Succeeded = false,
                    Status = "ConnectionFailed",
                    Message = "O transporte HFP não conectou.",
                    AccessStatus = accessStatus,
                    IsRegistered = true,
                    IsConnected = false,
                };
            }

            lock (sync)
            {
                activeDevice = candidate;
                candidate = null;
                state = CallState.Connected;
            }
            return new PhoneLineTransportActivationResult
            {
                Succeeded = true,
                Status = "Connected",
                Message = "Transporte HFP conectado; o roteamento da chamada depende do estado da chamada no telefone.",
                AccessStatus = accessStatus,
                IsRegistered = true,
                IsConnected = true,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(CallState.Error);
            logger.Info("HFP.Activation.Cancelled", new Dictionary<string, object?>
            {
                ["deviceId"] = transportDeviceId,
            });
            throw;
        }
        catch (Exception exception)
        {
            SetState(CallState.Error);
            logger.Error("HFP.Activation.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = transportDeviceId,
            });
            return new PhoneLineTransportActivationResult
            {
                Succeeded = false,
                Status = "Exception",
                Message = "A ativação HFP falhou; consulte os logs para o HRESULT.",
                IsRegistered = false,
                IsConnected = false,
            };
        }
    }

    public async Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var result = await ActivateAsync(deviceId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            activeDevice = null;
            state = CallState.Disconnecting;
        }
        logger.Warning("HFP.Connection.DisconnectNotExposed", new Dictionary<string, object?>
        {
            ["reason"] = "PhoneLineTransportDevice has no public disconnect method",
        });
        SetState(CallState.Disconnected);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    private void SetState(CallState nextState)
    {
        lock (sync)
        {
            state = nextState;
        }
        logger.Info("HFP.State.Changed", new Dictionary<string, object?>
        {
            ["state"] = nextState.ToString(),
        });
    }
}
