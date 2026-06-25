# Changelog — DesmatchMode4

## [3.0.0-alpha.14] — 2026-06-14

### Fixed / Performance
- **Тиннитус:** cleanup только по `_trackedTinnitusSources` (без `FindObjectsOfType` на каждом респавне)
- **Orphan cleanup:** один раз при входе в рейд (`CleanupOrphanTinnitusAudioObjects` в `CheckRaidStart`)
- **Fade:** убран лишний `StopTinnitusEffect` перед `StartTinnitusEffect` (Start уже останавливает предыдущий звук)

## [3.0.0-alpha.13] — 2026-06-14

### Fixed
- **Тиннитус после респавна:** звук оглушения (писк) больше не накапливается при повторных респавнах
- Убран двойной `StartTinnitusEffect` (fade + finalize); orphan `TinnitusAudioSource` уничтожаются при старте/стопе

## [3.0.0-alpha.12] — 2026-06-13

**Первая проверенная рабочая альфа** (invuln-таймер и penalty протестированы в рейде).

### Fixed
- **Invuln таймер:** боевой отсчёт 5 сек (из cfg) начинается в FINALIZE после fade, а не с телепорта (+8 с буфер)
- **Penalty после invuln:** срабатывает ровно через `ClientInvulnSeconds` после уведомления, не через 13 сек
- **Fade-path:** защита во время затемнения через `isRespawnInProgress`, без silent invuln `ClientInvulnSeconds + 8`

## [3.0.0-alpha.11] — 2026-06-12

### Fixed
- **Уведомление invuln:** показывает `ClientInvulnSeconds` из конфига (3 сек по умолчанию), а не внутренний буфер fade (+8 с)
- **Invuln при телепорте:** silent-режим (`notifyPlayer: false`) — игрок видит одно сообщение в FINALIZE
- **Эмодзи в UI:** убраны из player-facing уведомлений; расширен `SanitizeNotificationMessage` (U+1F300–1FAFF)

### Changed
- **Invulnerability Time по умолчанию:** 5 → **3** сек (клиент + сервер)
- **Fade пробуждения:** +30% скорость рассеивания чёрного экрана (`RESPAWN_WAKE_FADE_SPEED_MULTIPLIER = 1.3`)

## [3.0.0-alpha.10] — 2026-06-13

### Fixed
- **INVULNERABILITY_END:** восстановлен терапевтический урон + лечение (сброс залипших переломов/хромоты), без UI-уведомлений «получил урон»
- **Респавн fade:** убран дублирующийся блок ЭТАП 4.1 (двойной ForceRemove + TryHealWithNetworkSync)
- **ЭТАП 4.1:** только ForceRemove для Fika sync — без повторного RestoreFullHealth после RestorePlayerHealth4Stages

## [3.0.0-alpha.9] — 2026-06-13

### Fixed
- **Уведомление о старте рейда:** убрано из середины загрузки (`CheckRaidStart`); одно сообщение только после `GameWorld.OnGameStarted`: «DesmatchMode: рейд начался. Респавн — F10»
- **Ложный урон после invuln:** убран видимый «терапевтический» урон+лечение при окончании неуязvимости; вместо этого тихая `TryHealWithNetworkSync` для синхронизации с хостом

## [3.0.0-alpha.8] — 2026-06-13

### Fixed (неуязвимость после респавна)
- **Invuln с телепорта:** `EnablePlayerInvulnerability(ClientInvulnSeconds + 8s)` сразу после `TeleportPlayerToRespawnPosition` — защита на весь fade-path
- **FINALIZE:** не сбрасывает invuln, только продлевает если осталось меньше `ClientInvulnSeconds`
- **`IsLocalPlayerDamageBlocked()`:** единая проверка invuln + `isRespawnInProgress` (кроме therapeutic damage)
- **Harmony:** блок урона в `ChangeHealth`, `ApplyDamageInfo`, `Kill` через `IsLocalPlayerDamageBlocked`
- **Fika coop:** патч `FikaPlayer.HandleDamagePacket` — сетевой урон от хоста не обходит локальные патчи
- **`IsProfileInvulnerable`:** локальный игрок считается неуязвимым и во время `isRespawnInProgress`
- **HTTP sync:** игнор server `invuln=false` во время локального disable

## [3.0.0-alpha.7] — 2026-06-12

### Fixed (client freeze после респавна)
- **Stack overflow / зависание:** `IsPlayerInvulnerable()` без side-effects — истечение invuln только из `Update()`
- **INVULNERABILITY_END loop:** guard `invulnDisableCoroutine`, сброс флага invuln до therapeutic damage, bypass-патч `IsTherapeuticDamageInProgress`
- **Респавн lag:** `TryClearEffectsForRespawnNetworkSync` вместо тяжёлого `TryClearAllStatusEffects` (~1300 строк reflection на main thread)
- **HTTP sync:** игнор server `invuln=false` во время локального disable / если invuln уже снята

