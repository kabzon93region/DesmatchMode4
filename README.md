# Desmatch Mode 4



**GitHub:** [kabzon93region](https://github.com/kabzon93region)

**Combo-мод (клиент + сервер) для SPT 4 + Fika.** Форк DesmatchMode для SPT 4 с интеграцией Fika 2.3. В разработке.



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
