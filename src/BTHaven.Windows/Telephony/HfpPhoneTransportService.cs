using BTHaven.Core.Calls;
using BTHaven.Core.Contracts;
using BTHaven.Windows.Diagnostics;
using Windows.Devices.Enumeration;

namespace BTHaven.Windows.Telephony;

public sealed class HfpPhoneTransportService : IPhoneTransport, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly IWindowsDiagnosticLogger logger;
    private readonly Func<string, IHfpTransportDevice> deviceFactory;
    private readonly Func<bool> capabilityProbe;
    private IHfpTransportDevice? activeDevice;
    private CallState state = CallState.Disconnected;

    public HfpPhoneTransportService(IWindowsDiagnosticLogger? logger = null)
        : this(HfpTransportAdapters.FromId, HfpTransportAdapters.IsSupported, logger)
    {
    }

    internal HfpPhoneTransportService(
        Func<string, IHfpTransportDevice> deviceFactory,
        Func<bool> capabilityProbe,
        IWindowsDiagnosticLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(deviceFactory);
        ArgumentNullException.ThrowIfNull(capabilityProbe);
        this.deviceFactory = deviceFactory;
        this.capabilityProbe = capabilityProbe;
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
        var typePresent = HfpTransportAdapters.IsTypePresent();
        var contractPresent = HfpTransportAdapters.IsContractPresent();
        logger.Info("HFP.Discovery.Started", new Dictionary<string, object?>
        {
            ["typeName"] = "Windows.ApplicationModel.Calls.PhoneLineTransportDevice",
            ["typePresent"] = typePresent,
            ["contractName"] = "Windows.ApplicationModel.Calls.CallsPhoneContract",
            ["contractV5Present"] = contractPresent,
        });

        try
        {
            var selector = HfpTransportAdapters.GetDeviceSelector();
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
                    using var transportDevice = deviceFactory(info.Id);
                    var model = new PhoneLineTransportModel
                    {
                        Id = info.Id,
                        Name = info.Name,
                        DeviceId = transportDevice.DeviceId,
                        Transport = transportDevice.Transport,
                        AudioRoutingStatus = transportDevice.AudioRoutingStatus,
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

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IHfpTransportDevice? candidate = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCurrent();
            if (!capabilityProbe())
            {
                SetState(CallState.Error);
                return new PhoneLineTransportActivationResult
                {
                    Succeeded = false,
                    Status = "NotSupported",
                    Message = "A API PhoneLineTransportDevice não está disponível nesta versão do Windows.",
                    IsRegistered = false,
                    IsConnected = false,
                };
            }

            candidate = deviceFactory(transportDeviceId);
            cancellationToken.ThrowIfCancellationRequested();
            var accessStatus = await candidate.RequestAccessAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
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
            var connected = await candidate.ConnectAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            logger.Info("HFP.Connection.Result", new Dictionary<string, object?>
            {
                ["deviceId"] = transportDeviceId,
                ["connected"] = connected,
                ["audioRoutingStatus"] = candidate.AudioRoutingStatus,
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
        finally
        {
            candidate?.Dispose();
            lifecycleGate.Release();
        }
    }

    public async Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var result = await ActivateAsync(deviceId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisconnectCurrent();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private void DisconnectCurrent()
    {
        IHfpTransportDevice? oldDevice;
        lock (sync)
        {
            oldDevice = activeDevice;
            activeDevice = null;
            state = CallState.Disconnecting;
        }

        logger.Warning("HFP.Connection.DisconnectNotExposed", new Dictionary<string, object?>
        {
            ["reason"] = "PhoneLineTransportDevice has no public disconnect method",
        });
        oldDevice?.Dispose();
        SetState(CallState.Disconnected);
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
