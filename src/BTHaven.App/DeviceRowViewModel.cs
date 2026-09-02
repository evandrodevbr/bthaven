using BTHaven.Core.Battery;
using BTHaven.Core.Devices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BTHaven_App;

public sealed class DeviceRowViewModel
{
    public DeviceRowViewModel(
        BluetoothDeviceModel model,
        BatteryTelemetryEntry? telemetry = null,
        bool mediaEnabled = false)
    {
        Id = model.Id;
        Name = model.Name;
        Summary = BuildSummary(model);
        StatusText = BuildStatus(model);
        BatteryText = BuildBatteryText(telemetry);
        ConnectionTransportText =
            BluetoothEndpointSelection.SelectPreferredConnection(model)?.Transport.ToString()
            ?? model.Transport.ToString();
        TelemetryText = BuildTelemetryText(model, ConnectionTransportText);
        TelemetryAutomationText = BuildTelemetryAutomationText(model, telemetry, ConnectionTransportText);
        MediaAutomationName = $"Áudio de mídia para {model.Name}";
        IconGlyph = BuildGlyph(model.Category);
        MediaToggleEnabled = model.IsPaired
            && model.IsPresent
            && (model.Category is BluetoothDeviceCategory.Smartphone
                or BluetoothDeviceCategory.Headphones
                or BluetoothDeviceCategory.Speaker
                || model.Capabilities.HasFlag(BluetoothCapabilities.MediaAudio));
        MediaEnabled = mediaEnabled;
        IsConnected = model.IsConnected;
    }

    public string Id { get; }
    public string Name { get; }
    public string Summary { get; }
    public string StatusText { get; }
    public string BatteryText { get; }
    public string TelemetryText { get; }
    public string TelemetryAutomationText { get; }
    public string ConnectionTransportText { get; }
    public string MediaAutomationName { get; }
    public string IconGlyph { get; }
    public bool IsConnected { get; }

    public bool MediaToggleEnabled { get; }
    public bool MediaEnabled { get; }

    private static string BuildBatteryText(BatteryTelemetryEntry? telemetry)
    {
        if (telemetry?.Current is { } current)
        {
            return current.Percentage is int percentage
                ? BatteryTextFormatter.FormatPercentage(percentage, current.IsCharging)
                : current.IsCharging switch { true => "Carregando", false => "Não carregando", _ => "—" };
        }

        if (telemetry?.LastAvailable is not { } lastAvailable)
        {
            return "—";
        }

        var lastValue = lastAvailable.Percentage is int lastPercentage
            ? BatteryTextFormatter.FormatPercentage(lastPercentage, lastAvailable.IsCharging)
            : lastAvailable.IsCharging switch
            {
                true => "carregando",
                false => "não carregando",
                _ => null,
            };
        return lastValue is null
            ? "—"
            : $"Última bateria: {lastValue} · {lastAvailable.LastUpdated.ToLocalTime():HH:mm}";
    }

    private static string BuildTelemetryText(
        BluetoothDeviceModel model,
        string connectionTransportText)
    {
        var rssi = model.Rssi is int value ? FormatRssi(value) : null;
        var observed = $"observado {model.LastUpdated.ToLocalTime():HH:mm:ss}";
        return string.Join(
            " · ",
            new[] { connectionTransportText, rssi, observed }.Where(value => value is not null));
    }

    private static string BuildTelemetryAutomationText(
        BluetoothDeviceModel model,
        BatteryTelemetryEntry? telemetry,
        string connectionTransportText)
    {
        var battery = BuildBatteryAutomationText(telemetry);
        var rssi = model.Rssi is int value ? $" · {FormatRssi(value)}" : string.Empty;
        return $"{battery} · {connectionTransportText}{rssi} · observado {model.LastUpdated.ToLocalTime():HH:mm:ss}";
    }

    private static string BuildBatteryAutomationText(BatteryTelemetryEntry? telemetry)
    {
        if (telemetry?.Current is { } current)
        {
            return current.Percentage is int percentage
                ? $"Bateria {BatteryTextFormatter.FormatPercentage(percentage, current.IsCharging)}"
                : current.IsCharging switch
                {
                    true => "Bateria carregando; porcentagem indisponível",
                    false => "Bateria não está carregando; porcentagem indisponível",
                    _ => "Bateria indisponível",
                };
        }

        if (telemetry?.LastAvailable is { } lastAvailable)
        {
            var prefix = telemetry.Status == BatteryTelemetryStatus.Loading
                ? "Consulta de bateria em andamento"
                : "Bateria atual indisponível";
            var lastValue = lastAvailable.Percentage is int lastPercentage
                ? BatteryTextFormatter.FormatPercentage(lastPercentage, lastAvailable.IsCharging)
                : lastAvailable.IsCharging switch
                {
                    true => "carregando; porcentagem indisponível",
                    false => "não carregando; porcentagem indisponível",
                    _ => "indisponível",
                };
            return $"{prefix} · Última bateria: {lastValue} · {lastAvailable.LastUpdated.ToLocalTime():HH:mm}";
        }

        return telemetry?.Status == BatteryTelemetryStatus.Loading
            ? "Consulta de bateria em andamento"
            : "Bateria indisponível";
    }

    private static string FormatRssi(int value) =>
        value < 0 ? $"−{-(long)value} dBm" : $"{value} dBm";

    private static string BuildSummary(BluetoothDeviceModel model)
    {
        var category = model.Category switch
        {
            BluetoothDeviceCategory.Smartphone => "Smartphone",
            BluetoothDeviceCategory.Headphones => "Fones de ouvido",
            BluetoothDeviceCategory.Speaker => "Caixa de som",
            BluetoothDeviceCategory.Mouse => "Mouse",
            BluetoothDeviceCategory.Keyboard => "Teclado",
            BluetoothDeviceCategory.Controller => "Controle",
            BluetoothDeviceCategory.Peripheral => "Periférico",
            _ => "Dispositivo Bluetooth",
        };
        return $"{category} · {model.Transport}";
    }

    private static string BuildStatus(BluetoothDeviceModel model)
    {
        if (model.IsConnected)
        {
            return "Conectado";
        }
        if (model.IsPaired && model.IsPresent)
        {
            return "Emparelhado · presente";
        }
        if (model.IsPaired)
        {
            return "Emparelhado · desconectado";
        }
        return model.IsPresent ? "Presente" : "Desconectado";
    }

    private static string BuildGlyph(BluetoothDeviceCategory category)
    {
        return category switch
        {
            BluetoothDeviceCategory.Smartphone => "\uE8EA",
            BluetoothDeviceCategory.Headphones => "\uE7F6",
            BluetoothDeviceCategory.Speaker => "\uE7F5",
            BluetoothDeviceCategory.Mouse => "\uE962",
            BluetoothDeviceCategory.Keyboard => "\uE765",
            BluetoothDeviceCategory.Controller => "\uE7FC",
            _ => "\uE702",
        };
    }
}

// WinUI 3 nao tem DataStateTrigger (XamlCompiler WMC0001); a visibilidade dos
// TextBlocks de status alternam a foreground ThemeResource apropriada no XAML.
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ^ parameter is "invert" ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
