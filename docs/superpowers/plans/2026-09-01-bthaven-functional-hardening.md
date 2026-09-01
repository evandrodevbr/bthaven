# BTHaven Functional Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar a identidade Bluetooth estável, manter estados A2DP/UI coerentes, tornar exportação portátil e preservar o limite honesto de HFP/call PCM.

**Architecture:** O Core será a fonte única da identidade lógica e das referências de endpoints. Os adapters Windows continuarão donos dos IDs WinRT e das operações de transporte; a UI consumirá a identidade lógica e um vínculo explícito com o alvo A2DP. Cada incremento fecha um contrato observável antes de avançar para o próximo.

**Tech Stack:** C# / .NET 10 / WinUI 3 / Windows App SDK 2.4 / WinRT Bluetooth / NAudio 2.2.1 / xUnit 2.9.3 / Windows 11 x64.

**Spec:** `docs/superpowers/specs/2026-09-01-bthaven-functional-hardening-design.md`

## Global Constraints

- `BluetoothDeviceModel.Id` é a identidade lógica estável; IDs AEP/WinRT ficam em referências explícitas Classic/BLE.
- A identidade segue `ContainerId > endereço normalizado > endpoint isolado`; endpoints sem identidade confiável não são mesclados por nome.
- Eventos `BluetoothDeviceChange.DeviceId` usam a identidade lógica; remoção parcial mantém uma linha e remoção do último endpoint gera `Removed`.
- GATT usa a referência BLE explícita; A2DP usa o selector ID exato retornado por `AudioPlaybackConnection.GetDeviceSelector()`.
- A2DP usa serialização e generation guard; reconnect segue `1s, 2s, 5s, 10s, 30s, 60s` e mantém o vínculo com o dispositivo lógico.
- Exportação usa `Environment.SpecialFolder.LocalApplicationData/BTHaven/Diagnostics` por padrão, diretório injetável em testes e nome não-colidente.
- Logs redigidos omitem ou substituem `message` e `stackTrace`; `exceptionType` e HRESULT permanecem.
- Render usa `Role.Multimedia`; HFP só é acionável para `Smartphone`; fallback por nome não único é `Ambiguous`.
- HFP/call PCM não será habilitado sem capacidade aprovada e teste físico bidirecional de downlink/uplink.
- Não adicionar dependências, capabilities restritas, drivers, hooks HCI, ADB, Phone Link reverse engineering ou controle AVRCP privado.
- Cada tarefa usa RED → execução com falha esperada → GREEN → execução aprovada → refactor → execução aprovada → commit.
- Nenhuma etapa deste plano altera a evidência da máquina sem registrar o comando e a saída; probes de ativação HFP e execução MSIX exigem decisão operacional separada.

---

## Incremento 1: política de testes não-vacuosos e identidade/filtros do Core

**Arquivos:**
- Modify: `src/BTHaven.Core/Devices/BluetoothDeviceModel.cs`
- Modify: `src/BTHaven.Core/Devices/BluetoothDeviceProjection.cs`
- Modify: `src/BTHaven.Core/Contracts/ServiceContracts.cs`
- Create: `tests/BTHaven.Core.Tests/BluetoothDeviceIdentityTests.cs`
- Modify: `tests/BTHaven.Core.Tests/BluetoothDeviceProjectionTests.cs`
- Modify: `tests/BTHaven.Core.Tests/BluetoothDeviceModelTests.cs`

**Interfaces:**
- Produces `BluetoothEndpointReference` com `Id`, `Transport`, `ContainerId` e `Address`.
- Produces `BluetoothDeviceModel.Id` lógico e `BluetoothDeviceModel.Endpoints`.
- `BluetoothDeviceChange.DeviceId` passa a documentar e transportar o Id lógico; o ID bruto do evento, quando necessário, fica em campo opcional `EndpointId`.
- A função determinística `BluetoothDeviceIdentity.GetLogicalId(BluetoothDeviceObservation)` aplica ContainerId, endereço normalizado e endpoint isolado nesta ordem.

- [ ] **Step 1: Write the failing test**

Adicionar testes comportamentais:

