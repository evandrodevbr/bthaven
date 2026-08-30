using BTHaven.Core.Devices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace BTHaven_App;

public sealed class DeviceRowViewModel
{
    public DeviceRowViewModel(BluetoothDeviceModel model)
    {
        Id = model.Id;
        Name = model.Name;
        Summary = BuildSummary(model);
        StatusText = BuildStatus(model);
        BatteryText = model.Battery?.Percentage is int percentage
            ? $"{percentage}%"
            : "—";
        IconGlyph = BuildGlyph(model.Category);
        StatusBrush = new SolidColorBrush(model.IsConnected
            ? ColorHelper.FromArgb(255, 76, 188, 118)
            : ColorHelper.FromArgb(255, 142, 151, 164));
    }

    public string Id { get; }
    public string Name { get; }
    public string Summary { get; }
    public string StatusText { get; }
    public string BatteryText { get; }
    public string IconGlyph { get; }
    public SolidColorBrush StatusBrush { get; }

    private static string BuildSummary(BluetoothDeviceModel model)
    {
        var category = model.Category switch
        {
            BluetoothDeviceCategory.Smartphone => "Smartphone",
            BluetoothDeviceCategory.Headphones => "Headphones",
            BluetoothDeviceCategory.Speaker => "Speaker",
            BluetoothDeviceCategory.Mouse => "Mouse",
            BluetoothDeviceCategory.Keyboard => "Keyboard",
            BluetoothDeviceCategory.Controller => "Controller",
            BluetoothDeviceCategory.Peripheral => "Peripheral",
            _ => "Bluetooth device",
        };
        return $"{category} · {model.Transport}";
    }

    private static string BuildStatus(BluetoothDeviceModel model)
    {
        if (model.IsConnected)
        {
            return "● Conectado";
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