## [3.0.0-alpha.6] — 2026-06-12

### Fixed (Fika coop)
- **Эффекты на хосте:** `TryForceRemoveAllActiveEffects` + `TryHealWithNetworkSync` — ForceRemove через `IReadOnlyList_0` отправляет RemoveEffect в Fika
- **Неуязвимость coop:** FIKA broadcast `DesmatchInvulnerabilityPacket`, `IsProfileInvulnerable`, патч `Player.ApplyDamageInfo`
- **Debounce invuln:** исправлен спам `DisablePlayerInvulnerability` (guard `isInvulnDisableInProgress`)
- **Respawn FIKA:** broadcast `DesmatchRespawnPacket`, хост очищает эффекты у remote player
- **HTTP invuln sync:** PascalCase JSON (`Invulnerable`, `InvulnUntil`) для SPT server

## [3.0.0-alpha.5] — 2026-06-12

### Fixed
- **Респавн:** `FinalizeRespawnAfterEffects()` в конце fade-path — invuln, `IsPlayerDead=false`, HTTP respawn/teleport
- **Смерть:** debounce `SetPlayerCriticalState` (cooldown 2s) + блок Kill при активном респавне/критическом состоянии
- **Health API 16.9/Fika:** новый `DesmatchHealthReflection.cs` — обход `Invalid generic instantiation` через reflection Invoke
- **HTTP sync:** health JSON изолирован (`GetPlayerHealthDataSafe`) — `/save-player-data` не блокируется ошибками health
- **FIKA:** отложенная инициализация (`TryEnableFikaIntegration` на raid start / network event), без false negative в Awake
- **Effects reflection:** `Unsubscribers`, `HealthEffectsController_0`, снижен log spam (Warning → Debug после первого раза)

## [3.0.0-alpha.4] — 2026-06-12

### Fixed
- **Респавн:** `Invalid generic instantiation` при чтении `health.Energy/Hydration` на EFT 16.9 — чтение через `HealthValue_0/HealthValue_1`
- **Ручной респавн (F10):** `HandleRespawn()` вызывается на клиенте сразу; HTTP к серверу — best-effort
- **Harmony:** `GamePatches.SetPlugin()` из `Awake()` + lazy resolve (патч смерти мог не находить plugin)
- **Уведомления:** emoji удаляются в `ShowNotification()` (EFT UI их не показывает)

## [3.0.0-alpha.3] — 2026-06-12

### Fixed
- **Критично:** deadlock при запуске — `DesmatchHttpHelper` теперь использует `RequestHandler.PostJson` (Task.Run), а не `PostJsonAsync().GetResult()` на main thread
- Тест соединения с сервером перенесён из `Awake()` в `Start()` (coroutine), чтобы не блокировать BepInEx Chainloader

## [3.0.0-alpha.2] — 2026-06-12

### Added
- `pack_desmatchmode4.py` — сборка + деплой + ZIP для тестового ПК
- Сервер: все 12 HTTP routes (порт с mod.ts)
- `DesmatchConfigService`, `DesmatchPlayerDataService`, `DesmatchRespawnService`, `DesmatchDefibrillatorService`
- `config/config.json` для SPT 4 server mod

### Fixed
- Defibrillator inventory lookup: `_id` вместо `_tpl`

## [3.0.0-alpha.1] — 2026-06-12

### Added
- Fork `DesmatchMode4` — отдельная папка от `DesmatchMode` v2.5.0
- Клиент: BepInPlugin `DesmatchMode4`, namespace `DesmatchMode4`
- `DesmatchHttpHelper` — совместимость с SPT 4 async RequestHandler
- `DesmatchNetSerialization` — Vector3 для Fika 2.3 LiteNetLib
- Сервер C# для SPT 4: ModGuid `com.desmatchmode4.server`
- HTTP routes: `/singleplayer/desmatch/test`, `/singleplayer/desmatch/ping`

### Changed
- TargetFramework client: `netstandard2.1` (Fika 2.3)
- Fika: `Fika.Core.Networking.LiteNetLib.*` вместо `LiteNetLib.*`
- Fika send: `SendData(..., broadcast: true)` вместо `SendDataToAll`
- Health effects: `NetworkBodyEffectsAbstractClass` вместо `GClass2813`
- Удалён fallback `GClass966.Play` (не аудио-класс в 16.9)

### Unchanged (legacy)
- `client-mods/DesmatchMode` и `server-mods/DesmatchMode` — без изменений
