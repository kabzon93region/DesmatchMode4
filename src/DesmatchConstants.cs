using System;

namespace DesmatchMode4
{
    /// <summary>
    /// Общие константы для DesmatchMode
    /// Используются как клиентом, так и сервером
    /// </summary>
    public static class DesmatchConstants
    {
        // Версия мода
        public const string MOD_VERSION = "2.5.0";
        public const string MOD_NAME = "DesmatchMode4";
        public const string MOD_AUTHOR = "DesmatchTeam";
        
        // Настройки по умолчанию
        public const int DEFAULT_RESPAWN_DELAY_SECONDS = 5;
        public const int DEFAULT_INVULNERABILITY_SECONDS = 10;
        public const int MIN_RESPAWN_DELAY_SECONDS = 1;
        public const int MAX_RESPAWN_DELAY_SECONDS = 30;
        public const int MIN_INVULNERABILITY_SECONDS = 0;
        public const int MAX_INVULNERABILITY_SECONDS = 60;
        
        // Клавиши по умолчанию
        public const string DEFAULT_MANUAL_RESPAWN_KEY = "F10";
        public const string DEFAULT_SOUND_TEST_KEY = "F6"; // Изменено с F5 на F6, так как F5 теперь для использования дефибриллятора
        public const string DEFAULT_DEFIBRILLATOR_USE_KEY = "F5"; // Клавиша для использования дефибриллятора
        
        // HTTP маршруты
        public const string ROUTE_SAVE_PLAYER_DATA = "/singleplayer/desmatch/save-player-data";
        public const string ROUTE_RESPAWN_PLAYER = "/singleplayer/desmatch/respawn-player";
        public const string ROUTE_PLAYER_DIED = "/singleplayer/desmatch/player-died";
        public const string ROUTE_INVULNERABILITY_STATUS = "/singleplayer/desmatch/invulnerability-status";
        public const string ROUTE_GET_SETTINGS = "/singleplayer/desmatch/get-settings";
        public const string ROUTE_UPDATE_SETTINGS = "/singleplayer/desmatch/update-settings";
        
        // FIKA пакеты
        public const string FIKA_PACKET_PLAYER_DATA = "DesmatchPlayerData";
        public const string FIKA_PACKET_RESPAWN_REQUEST = "DesmatchRespawnRequest";
        public const string FIKA_PACKET_SERVER_RESPONSE = "DesmatchServerResponse";
        public const string FIKA_PACKET_SETTINGS_UPDATE = "DesmatchSettingsUpdate";
        public const string FIKA_PACKET_INVULNERABILITY = "DesmatchInvulnerability";
        public const string FIKA_PACKET_DEFIBRILLATOR_REQUEST = "DesmatchDefibrillatorRequest";
        public const string FIKA_PACKET_DEFIBRILLATOR_RESPONSE = "DesmatchDefibrillatorResponse";
        
        // Зоны респавна
        public const string DEFAULT_ZONE = "default_zone";
        public const string CUSTOM_ZONE = "custom_zone";
        
        // Типы респавна
        public const string RESPAWN_TYPE_AUTO = "auto";
        public const string RESPAWN_TYPE_MANUAL = "manual";
        public const string RESPAWN_TYPE_FORCED = "forced";
        
        // Статусы
        public const string STATUS_SUCCESS = "success";
        public const string STATUS_ERROR = "error";
        public const string STATUS_WARNING = "warning";
        public const string STATUS_INFO = "info";
        
        // Сообщения
        public const string MSG_RESPAWN_SUCCESS = "Респавн выполнен успешно";
        public const string MSG_RESPAWN_FAILED = "Ошибка респавна";
        public const string MSG_INVULNERABILITY_ENABLED = "Неуязвимость включена";
        public const string MSG_INVULNERABILITY_DISABLED = "Неуязвимость отключена";
        public const string MSG_HEALTH_RESTORED = "Здоровье восстановлено";
        public const string MSG_SETTINGS_UPDATED = "Настройки обновлены";
        
        // Логирование
        public const string LOG_PREFIX_CLIENT = "[CLIENT]";
        public const string LOG_PREFIX_SERVER = "[SERVER]";
        public const string LOG_PREFIX_FIKA = "[FIKA]";
        public const string LOG_PREFIX_HEALTH = "[HEALTH]";
        public const string LOG_PREFIX_RESPAWN = "[RESPAWN]";
        public const string LOG_PREFIX_INVULNERABILITY = "[INVULNERABILITY]";
        
        // Префиксы уведомлений (пустые — EFT UI не отображает emoji)
        public const string EMOJI_RESPAWN = "";
        public const string EMOJI_HEALTH = "";
        public const string EMOJI_INVULNERABILITY = "";
        public const string EMOJI_ERROR = "";
        public const string EMOJI_SUCCESS = "";
        public const string EMOJI_WARNING = "";
        public const string EMOJI_INFO = "";
        public const string EMOJI_SOUND = "";
        public const string EMOJI_FADE = "";
        public const string EMOJI_DEFIBRILLATOR = "";
        public const string EMOJI_BATTERY = "";
        public const string EMOJI_CHARGE = "";
        public const string EMOJI_REMOVED = "";
        
        // Валидация
        public static bool IsValidRespawnDelay(int delay)
        {
            return delay >= MIN_RESPAWN_DELAY_SECONDS && delay <= MAX_RESPAWN_DELAY_SECONDS;
        }
        
        public static bool IsValidInvulnerabilityTime(int time)
        {
            return time >= MIN_INVULNERABILITY_SECONDS && time <= MAX_INVULNERABILITY_SECONDS;
        }
        
        public static int ClampRespawnDelay(int delay)
        {
            return Math.Max(MIN_RESPAWN_DELAY_SECONDS, Math.Min(MAX_RESPAWN_DELAY_SECONDS, delay));
        }
        
        public static int ClampInvulnerabilityTime(int time)
        {
            return Math.Max(MIN_INVULNERABILITY_SECONDS, Math.Min(MAX_INVULNERABILITY_SECONDS, time));
        }
    }
}
