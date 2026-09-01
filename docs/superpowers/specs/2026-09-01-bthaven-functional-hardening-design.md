# BTHaven Functional Hardening — Design

**Status:** aprovado para planejamento incremental em 2026-09-01.

## Objetivo

Corrigir os Required encontrados na revisão funcional sem introduzir uma segunda política de identidade, sem afirmar suporte de chamadas que o Windows ainda não provou e sem acoplar o domínio a objetos WinRT.

## Decisão de desenho

O domínio passa a expor `BluetoothDeviceModel.Id` como **identidade lógica estável**. Os identificadores reais de endpoint deixam de ser implícitos: cada dispositivo mantém referências explícitas para os endpoints Classic e BLE. Nenhum consumidor de domínio usa `Id` como se fosse automaticamente um `DeviceInformation.Id`.

A identidade é derivada nesta ordem:

1. `ContainerId` não vazio, normalizado de forma determinística;
2. endereço Bluetooth não vazio, normalizado removendo separadores e usando maiúsculas;
3. um identificador isolado pelo endpoint (`transport + endpointId`) quando não houver identidade confiável para mesclar.

Se dois endpoints não compartilham `ContainerId` nem endereço equivalente, eles **não** são mesclados por nome. Nome é apenas fallback de consulta e, quando retornar mais de um candidato, o resultado é `Ambiguous`, nunca uma escolha arbitrária.

### Modelo e eventos

A extensão aditiva do Core deve conter, no mínimo:

```csharp
public sealed record BluetoothEndpointReference
{
    public required string Id { get; init; }
    public required BluetoothTransport Transport { get; init; }
    public string? ContainerId { get; init; }
    public string? Address { get; init; }
}

public sealed record BluetoothDeviceModel
{
    public required string Id { get; init; } // identidade lógica, não AEP ID
    public IReadOnlyList<BluetoothEndpointReference> Endpoints { get; init; } = [];
}
```

O contrato real preserva os demais campos existentes. `BluetoothDeviceChange.DeviceId` passa a ser sempre o `BluetoothDeviceModel.Id` lógico; `EndpointId` pode ser adicionado apenas como metadado da observação. A UI, o inspetor, a bateria e o reconector usam o Id lógico como chave. IDs de `AudioPlaybackConnection`, `BluetoothLEDevice` e `PhoneLineTransportDevice` só circulam nas referências e nas chamadas dos respectivos adaptadores.

Uma atualização de estado de um endpoint dentro do mesmo grupo produz um único evento `Updated` para a identidade lógica. A remoção do último endpoint produz um único `Removed`. Trocar o primeiro endpoint observado nunca troca a identidade lógica nem cria uma segunda linha.

## Fluxos e limites

### Bateria e GATT

`GattBatteryProvider` escolhe a referência BLE explícita. Para `DualMode`, ele nunca chama `BluetoothLEDevice.FromIdAsync` com o ID Classic. Sem referência BLE, retorna `Unavailable` com fonte e diagnóstico, sem tentar reconstruir um ID.

### Áudio A2DP

A descoberta usa somente o ID exato retornado por `AudioPlaybackConnection.GetDeviceSelector()` e `DeviceInformation`. A correlação com o dispositivo lógico segue `ContainerId`, endereço normalizado e, por último, nome único. Nome repetido é `Ambiguous` e bloqueia a conexão.

`A2dpSinkService` serializa `ConnectAsync`/`DisconnectAsync` em uma gate única. Cada conexão recebe uma geração. O serviço só publica estado do candidato que ainda é a conexão corrente; eventos tardios de uma conexão substituída são ignorados. Um fechamento ocorrido durante `OpenAsync` não pode ser convertido em `Opened` posteriormente. Candidatos não promovidos são sempre descartados.

O reconector mantém o vínculo entre `LogicalId` e o selector ID exato. Quando o estado volta a `Opened`, a UI reconstitui o toggle pelo vínculo persistido em memória, não por uma flag definida apenas pelo clique manual. Desabilitar remove o vínculo, cancela o loop e fecha a conexão corrente. A política continua limitada a `1s, 2s, 5s, 10s, 30s, 60s`.

