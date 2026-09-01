using System.Text;
using BTHaven.Core.Devices;

namespace BTHaven_App;

internal static class BluetoothInspectionTextFormatter
{
    public static string Format(BluetoothDeviceInspectionSnapshot snapshot)
    {
        var text = new StringBuilder();
        text.AppendLine("IDENTIDADE E ESTADO");
        text.AppendLine($"Id: {snapshot.DeviceId}");
        text.AppendLine($"Nome: {snapshot.Name}");
        text.AppendLine($"ContainerId: {snapshot.ContainerId ?? "não exposto"}");
        text.AppendLine($"Fabricante: {snapshot.Manufacturer ?? "não exposto"}");
        text.AppendLine($"Modelo: {snapshot.Model ?? "não exposto"}");
        text.AppendLine($"Endereço: {snapshot.Address ?? "não exposto"}");
        text.AppendLine($"Transporte: {snapshot.Transport}");
        text.AppendLine($"Conexão: {snapshot.ConnectionStatus ?? "desconhecida"}");
        text.AppendLine($"Emparelhado: {FormatNullable(snapshot.IsPaired)} · conectado: {FormatNullable(snapshot.IsConnected)} · presente: {FormatNullable(snapshot.IsPresent)}");
        text.AppendLine($"RSSI: {snapshot.Rssi?.ToString() ?? "não exposto"} · ClassOfDevice: {snapshot.ClassOfDevice ?? "não exposto"}");
        text.AppendLine($"HostName: {snapshot.HostName ?? "não exposto"} · secure pairing: {FormatNullable(snapshot.WasSecureConnectionUsedForPairing)}");

        text.AppendLine();
        text.AppendLine("PROPRIEDADES OBSERVADAS");
        if (snapshot.DeviceProperties.Count == 0)
        {
            text.AppendLine("  nenhuma propriedade retornada");
        }
        foreach (var property in snapshot.DeviceProperties)
        {
            text.AppendLine($"  {property.Key} [{property.Type}] = {property.Value ?? "não exposto"} · {property.Status} · fonte={property.Source}");
        }

        text.AppendLine();
        text.AppendLine("ENDPOINTS ASSOCIADOS");
        if (snapshot.Endpoints.Count == 0)
        {
            text.AppendLine("  nenhum endpoint associado encontrado");
        }
        foreach (var endpoint in snapshot.Endpoints)
        {
            text.AppendLine($"  {endpoint.Kind}: {endpoint.Id} · transporte={endpoint.Transport} · estado={endpoint.Status ?? "desconhecido"}");
            text.AppendLine($"    nome={endpoint.Name ?? "não exposto"} · endereço={endpoint.Address ?? "não exposto"} · container={endpoint.ContainerId ?? "não exposto"}");
            text.AppendLine($"    paired={FormatNullable(endpoint.IsPaired)} · connected={FormatNullable(endpoint.IsConnected)} · present={FormatNullable(endpoint.IsPresent)} · RSSI={endpoint.Rssi?.ToString() ?? "não exposto"}");
        }

        text.AppendLine();
        text.AppendLine("BATERIA");
        foreach (var battery in snapshot.BatteryObservations)
        {
            text.AppendLine($"  {battery.Source}: {battery.Status} · percentual={battery.Percentage?.ToString() ?? "não exposto"} · carregando={FormatNullable(battery.IsCharging)} · confiança={battery.Confidence} · {battery.Message ?? "sem mensagem"}");
        }

        text.AppendLine();
        text.AppendLine("PERFIS");
        foreach (var profile in snapshot.ProfileObservations)
        {
            text.AppendLine($"  {profile.Profile}: {profile.Status} · fonte={profile.Source} · id={profile.DeviceId ?? "não confirmado"} · {profile.Message ?? "sem mensagem"}");
        }

        text.AppendLine();
        text.AppendLine("VOLUME REMOTO");
        if (snapshot.RemoteVolume is { } volume)
        {
            text.AppendLine($"  {volume.Availability} · fonte={volume.Source} · nível={volume.Level?.ToString("P0") ?? "não exposto"} · {volume.Message ?? "sem mensagem"}");
        }
        else
        {
            text.AppendLine("  nenhum estado retornado");
        }

        text.AppendLine();
        text.AppendLine("GATT");
        if (snapshot.GattServices.Count == 0)
        {
            text.AppendLine("  nenhum serviço GATT enumerado");
        }
        foreach (var service in snapshot.GattServices)
        {
            text.AppendLine($"  serviço {service.Uuid} handle={service.AttributeHandle} status={service.Status}");
            foreach (var characteristic in service.Characteristics)
            {
                text.AppendLine($"    característica {characteristic.Uuid} handle={characteristic.AttributeHandle} status={characteristic.Status} descritores={characteristic.DescriptorStatus ?? "não consultado"} propriedades={characteristic.Properties} descrição={characteristic.UserDescription ?? "não exposta"}");
                foreach (var descriptor in characteristic.Descriptors)
                {
                    text.AppendLine($"      descritor {descriptor.Uuid} handle={descriptor.AttributeHandle}");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("RFCOMM");
        if (snapshot.RfcommServices.Count == 0)
        {
            text.AppendLine("  nenhum serviço RFCOMM enumerado");
        }
        foreach (var service in snapshot.RfcommServices)
        {
            text.AppendLine($"  {service.ServiceId} · {service.KnownName ?? "nome desconhecido"} · status={service.Status}");
        }

        text.AppendLine();
        text.AppendLine("DIAGNÓSTICOS");
        if (snapshot.Diagnostics.Count == 0)
        {
            text.AppendLine("  nenhum diagnóstico");
        }
        foreach (var diagnostic in snapshot.Diagnostics)
        {
            text.AppendLine($"  {diagnostic.Operation}: {diagnostic.Status ?? "sem status"} · fonte={diagnostic.Source} · HRESULT={diagnostic.HResult ?? "n/a"} · {diagnostic.Message ?? "sem mensagem"}");
        }
        return text.ToString();
    }

    private static string FormatNullable(bool? value)
    {
        return value switch
        {
            true => "sim",
            false => "não",
            _ => "desconhecido",
        };
    }
}