```csharp
[Fact]
public void Same_container_produces_one_logical_id_and_keeps_both_endpoint_ids()
{
    var classic = Observation("classic", BluetoothTransport.Classic, "container-1", "80:54:2D:51:3B:D6");
    var ble = Observation("ble", BluetoothTransport.LowEnergy, "container-1", "80-54-2d-51-3b-d6");

    var classicModel = BluetoothDeviceProjection.ToModel(classic);
    var bleModel = BluetoothDeviceProjection.ToModel(ble);

    Assert.Equal(classicModel.Id, bleModel.Id);
    Assert.NotEqual(classicModel.Id, "classic");
    Assert.Equal("80:54:2D:51:3B:D6", bleModel.Endpoints.Single().Address);
}

[Fact]
public void Different_container_and_address_do_not_merge_by_equal_name()
{
    var first = Observation("endpoint-a", BluetoothTransport.Classic, "container-a", "001122334455") with { Name = "Phone" };
    var second = Observation("endpoint-b", BluetoothTransport.LowEnergy, "container-b", "AABBCCDDEEFF") with { Name = "Phone" };

    Assert.NotEqual(BluetoothDeviceIdentity.GetLogicalId(first), BluetoothDeviceIdentity.GetLogicalId(second));
}

[Fact]
public void Missing_identity_uses_transport_namespaced_endpoint_id()
{
    var observation = Observation("endpoint", BluetoothTransport.LowEnergy, null, null);

    Assert.Equal("endpoint:LowEnergy:endpoint", BluetoothDeviceIdentity.GetLogicalId(observation));
}
```

Os helpers de fixture devem construir somente `BluetoothDeviceObservation` e não depender de WinRT.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~BluetoothDeviceIdentityTests
```

Expected: FAIL de compilação porque `BluetoothEndpointReference`, `BluetoothDeviceIdentity.GetLogicalId` e a identidade lógica ainda não existem.

- [ ] **Step 3: Write minimal implementation**

Adicionar o record de referência e a política única no Core. O `ToModel` deve preencher uma referência para a observação de entrada e usar o Id retornado pela política. A normalização remove `:`, `-`, espaço e demais separadores não alfanuméricos, converte para maiúsculas e rejeita string vazia. A chave de endpoint isolado deve conter transporte e ID para não colidir entre Classic e BLE. Manter filtros existentes e fazer `Audio` continuar baseado em capacidades, não em nomes.

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~BluetoothDeviceIdentityTests
```

Expected: todos os casos novos passam.

- [ ] **Step 5: Migrate existing Core assertions and non-vacuous guards**

Atualizar `BluetoothDeviceProjectionTests` para afirmar Id lógico e referência de endpoint, mantendo as asserções de paired/connected/present. Em testes de hardware existentes, substituir retornos silenciosos por `Assert.Skip` com motivo explícito quando o teste é realmente hardware-gated; testes de política pura não podem sair cedo.