### Endpoint padrão

O inventário de render usa `Role.Multimedia` para marcar o endpoint padrão de mídia. O endpoint de comunicações continua podendo ser observado separadamente, mas não é apresentado como destino padrão do A2DP.

### HFP e chamadas

A ação HFP fica disponível somente para um `BluetoothDeviceModel` cuja categoria seja `Smartphone`. A descoberta pode continuar reportando `PhoneLineTransportDevice`, mas uma correspondência por nome repetido é `Ambiguous`.

A ação chama a API pública real (`RequestAccessAsync`, registro quando permitido e conexão). O resultado de transporte é exibido com seus status e HRESULT. Nenhum texto ou estado habilita PCM, microfone, roteamento de chamada ou afirma chamada bidirecional sem teste físico de downlink/uplink aprovado. `ICallSession`, `IAudioRouter` e `IAudioProcessingPipeline` continuam como limites de contrato até existir capacidade aprovada e evidência de chamada real.

### Diagnósticos e privacidade

`DiagnosticsExporter` recebe um diretório opcional injetável para teste e integração. O padrão é uma pasta gravável sob `Environment.SpecialFolder.LocalApplicationData/BTHaven/Diagnostics`; não há dependência de uma unidade `D:`. O nome contém timestamp UTC com milissegundos e um sufixo GUID, e a criação usa modo não-colidente.

A exportação redigida mantém tipo de exceção, HRESULT, níveis e contagens úteis. Campos de `message` e `stackTrace` de exceções são omitidos ou substituídos por `[REDACTED]`; identificadores, nomes, endereços, caminhos e chaves sensíveis permanecem redigidos. A visualização local de logs pode continuar detalhada, mas nunca é usada como conteúdo do ZIP sem essa política.

## Não objetivos

- Não adicionar `phoneLineTransportManagement` ao pacote aberto sem aprovação de capacidade e experimento de assinatura.
- Não criar driver virtual, hook HCI, ADB, Phone Link reverse engineering ou controle AVRCP privado.
- Não implementar PCM de chamadas, AEC, AGC ou supressão de ruído como parte deste hardening.
- Não manter dois algoritmos independentes de correlação; a política deve viver no Core/adapter apropriado e ser reutilizada.

## Compatibilidade e migração

A mudança de semântica de `BluetoothDeviceModel.Id` é interna ao repositório, mas quebra consumidores que persistem o antigo AEP ID. A migração deve atualizar todos os callers, probes e testes na mesma sequência; referências de endpoint são a rota de acesso ao WinRT. Não há alias para o significado antigo. Se um consumidor externo for confirmado, a decisão deve ser versionada antes do primeiro commit de código.

O contrato `BluetoothDeviceChange` deve permanecer extensível: o significado lógico de `DeviceId` é corrigido e qualquer ID bruto necessário para diagnóstico é um campo opcional separado. A ordem dos eventos é observável; testes devem exigir uma transição por identidade, não uma transição por endpoint.

## Critérios de aceitação do design

- Um telefone DualMode observado em qualquer ordem produz uma identidade lógica e uma linha; as referências Classic e BLE permanecem acessíveis.
- GATT usa a referência BLE explícita mesmo quando o endpoint Classic foi observado primeiro.
- Remover um endpoint preserva a linha e a seleção; remover o último produz uma remoção sem duplicata.
- Duas ativações A2DP concorrentes deixam apenas a conexão vencedora, descartam a perdedora e não deixam evento velho alterar o estado novo.
- Perda seguida de reconexão mantém o toggle ligado ao dispositivo lógico; desligar cancela reconexão e fecha a conexão.
- Exportação funciona em uma pasta temporária e no padrão LocalApplicationData, inclusive em duas chamadas concorrentes, sem depender de `D:`.
- O ZIP redigido não contém IDs, nomes, endereços ou caminhos embutidos em `message`/`stackTrace`; preserva `exceptionType` e HRESULT.
- Render padrão é derivado de `Role.Multimedia`; HFP só é acionável para Smartphone; correspondência de nome não única resulta em `Ambiguous`.
- HFP e call PCM continuam explicitamente bloqueados além da fronteira pública documentada.
