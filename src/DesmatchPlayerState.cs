using UnityEngine;

namespace DesmatchMode4.Player
{
    /// <summary>
    /// Менеджер состояния игрока - управляет состоянием игрока в рейде
    /// </summary>
    public static class PlayerState
    {
        // Состояние игрока
        public static bool IsInRaid = false;
        public static bool IsPlayerDead = false;
        public static bool IsPlayerInvulnerable = false;
        public static float InvulnUntil = 0f; // Время окончания неуязвимости (Time.time)
        
        // Клиентские настройки для единой обработки респавна (безопасные значения по умолчанию)
        public static int ClientRespawnDelay = Settings.Settings.DEFAULT_RESPAWN_DELAY; // Задержка респавна в миллисекундах
        public static int ClientInvulnSeconds = Settings.Settings.DEFAULT_INVULN_SECONDS; // Время неуязвимости в секундах
        
        /// <summary>
        /// Обновляем клиентские настройки из сервера с валидацией
        /// </summary>
        public static void UpdateClientSettings(int respawnDelay, int invulnSeconds)
        {
            // Валидация и санитизация respawnDelay
            int validatedRespawnDelay = Settings.Settings.ValidateRespawnDelay(respawnDelay);
            if (validatedRespawnDelay != respawnDelay)
            {
                UnityEngine.Debug.LogWarning($"🔄 [SETTINGS] respawnDelay скорректирован: {respawnDelay}ms → {validatedRespawnDelay}ms");
            }
            
            // Валидация и санитизация invulnSeconds
            int validatedInvulnSeconds = Settings.Settings.ValidateInvulnSeconds(invulnSeconds);
            if (validatedInvulnSeconds != invulnSeconds)
            {
                UnityEngine.Debug.LogWarning($"🔄 [SETTINGS] invulnSeconds скорректирован: {invulnSeconds}s → {validatedInvulnSeconds}s");
            }
            
            // Применяем валидированные значения
            ClientRespawnDelay = validatedRespawnDelay;
            ClientInvulnSeconds = validatedInvulnSeconds;
            
            UnityEngine.Debug.Log($"🔄 [SETTINGS] Настройки обновлены: delay={ClientRespawnDelay}ms, invuln={ClientInvulnSeconds}s");
        }
        
        /// <summary>
        /// Проверка, истекла ли неуязвимость
        /// </summary>
        public static bool IsInvulnerabilityExpired()
        {
            return Time.time >= InvulnUntil;
        }
        
        /// <summary>
        /// Установка неуязвимости
        /// </summary>
        public static void SetInvulnerability(float duration)
        {
            IsPlayerInvulnerable = true;
            InvulnUntil = Time.time + duration;
        }
        
        /// <summary>
        /// Снятие неуязвимости
        /// </summary>
        public static void RemoveInvulnerability()
        {
            IsPlayerInvulnerable = false;
            InvulnUntil = 0f;
        }
        
        /// <summary>
        /// Сброс состояния игрока
        /// </summary>
        public static void ResetPlayerState()
        {
            IsInRaid = false;
            IsPlayerDead = false;
            IsPlayerInvulnerable = false;
            InvulnUntil = 0f;
        }
    }
}
