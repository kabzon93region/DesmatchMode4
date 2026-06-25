using BepInEx.Configuration;

namespace DesmatchMode4.Settings
{
    /// <summary>
    /// Менеджер настроек - управляет конфигурацией мода
    /// </summary>
    public static class Settings
    {
        // Основные настройки
        public static ConfigEntry<bool> EnableDesmatchMode;
        public static ConfigEntry<float> RespawnDelay;
        public static ConfigEntry<UnityEngine.KeyCode> ManualRespawnKey;
        public static ConfigEntry<float> InvulnerabilityTime;
        
        // Настройки уведомлений
        public static ConfigEntry<bool> ShowMainNotifications;
        public static ConfigEntry<bool> ShowFadeEffectNotifications;
        public static ConfigEntry<bool> ShowHealingNotifications;
        public static ConfigEntry<bool> ShowDamageNotifications;
        public static ConfigEntry<bool> ShowAutoRespawnNotifications;
        
        // Настройки звуковых эффектов
        public static ConfigEntry<bool> EnableTinnitusEffect;
        public static ConfigEntry<float> TinnitusDuration;
        public static ConfigEntry<bool> TinnitusFadeOut;
        public static ConfigEntry<bool> ShowSoundEffectNotifications;
        
        // Константы для валидации и безопасности
        public const int MIN_RESPAWN_DELAY = 0; // Минимальная задержка респавна (мс)
        public const int MAX_RESPAWN_DELAY = 30000; // Максимальная задержка респавна (мс) - 30 секунд
        public const int MIN_INVULN_SECONDS = 0; // Минимальное время неуязвимости (с)
        public const int MAX_INVULN_SECONDS = 60; // Максимальное время неуязвимости (с) - 1 минута
        public const int DEFAULT_RESPAWN_DELAY = 500; // Безопасное значение по умолчанию (мс) - 0.5 секунды
        public const int DEFAULT_INVULN_SECONDS = 3; // Боевая неуязvимость после респавна (с)
        /// <summary>Доп. секунды invuln на время fade-path (не показываются игроку).</summary>
        public const float INVULN_FADE_BUFFER_SECONDS = 8f;
        /// <summary>Ускорение рассеивания чёрного экрана после телепорта (+30%).</summary>
        public const float RESPAWN_WAKE_FADE_SPEED_MULTIPLIER = 1.3f;
        
        /// <summary>
        /// Валидация задержки респавна
        /// </summary>
        public static int ValidateRespawnDelay(int respawnDelay)
        {
            if (respawnDelay < MIN_RESPAWN_DELAY)
                return MIN_RESPAWN_DELAY;
            if (respawnDelay > MAX_RESPAWN_DELAY)
                return MAX_RESPAWN_DELAY;
            return respawnDelay;
        }
        
        /// <summary>
        /// Валидация времени неуязвимости
        /// </summary>
        public static int ValidateInvulnSeconds(int invulnSeconds)
        {
            if (invulnSeconds < MIN_INVULN_SECONDS)
                return MIN_INVULN_SECONDS;
            if (invulnSeconds > MAX_INVULN_SECONDS)
                return MAX_INVULN_SECONDS;
            return invulnSeconds;
        }
    }
}
