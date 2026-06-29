# Changelog — DesmatchMode4

## [3.0.23] — 2026-06-29

### Changed
- **Respawn Pipeline:** описания шагов — только свой тег в подсказке (без списка всех меток в каждом пункте)
- **Metabolism steps (06, 19, 21):** по умолчанию **выключены** — принудительный сброс метаболизма может остановить drain еды/воды после revive
- Остальные шаги пайплайна по умолчанию **включены**; шаги метаболизма выполняются только если явно включены в cfg

### Fixed
- Стабильный метаболизм после revive без включения metabolism-restore шагов (подтверждено тестом)

## [3.0.22] — 2026-06-29

### Added
- **Respawn Pipeline:** 22 нумерованных галочки в `DesmatchMode4.cfg` → секция `Respawn Pipeline` — каждый шаг revive/finalize/invuln-penalty можно отключить для отладки метаболизма
- Метки в описаниях: `[CRITICAL]`, `[recommended]`, `[cosmetic]`, `[optional]`, `[debug]`
- **22 Legacy Five Stage At Invuln End** — включить старый 5-stage (известно ломает метаболизм) для сравнения
- В логе при пропуске: `[PIPELINE] SKIP: <шаг>`

## [3.0.21] — 2026-06-29

### Fixed
- **Metabolism после revive:** через ~3 сек (конец invuln) метаболизм замирал — еда/вода переставали убывать, отрицательная гидратация от еды не работала
- **Причина:** penalty после invuln вызывал `RestorePlayerHealth4Stages` + `TryHealWithNetworkSync`, что снимало эффект `Existence` и сбрасывало rates регенерации
- **Fix:** metabolism-safe penalty (ClearNegativeEffects + lightweight heal + restore Existence/Boolean_0), без 5-stage pipeline и без ForceRemove всех эффектов; отложенный re-sync метаболизма через 0.5с

## [3.0.20] — 2026-06-29

### Fixed
- **Manual respawn (F10):** снова работает в любой момент в рейде (не только в состоянии смерти) и всегда запускает полный пайплайн респавна
- **F10 gate:** убрана блокировка `IsPlayerDead` для ручного респавна; остаются только проверки `in raid`, `not in progress`, `debounce`
- **Respawn delay:** убран принудительный минимум auto-delay, т.к. ручной респавн больше не зависит от окна после смерти
- **Penalty после invuln:** сохранён полный 5-этапный пайплайн лечения (как в стабильных сборках), с терапевтическим уронам до лечения

## [3.0.19] — 2026-06-29

### Fixed
- **Penalty после invuln:** возвращён полный 5-этапный пайплайн лечения (как в стабильных сборках), с терапевтическим уронам до лечения
- **F10 окно:** при включённом auto-respawn теперь принудительный минимум задержки 1.5 сек (если в cfg было 0.1–0.5s), чтобы ручной респавн успевал срабатывать
- **Respawn validation:** на старых конфигах `Respawn Delay` автоматически поднимается до минимального значения для F10

## [3.0.18] — 2026-06-29

### Fixed
- **F10 / ручной респавн:** автовозрождение запускало fade сразу, без `Respawn Delay` — окна для F10 не было. Теперь auto идёт через задержку; F10 отменяет ожидание и возрождает немедленно
- **F10 only:** опция `Enable Auto Respawn` — при `false` после смерти ждёт только F10
- **Penalty после invuln:** восстановлен терапевтический урон (ноги/живот/руки) + лёгкое лечение + сброс движения — сбрасывает залипшую хромоту/стоны при визуально полном HP
- **Respawn Delay по умолчанию:** 3 сек (было 0.5) — больше времени на F10

## [3.0.17] — 2026-06-29

### Fixed (critical — mod not loading)
- **BepInEx:** версия в `[BepInPlugin]` была `3.0.0-alpha.16` — BepInEx считает её invalid и **полностью пропускает плагин** (`Skipping type ... because its version is invalid`). Заменено на numeric semver `3.0.17`

## [3.0.16] — 2026-06-29

### Fixed (metabolism + hands after revive)
- **Metabolism:** после revive вызываются `UnpauseAllEffectsOnPlayer`, `RevealWeapon`/Fika `ToggleDowned(false)`, сброс `Boolean_0` и `IsAlive`
- **Hands busy / invisible gun:** убран `Input.ResetInputAxes` и агрессивный StopShooting при смерти; после revive — `WriteCancelApplyingItemPacket`, `RevealWeapon`, `SetEmptyHands` fallback, разблокировка ввода
- **Animators:** повторное включение Body/Arms animator и CharacterController если остались выключены

## [3.0.0-alpha.15] — 2026-06-29

### Fixed (metabolism freeze after manual revive)
- **IsAlive / Boolean_0:** после revive явно восстанавливаются `IsAlive=true`, `Boolean_0=false`, `DamageCoeff=1`, `UnpauseAllEffects()` — метаболизм снова тикает
- **Lightweight revive path:** один проход `TryLightweightReviveHeal` вместо 2–3 вызовов `RestorePlayerHealth4Stages` с ForceRemove и сбросом таймеров
- **Invuln end:** убраны терапевтический урон + повторное тяжёлое лечение; только quiet heal + metabolism restore
- **Fade-path:** убраны этапы therapeutic damage / wait / re-heal — меньше лагов на main thread
- **F10 gate:** ручной респавн только если `IsPlayerDead`, с debounce 3 сек
- **ChangeHealth patch:** не блокирует existence/dehydration/exhaustion/stimulator/medkit drains во время invuln
- **Invuln sync:** Unix timestamp (ms) вместо `Time.time * 1000`; сервер → клиент через remaining duration
- **Server manual respawn:** не планирует redundant `ScheduleRespawnAsync` — клиент уже делает revive локально

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
