# Desmatch Mode 4

[![Release](https://img.shields.io/badge/release-v3.0.23-blue)](https://github.com/kabzon93region/DesmatchMode4)
[![EFT](https://img.shields.io/badge/EFT-16%2E9-orange)](https://www.escapefromtarkov.com/)
[![SPT](https://img.shields.io/badge/SPT-4.0.13-blue)](https://sp-tarkov.com/)
[![Fika](https://img.shields.io/badge/Fika-2%2E3%2Ex-purple)](https://github.com/project-fika/Fika-Plugin)
[![BepInEx](https://img.shields.io/badge/BepInEx-5%2E4%2Ex-yellow)](https://github.com/BepInEx/BepInEx)
![Deployment](https://img.shields.io/badge/deployment-server_client%2Cheadless_all-lightgrey)

Desmatch-режим для SPT 4 + Fika 2.3: автовозрождение в рейде, неуязvимость, penalty после invuln, интеграция с headless coop.

| | |
|---|---|
| **Разработчик** | [kabzon93region](https://github.com/kabzon93region) |
| **Версия** | 3.0.23 |
| **GitHub** | [DesmatchMode4](https://github.com/kabzon93region/DesmatchMode4) |
| **Deployment** | `(server_client,headless_all)` |
| **Тип** | combo (client + server) |

## Возможности

- Авто- и ручное (F10) возрождение в рейде с fade-эффектами
- Неуязvимость после revive (настраиваемое время)
- Penalty после invuln: терапевтический урон + лечение (сброс хромоты/переломов)
- **Respawn Pipeline** — 22 нумерованных шага в `DesmatchMode4.cfg`, каждый можно отключить
- Fika coop: broadcast respawn / invuln
- Серверная часть (C#): базовые маршруты SPT

## Respawn Pipeline и метаболизм

Секция **`[Respawn Pipeline]`** в `BepInEx/config/DesmatchMode4.cfg`.

По умолчанию **все шаги включены**, кроме шагов метаболизма:

| Шаг | Название | Default |
|-----|----------|---------|
| 06 | Metabolism Restore (revive) | **off** |
| 19 | Penalty Metabolism Restore | **off** |
| 21 | Penalty Delayed Metabolism | **off** |

Шаги с тегом `[metabolism]` в подсказке cfg: принудительный сброс `Existence` / `Boolean_0` **может остановить** пассивное убывание еды и воды после revive. Включайте только при отладке.

Шаг **22 Legacy Five Stage** — debug, известно ломает метаболизм (default off).

## Установка

1. Zip: `DesmatchMode4_(server_client,headless_all)_vX.Y.Z_*.zip` через SPT Mod Manager
2. Client: `BepInEx/plugins/DesmatchMode4/DesmatchMode4.dll`
3. Server: `SPT/user/mods/DesmatchMode4/`
4. Coop: установить на **все** клиенты + server + headless

## Требования

- **EFT**: 16.9.x
- **SPT**: 4.0.13
- **Fika**: 2.3.x
- **BepInEx**: 5.4.x

## Известные проблемы

- Сервер: не все HTTP routes перенесены с legacy DesmatchMode (TS)
- CustomItemService (дефибриллятор) не реализован
- HTTP `/singleplayer/desmatch/*` может конфликтовать со старым DesmatchMode TS
- Принудительный metabolism-restore (шаги 06/19/21) может сломать drain еды/воды — **оставляйте выключенным**, если метаболизм работает штатно

## Совместимость

- `(server_client)` — SPT server + BepInEx client
- `(headless_all)` — headless host + player clients в Fika coop

## Поддержать проект

**[DonationAlerts → kabzon93region](https://www.donationalerts.com/r/kabzon93region)**