- [ ] **Step 6: Run Core suite and refactor**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release --no-restore
```

Expected: todos os testes Core passam. Remover duplicação de normalização e verificar que não há uma segunda regra de identidade em `BluetoothDeviceProjection`.

- [ ] **Step 7: Commit**

```powershell
git add src/BTHaven.Core/Devices/BluetoothDeviceModel.cs src/BTHaven.Core/Devices/BluetoothDeviceProjection.cs src/BTHaven.Core/Contracts/ServiceContracts.cs tests/BTHaven.Core.Tests/BluetoothDeviceIdentityTests.cs tests/BTHaven.Core.Tests/BluetoothDeviceProjectionTests.cs tests/BTHaven.Core.Tests/BluetoothDeviceModelTests.cs
git commit -m "test: define stable Bluetooth device identity"
```

## Incremento 2: logger/exporter portátil e redigido

**Arquivos:**
- Modify: `src/BTHaven.Windows/Diagnostics/TraceDiagnosticLogger.cs`
- Modify: `src/BTHaven.Windows/Diagnostics/DiagnosticsExporter.cs`
- Modify: `tests/BTHaven.IntegrationTests/TraceDiagnosticLoggerTests.cs`
- Modify: `tests/BTHaven.IntegrationTests/DiagnosticsExporterTests.cs`

**Interfaces:**
- `DiagnosticsExporter(..., string? exportDirectory = null)` usa o diretório recebido; quando nulo, deriva `LocalApplicationData/BTHaven/Diagnostics`.
- O helper de arquivo cria o ZIP com `FileMode.CreateNew`; timestamp UTC com milissegundos e GUID tornam nomes distintos sob concorrência.
- `TraceDiagnosticLogger.ReadRecent(..., redactSensitive: true)` preserva evento, nível, HRESULT e tipo; `message`/`stackTrace` são `[REDACTED]` ou omitidos.

- [ ] **Step 1: Write the failing test**

Adicionar:

```csharp
[Fact]
public async Task Export_uses_injected_directory_and_unique_names_for_concurrent_calls()
{
    var directory = Path.Combine(Path.GetTempPath(), "bthaven-export-tests", Guid.NewGuid().ToString("N"));
    var exporter = CreateExporter(directory);
    var paths = await Task.WhenAll(exporter.ExportAsync(), exporter.ExportAsync());

    Assert.Equal(2, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    Assert.All(paths, path => Assert.StartsWith(directory, path, StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void Redacted_exception_logs_do_not_export_exception_text_or_stack()
{
    var logger = CreateLogger();
    logger.Error("Test.Error", new InvalidOperationException("deviceId=phone-secret name=Private Phone"));

    var redacted = logger.ReadRecent(100, redactSensitive: true);

    Assert.DoesNotContain(redacted, line => line.Contains("Private Phone", StringComparison.Ordinal));
    Assert.DoesNotContain(redacted, line => line.Contains("phone-secret", StringComparison.Ordinal));
}
```

O fixture `CreateExporter` recebe somente doubles já usados no projeto e diretório temporário; não usa o literal `D:\Documents`.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~DiagnosticsExporterTests
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~TraceDiagnosticLoggerTests
```

Expected: o teste de diretório não compila sem o parâmetro injetável e o teste de redaction falha porque `message`/`stackTrace` ainda atravessam a redação.

- [ ] **Step 3: Write minimal implementation**

Trocar o literal de diretório pelo parâmetro opcional e o caminho `LocalApplicationData`. Criar o ZIP por `FileStream(FileMode.CreateNew)` dentro de uma tentativa de nome único, depois anexar `ZipArchive`. Na transformação redigida, substituir os valores de `message` e `stackTrace` de exceção antes de serializar; não remover tipo nem HRESULT. Manter os campos de dispositivo, nome, endereço, path e chaves secretas já redigidos.

- [ ] **Step 4: Run test to verify it passes**

Repetir os dois comandos do Step 2. Expected: ambos PASS; nenhum arquivo fica no diretório temporário após o cleanup do teste.

- [ ] **Step 5: Refactor and verify privacy contract**

Consolidar a função de sanitização de logs para não haver divergência entre `TraceDiagnosticLogger` e `DiagnosticsExporter`. Executar:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~DiagnosticsExporterTests
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~TraceDiagnosticLoggerTests
```

Expected: PASS e ZIP sem identificador embutido em texto de exceção.

- [ ] **Step 6: Commit**

```powershell
git add src/BTHaven.Windows/Diagnostics/TraceDiagnosticLogger.cs src/BTHaven.Windows/Diagnostics/DiagnosticsExporter.cs tests/BTHaven.IntegrationTests/TraceDiagnosticLoggerTests.cs tests/BTHaven.IntegrationTests/DiagnosticsExporterTests.cs
git commit -m "fix: harden diagnostic export privacy"
```

## Incremento 3: A2DP/reconnect serializado e associado à UI

**Arquivos:**
- Modify: `src/BTHaven.Windows/Audio/A2dpSinkService.cs`
- Modify: `src/BTHaven.Windows/Audio/A2dpAutoReconnectService.cs`
- Modify: `tests/BTHaven.IntegrationTests/WindowsAudioServicesSmokeTests.cs`
- Create: `tests/BTHaven.IntegrationTests/A2dpSinkServiceTests.cs`

**Interfaces:**
- Manter `IMediaAudioSink.ConnectAsync(string selectorDeviceId, ...)` e `DisconnectAsync(...)` sem expor WinRT.
- Introduzir somente uma seam interna de teste para criação da conexão (`IA2dpConnectionFactory` e wrapper de conexão), com implementação de produção que chama `AudioPlaybackConnection.TryCreateFromId`.
- O serviço mantém `selectorDeviceId`, generation e conexão corrente; o reconector mantém `selectorDeviceId` associado ao `LogicalId` recebido pela UI.

- [ ] **Step 1: Write the failing test**

Adicionar testes com factory fake:

```csharp
[Fact]
public async Task Concurrent_connects_dispose_the_loser_and_ignore_stale_state()
{
    var first = FakeConnection.BlockOpen();
    var second = FakeConnection.OpenSuccessfully();
    var service = CreateService(first, second);

    var firstTask = service.ConnectAsync("selector-a");
    var secondTask = service.ConnectAsync("selector-b");
    var results = await Task.WhenAll(firstTask, secondTask);

    Assert.Contains(true, results);
    Assert.Equal("selector-b", service.DeviceId);
    Assert.Equal(MediaAudioSinkState.Opened, service.State);
    Assert.True(first.DisposeCalled);
}

[Fact]
public async Task Closed_connection_reconnects_without_losing_the_logical_binding()
{
    var connection = FakeConnection.OpenSuccessfully();
    var sink = CreateService(connection);
    await sink.ConnectAsync("selector-a");
    connection.RaiseClosed();

    Assert.Equal(MediaAudioSinkState.Disabled, sink.State);
}
```

O teste de reconnect deve executar o schedule com atrasos injetáveis nulos apenas no fake; a política pública continua documentada com os seis atrasos reais.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~A2dpSinkServiceTests
```

Expected: falha de compilação porque não existe factory fake; a execução atual não oferece gate nem generation guard.

- [ ] **Step 3: Write minimal implementation**

Adicionar `SemaphoreSlim` para a operação inteira de troca de conexão. Incrementar generation ao iniciar cada operação, validar generation e sender antes de aplicar `StateChanged`, descartar candidatos não promovidos e verificar o estado real após `OpenAsync` antes de publicar `Opened`. Fazer `A2dpAutoReconnectService` chamar a mesma superfície serializada e carregar o vínculo lógico. O evento de estado deve permitir que a UI associe o selector ID à identidade lógica já conhecida.

- [ ] **Step 4: Run test to verify it passes**

Repetir o comando do Step 2. Expected: PASS, com uma única conexão corrente e sem evento tardio alterando o estado vencedor.

- [ ] **Step 5: Refactor and run real smoke**

Manter o smoke real existente e executar:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~WindowsAudioServicesSmokeTests
```

Expected: os testes de inventário passam; o teste de abertura continua hardware-gated e não pode converter ausência de alvo em PASS silencioso.

- [ ] **Step 6: Commit**

```powershell
git add src/BTHaven.Windows/Audio/A2dpSinkService.cs src/BTHaven.Windows/Audio/A2dpAutoReconnectService.cs tests/BTHaven.IntegrationTests/WindowsAudioServicesSmokeTests.cs tests/BTHaven.IntegrationTests/A2dpSinkServiceTests.cs
git commit -m "fix: serialize A2DP connection lifecycle"
```

## Incremento 4: seleção explícita do endpoint BLE e bateria

**Arquivos:**
- Modify: `src/BTHaven.Windows/Battery/GattBatteryProvider.cs`
- Modify: `src/BTHaven.Windows/Battery/WindowsDevicePropertiesBatteryProvider.cs`
- Modify: `src/BTHaven.Windows/Battery/WindowsBatteryService.cs`
- Modify: `tests/BTHaven.Core.Tests/BatteryServiceTests.cs`
- Modify: `tests/BTHaven.IntegrationTests/WindowsAudioServicesSmokeTests.cs` somente se o fixture de integração compartilhar o modelo
- Create: `tests/BTHaven.IntegrationTests/GattBatteryProviderTests.cs`

**Interfaces:**
- O resolvedor de endpoint BLE recebe `BluetoothDeviceModel` e retorna o único `BluetoothEndpointReference` LE; ausência retorna `null` e diagnóstico `Unavailable`.
- Consultas de propriedades Windows iteram referências de endpoint reais, nunca `BluetoothDeviceModel.Id` lógico como AEP ID.
- `IBatteryProvider` e `IBatteryService` permanecem com as assinaturas existentes.

- [ ] **Step 1: Write the failing test**

Adicionar um modelo DualMode cujo `Id` lógico seja `container:...`, cujo primeiro endpoint seja Classic e cujo segundo endpoint seja BLE. A asserção deve verificar que a seleção do provider retorna exatamente o `BluetoothLE#...` e que um modelo sem endpoint BLE retorna `Unavailable` sem reconstruir ID.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~GattBatteryProviderTests
```

Expected: FAIL porque o provider atual usa `device.Id` diretamente e não possui seleção de endpoint.

- [ ] **Step 3: Write minimal implementation**

Usar `device.Endpoints.FirstOrDefault(endpoint => endpoint.Transport is LowEnergy or DualMode)` para GATT; passar o ID selecionado a `BluetoothLEDevice.FromIdAsync`. Para propriedades Windows, consultar os IDs de endpoint em ordem determinística e parar no primeiro resultado com dado utilizável. Manter `Unavailable` quando nenhum valor confiável existe. Em `SubscribeAsync`, remover o handler e descartar objetos também em cancelamento/exceção antes da criação da assinatura persistente.

- [ ] **Step 4: Run test to verify it passes**

Repetir o comando do Step 2. Expected: PASS, incluindo o caso Classic-first/BLE-second.

- [ ] **Step 5: Run existing battery tests and refactor**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.Core.Tests/BTHaven.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~BatteryServiceTests
```

Expected: PASS; a precedência de provider continua a primeira fonte com dado real. Refatorar para uma única função de seleção de endpoint.

- [ ] **Step 6: Commit**

```powershell
git add src/BTHaven.Windows/Battery/GattBatteryProvider.cs src/BTHaven.Windows/Battery/WindowsDevicePropertiesBatteryProvider.cs src/BTHaven.Windows/Battery/WindowsBatteryService.cs tests/BTHaven.Core.Tests/BatteryServiceTests.cs tests/BTHaven.IntegrationTests/GattBatteryProviderTests.cs
git commit -m "fix: resolve battery providers by endpoint"
```

## Incremento 5: manager, inspector, HFP e endpoint de áudio

**Arquivos:**
- Modify: `src/BTHaven.Windows/Bluetooth/BluetoothDeviceManager.cs`
- Modify: `src/BTHaven.Windows/Bluetooth/WindowsBluetoothDeviceObservationFactory.cs`
- Modify: `src/BTHaven.Windows/Bluetooth/BluetoothDeviceInspector.cs`
- Modify: `src/BTHaven.Windows/Bluetooth/BluetoothDeviceInspector.Gatt.cs`
- Modify: `src/BTHaven.Windows/Telephony/HfpPhoneTransportService.cs`
- Modify: `src/BTHaven.Windows/Audio/AudioEndpointManager.cs`
- Modify: `tests/BTHaven.IntegrationTests/BluetoothDeviceManagerSmokeTests.cs`
- Modify: `tests/BTHaven.IntegrationTests/BluetoothDeviceInspectorTests.cs`
- Modify: `tests/BTHaven.IntegrationTests/HfpPhoneTransportServiceTests.cs`
- Modify: `tests/BTHaven.IntegrationTests/WindowsAudioServicesSmokeTests.cs`

**Interfaces:**
- `BluetoothDeviceManager` agrupa por `BluetoothDeviceIdentity.GetLogicalId`, preserva referências de todos os endpoints e publica mudanças por Id lógico.
- `BluetoothDeviceInspector` retorna `Ambiguous` quando o fallback por nome encontra mais de um endpoint; não inspeciona arbitrariamente um candidato.
- `HfpPhoneTransportService` mantém descoberta/ativação real e não promete disconnect que a API não expõe.
- `AudioEndpointManager` usa `Role.Multimedia` ao calcular `AudioEndpointModel.IsDefault` para render.

- [ ] **Step 1: Write the failing test**

Adicionar/ajustar cenários:

```csharp
[Fact]
public void Removing_one_endpoint_emits_one_update_for_the_logical_device()
{
    // Alimentar o manager com Classic e BLE do mesmo ContainerId; remover Classic.
    // Assert: uma mudança Updated, DeviceId igual ao Id lógico e nenhuma chave de endpoint antiga.
}

[Fact]
public void Equal_name_without_container_or_address_is_ambiguous()
{
    // Dois DeviceInformation com o mesmo nome e identidades ausentes.
    // Assert: diagnóstico/status Ambiguous e nenhum endpoint arbitrário inspecionado.
}
```

O teste de Role deve afirmar que o default de render é o endpoint retornado para `Role.Multimedia`, independentemente de `Role.Communications`.

- [ ] **Step 2: Run test to verify it fails**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~BluetoothDeviceManagerSmokeTests
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~WindowsAudioServicesSmokeTests
```

Expected: os cenários de remoção/ambiguidade falham contra `first.Id`, `PublishChange` por endpoint e `Role.Communications`.

- [ ] **Step 3: Write minimal implementation**

Alterar `Merge` para produzir Id lógico, EndpointReferences e estados agregados. Fazer `HandleRemoved`/`PublishChange` usar o mesmo Id lógico antes/depois. Centralizar `MatchesModel`/`MatchesDevice` para tentar ContainerId e endereço primeiro; nome só gera match único. Registrar diagnóstico `Ambiguous` com contagem. Alterar o render default para `Role.Multimedia`. Não adicionar capability restrita nem alterar o resultado observado de HFP.

- [ ] **Step 4: Run test to verify it passes**

Repetir os comandos do Step 2 e executar:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~BluetoothDeviceInspectorTests
& "$env:USERPROFILE\.dotnet\dotnet.exe" test tests/BTHaven.IntegrationTests/BTHaven.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~HfpPhoneTransportServiceTests
```

Expected: PASS nos casos determinísticos; testes sem hardware devem ser explicitamente marcados como skipped, nunca retornar PASS vazio.

- [ ] **Step 5: Refactor and verify contracts**

Executar build Windows e revisar que nenhum `FromIdAsync` recebe `BluetoothDeviceModel.Id` sem selecionar uma referência:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build src/BTHaven.Windows/BTHaven.Windows.csproj -c Release -p:Platform=x64 --no-restore
```

Expected: exit code 0, zero warnings e zero erros.

- [ ] **Step 6: Commit**

```powershell
git add src/BTHaven.Windows/Bluetooth/BluetoothDeviceManager.cs src/BTHaven.Windows/Bluetooth/WindowsBluetoothDeviceObservationFactory.cs src/BTHaven.Windows/Bluetooth/BluetoothDeviceInspector.cs src/BTHaven.Windows/Bluetooth/BluetoothDeviceInspector.Gatt.cs src/BTHaven.Windows/Telephony/HfpPhoneTransportService.cs src/BTHaven.Windows/Audio/AudioEndpointManager.cs tests/BTHaven.IntegrationTests/BluetoothDeviceManagerSmokeTests.cs tests/BTHaven.IntegrationTests/BluetoothDeviceInspectorTests.cs tests/BTHaven.IntegrationTests/HfpPhoneTransportServiceTests.cs tests/BTHaven.IntegrationTests/WindowsAudioServicesSmokeTests.cs
git commit -m "fix: stabilize Bluetooth aggregation and matching"
```

## Incremento 6: presenter/handlers da UI e associação do toggle

**Arquivos:**
- Modify: `src/BTHaven.App/DeviceRowViewModel.cs`
- Modify: `src/BTHaven.App/MainPage.xaml.cs`
- Modify: `src/BTHaven.App/MainPage.Actions.cs`
- Modify: `src/BTHaven.App/MainPage.xaml`

**Interfaces:**
- `DeviceRowViewModel.Id` recebe o Id lógico; o Tag do toggle não contém AEP ID bruto.
- `MainPage` mantém `Dictionary<string, BluetoothDeviceModel>` por Id lógico e `Dictionary<string, string>` de selector A2DP para Id lógico.
- `A2dpService_StateChanged` atualiza o vínculo do toggle para `Opened`, limpa apenas o vínculo correspondente em `Disabled/Failed` e ignora estado de sender antigo.
- HFP fica habilitado somente para `BluetoothDeviceCategory.Smartphone`; o texto continua distinguindo descoberta, acesso negado, registro e conexão.

- [ ] **Step 1: Write the failing test/verification scenario**

Como não existe harness visual no projeto, registrar o cenário de presenter em teste manual automatizável por logs: selecionar DualMode, ativar A2DP, provocar `Closed`, aguardar reconnect e observar que `RefreshRows` mantém o mesmo Id lógico com `MediaEnabled=true`; trocar a seleção para mouse deve deixar HFP desabilitado.

O cenário de remoção deve aplicar `BluetoothDeviceChange` Updated/Removed na fila e afirmar via estado observado que não há duas linhas com o mesmo ContainerId.

- [ ] **Step 2: Run current verification to establish failure**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build src/BTHaven.App/BTHaven.App.csproj -c Release -p:Platform=x64 --no-restore
```

Expected: o build atual passa, mas o cenário manual reproduz o toggle desligado após reconnect e HFP habilitado para categorias não-phone; registrar a saída visual/log sem alterar código nesta etapa do executor.

- [ ] **Step 3: Write minimal implementation**

Migrar todas as chaves da página para Id lógico. Ao descobrir um alvo A2DP, guardar selector→logical Id. Ao receber `Opened`, localizar esse vínculo e reconstruir `activeMediaDeviceId`; ao receber `Disabled/Failed`, limpar somente o alvo corrente. No handler HFP, bloquear antes da descoberta quando a categoria não for Smartphone. Remover estados que dependem de um `device.Id` bruto.

- [ ] **Step 4: Run build and UI verification**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build src/BTHaven.App/BTHaven.App.csproj -c Release -p:Platform=x64 --no-restore
```

Expected: exit code 0, zero warnings e zero erros. No MSIX, o toggle permanece ligado depois de reconnect, a remoção parcial não duplica linha e HFP aparece indisponível para não-smartphone.

- [ ] **Step 5: Refactor and check XAML bindings**

Confirmar que `ToggleSwitch.Tag` é o Id lógico, que `IsOn` continua controlado pelo presenter e que `MediaToggleSwitch_Toggled` não usa seleção global para decidir qual dispositivo desligar. Manter `EmptyState`, layout e mensagens de limite HFP sem sobreposição.

- [ ] **Step 6: Commit**

```powershell
git add src/BTHaven.App/DeviceRowViewModel.cs src/BTHaven.App/MainPage.xaml.cs src/BTHaven.App/MainPage.Actions.cs src/BTHaven.App/MainPage.xaml
git commit -m "fix: keep UI state tied to logical devices"
```

## Incremento 7: hardware/MSIX e suíte final

**Arquivos:**
- Modify: `docs/probe-results.md` apenas com saída sanitizada da execução realizada
- Modify: `README.md` apenas se os comandos ou limites efetivamente mudarem
- No production/test files are changed by this verification increment unless a prior task has an observed contract mismatch that is documented before proceeding.

**Interfaces:**
- A verificação física confirma, sem inventar resultado, os contratos da spec: identidade DualMode, A2DP selector exato, reconnect, endpoint Multimedia e HFP bloqueado.
- HFP activation com `--request-access --register --connect` permanece uma operação explícita e separada; não é executada como smoke automático.

- [ ] **Step 1: Build the complete solution**

```powershell
Set-Location 'C:\Users\evand\Documents\GitHub\bthaven'
$env:Path = "$env:USERPROFILE\.dotnet;$env:Path"
& "$env:USERPROFILE\.dotnet\dotnet.exe" build BTHaven.slnx -c Release -p:Platform=x64 --no-restore
```

Expected: exit code 0, zero warnings e zero erros.

- [ ] **Step 2: Run the existing suite serially and in the default mode**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test BTHaven.slnx -c Release -p:Platform=x64 --no-restore --no-build -m:1
& "$env:USERPROFILE\.dotnet\dotnet.exe" test BTHaven.slnx -c Release -p:Platform=x64 --no-restore --no-build
```

Expected: Core e Integration aprovados; se o modo paralelo reproduzir o bloqueio WinRT documentado, registrar o erro e usar somente `-m:1` para a evidência final.

- [ ] **Step 3: Run read-only probes**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project probes/01-device-enumeration/01-device-enumeration.csproj -c Release -- --watch-seconds 2
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project probes/02-battery/02-battery.csproj -c Release
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project probes/03-a2dp-sink/03-a2dp-sink.csproj -c Release
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project probes/05-call-audio-routing/05-call-audio-routing.csproj -c Release
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project probes/06-device-inspection/06-device-inspection.csproj -c Release
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project probes/04-phone-hfp/04-phone-hfp.csproj -c Release
```

Expected: registrar contagens, status GATT/bateria, A2DP discovery/abertura se um selector exato for fornecido, `Role.Multimedia`, e HFP discovery sem declarar call PCM. Remover IDs, nomes, endereços e endpoints dos artefatos públicos.

- [ ] **Step 4: Run packaged MSIX smoke only with the approved environment**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" msbuild src/BTHaven.App/BTHaven.App.csproj -t:Run -p:Platform=x64 -p:Configuration=Release -p:WinAppRunDetach=true
```

Expected: a aplicação abrir com identidade de pacote; se o Windows App Runtime não estiver registrado, registrar `REGDB_E_CLASSNOTREG` e não reclassificar como falha de código. Fechar o processo antes do próximo build.

- [ ] **Step 5: Exercise physical acceptance path**

Com um telefone emparelhado, verificar manualmente: uma linha DualMode; referências Classic/BLE; ativar áudio; iniciar mídia no telefone apontando para o PC; ouvir no endpoint Multimedia padrão; provocar perda e retorno do alvo; desligar toggle; executar inspeção; confirmar GATT/RFCOMM/bateria explícitos; observar HFP `DeniedBySystem`/HRESULT quando a ação for autorizada pelo operador.

- [ ] **Step 6: Update evidence and perform final self-review**

Atualizar somente a evidência sanitizada em `docs/probe-results.md`. Conferir cobertura de cada requisito da spec: identidade, eventos, GATT, A2DP/generation, reconnect/UI, exporter, redaction, Multimedia, HFP/Smartphone, `Ambiguous` e limites de PCM. Não registrar sucesso de áudio audível ou HFP sem observação física correspondente.

- [ ] **Step 7: Commit final de código e evidência**

```powershell
git add docs/probe-results.md README.md
git commit -m "test: verify functional hardening on Windows"
```

## Critérios de aceitação consolidados

- O mesmo telefone DualMode observado nas ordens Classic→BLE e BLE→Classic produz um único Id lógico, uma linha e referências exatas para ambos os endpoints.
- Remoção parcial não duplica nem perde seleção; remoção do último endpoint produz um único evento `Removed`.
- GATT e propriedades Windows não tentam usar o Id lógico como AEP ID; GATT usa BLE.
- A2DP exibe `OpenAsync=Success` somente para selector ID exato, mantém uma única conexão sob concorrência, ignora eventos de gerações antigas e reconecta com a mesma associação lógica.
- O toggle permanece ligado após reconnect e desligá-lo cancela reconnect e fecha a conexão.
- Exportação não depende de `D:`, suporta diretório injetável e nomes concorrentes distintos.
- ZIP redigido não contém identificadores, nomes, endereços, paths, `message` ou `stackTrace`; tipo e HRESULT permanecem.
- Render padrão é Multimedia; HFP só é acionável para Smartphone; nome não único resulta em `Ambiguous`.
- HFP/call PCM continua bloqueado além da capacidade pública aprovada.
- A suíte não passa por retorno silencioso quando hardware falta; testes de hardware ficam explicitamente classificados e a suíte Core permanece não-vacuosa.

## Self-review do plano

- **Cobertura da spec:** os sete incrementos cobrem todas as regras de identidade, eventos, GATT, A2DP, reconnect, exporter, redaction, role, HFP, ambiguidade e limites de call PCM.
- **Tipos:** `BluetoothEndpointReference`, `BluetoothDeviceModel.Endpoints`, `BluetoothDeviceChange.DeviceId` lógico, `IA2dpConnectionFactory` e `DiagnosticsExporter.exportDirectory` são definidos antes dos consumidores que os usam.
- **Dependências:** nenhuma dependência nova; os comandos usam o SDK .NET per-user e os projetos/restores existentes.
- **Testes:** cada Required tem caso determinístico ou cenário físico explícito; early returns são convertidos em skips justificados apenas em testes hardware-gated.
- **Compatibilidade:** a mudança de semântica de Id é migrada em manager, inspector, battery, A2DP, UI, probes e testes no mesmo plano; não há alias para o antigo significado de endpoint.
- **Concorrência e cleanup:** há testes para Connect simultâneo, geração tardia, nome de arquivo concorrente, cancelamento GATT e remoção parcial.
- **Limitações honestas:** build verde não fecha a prova de áudio audível, capacidade HFP ou PCM; esses resultados dependem de MSIX, Windows App Runtime e telefone físico.
