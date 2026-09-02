namespace BTHaven.Windows.Battery;

internal static class WindowsBatteryPropertyDecoder
{
    public static WindowsBatteryPropertyValues Decode(
        byte? batteryLife,
        byte? batteryPlusCharging,
        byte? chargingState)
    {
        var plusPercentage = batteryPlusCharging switch
        {
            <= 100 => batteryPlusCharging.Value,
            >= 101 and <= 200 => batteryPlusCharging.Value - 100,
            _ => (int?)null,
        };
        var plusCharging = batteryPlusCharging switch
        {
            <= 100 => false,
            >= 101 and <= 200 => true,
            _ => (bool?)null,
        };

        var percentage = batteryLife is <= 100
            ? batteryLife.Value
            : plusPercentage;
        var isCharging = chargingState switch
        {
            0 => false,
            1 => true,
            _ => plusCharging,
        };

        return new WindowsBatteryPropertyValues(percentage, isCharging);
    }
}

internal readonly record struct WindowsBatteryPropertyValues(
    int? Percentage,
    bool? IsCharging)
{
    public bool HasData => Percentage is not null || IsCharging is not null;
}
