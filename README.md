# Desmatch Mode 4

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![EFT](https://img.shields.io/badge/EFT-16%2E9-orange)](https://www.escapefromtarkov.com/)
[![SPT](https://img.shields.io/badge/SPT-4.0.13-blue)](https://sp-tarkov.com/)
[![Fika](https://img.shields.io/badge/Fika-2%2E3%2Ex-purple)](https://github.com/project-fika/Fika-Plugin)
[![BepInEx](https://img.shields.io/badge/BepInEx-5%2E4%2Ex-yellow)](https://github.com/BepInEx/BepInEx)
![Deployment](https://img.shields.io/badge/deployment-server_client%2Cheadless_all-lightgrey)

﻿# Desmatch Mode 4

| | |
|---|---|
| **Разработчик** | [kabzon93region](https://github.com/kabzon93region) |
| **Версия** | 3.0.0 |
| **GitHub** | [DesmatchMode4](https://github.com/kabzon93region/DesmatchMode4) |
| **Deployment** | `(server_client,headless_all)` |
| **Тип** | combo (client + server) |

## Статус

**Alpha/Beta** — WIP. Серверная часть в процессе переноса с TypeScript на C#.

## Возможности (текущие)

- Базовый скелет сервера: /test и /ping маршруты
- Клиентская часть портирована
- Интеграция с Fika 2.3

## Установка

1. Скопировать DesmatchMode4.dll в BepInEx/plugins/
2. Серверная часть — копировать DesmatchMode4/ в SPT/user/mods/

## Требования

- **SPT**: 4.0.x
- **Fika**: 2.3.x
- **BepInEx**: 5.4.x

## Известные проблемы

- Сервер: только /test и /ping; 9 HTTP routes не перенесены с TS
- CustomItemService (дефибриллятор) не реализован
- HTTP /singleplayer/desmatch/* конфликтует со старым DesmatchMode TS
- **Не отдавать вне нашего стенда** — alpha/WIP

## Совместимость

- server_client,headless_all — сервер + клиент + headless

## Поддержать проект

Разовый донат картой РФ, СБП, ЮMoney, VK Pay:
**[DonationAlerts → kabzon93region](https://www.donationalerts.com/r/kabzon93region)**
