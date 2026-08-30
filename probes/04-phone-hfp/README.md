# 04 — Phone HFP feasibility

This probe is intentionally conservative. It checks:

- whether `PhoneLineTransportDevice` and `CallsPhoneContract` are present;
- the selector returned by `GetDeviceSelector()`;
- transport devices exposed for the current user/system state;
- optional access, registration, and connection operations on a concrete transport device.

The default run is discovery-only:

```text
dotnet run --project probes/04-phone-hfp -c Release
```

Operations that can change Windows call integration state are opt-in:

```text
dotnet run --project probes/04-phone-hfp -c Release -- --request-access --register --connect
```

If no transport device is returned, those operations are recorded as `NotRun`; that is not an HRESULT denial. The probe does not claim that `PhoneLineTransportDevice` is a generic HFP Hands-Free Unit registration API for arbitrary Android/iPhone devices.
