namespace BTHaven_App;

internal static class BatteryTextFormatter
{
    public static string FormatPercentage(int percentage, bool? isCharging) =>
        isCharging switch
        {
            true => $"{percentage}% · carregando",
            false => $"{percentage}% · não carregando",
            _ => $"{percentage}%",
        };
}
