using System;
using UnityEngine;
using EFT;
using Fika.Core.Networking;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Comfort.Common;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace DesmatchMode4
{
    /// <summary>
    /// Менеджер FIKA коммуникации для DesmatchMode
    /// </summary>
    public class FikaCommunicationManager
    {
        private static FikaCommunicationManager instance;
        private IFikaNetworkManager fikaNetworkManager;
        private bool fikaAvailable = false;
        private bool packetsRegistered = false;

        // События для уведомления основного мода
        public event Action<DesmatchFikaPackets.DesmatchServerResponsePacket> OnServerResponseReceived;
        public event Action<DesmatchFikaPackets.DesmatchSettingsUpdatePacket> OnSettingsUpdateReceived;
        public event Action<DesmatchFikaPackets.DesmatchInvulnerabilityPacket> OnInvulnerabilityUpdateReceived;
        public event Action<DesmatchFikaPackets.DesmatchRespawnPacket> OnRespawnReceived;
        public event Action OnFikaBecameAvailable;

        public static FikaCommunicationManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new FikaCommunicationManager();
                }
                return instance;
            }
        }

        /// <summary>
        /// Инициализация FIKA коммуникации
        /// </summary>
        public void Initialize()
        {
            try
            {
                // Подписка на события FIKA
                FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
                FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnNetworkManagerDestroyed);
                
                UnityEngine.Debug.Log("[FIKA_COMM] FIKA Communication Manager инициализирован");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка инициализации FIKA Communication Manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверка доступности FIKA (singleton в рейде)
        /// </summary>
        public bool IsFikaAvailable()
        {
            try
            {
                return GetNetworkManagerFromSingletons() != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Готовность FIKA для отправки пакетов
        /// </summary>
        public bool IsIntegrationReady()
        {
            return fikaAvailable && fikaNetworkManager != null && packetsRegistered;
        }

        /// <summary>
        /// Отложенная активация: проверяет singleton и регистрирует пакеты при входе в рейд
        /// </summary>
        public bool TryRefreshAvailability()
        {
            try
            {
                if (fikaNetworkManager == null)
                {
                    fikaNetworkManager = GetNetworkManagerFromSingletons();
                }

                if (fikaNetworkManager == null)
                {
                    return false;
                }

                var wasAvailable = fikaAvailable;
                fikaAvailable = true;

                if (!packetsRegistered)
                {
                    RegisterPackets();
                }

                if (!wasAvailable)
                {
                    OnFikaBecameAvailable?.Invoke();
                }

                return fikaAvailable;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[FIKA_COMM] TryRefreshAvailability: {ex.Message}");
                return false;
            }
        }

        private IFikaNetworkManager GetNetworkManagerFromSingletons()
        {
            if (fikaNetworkManager != null)
            {
                return fikaNetworkManager;
            }

            var fikaServer = Singleton<FikaServer>.Instance;
            if (fikaServer != null)
            {
                return fikaServer;
            }

            var fikaClient = Singleton<FikaClient>.Instance;
            return fikaClient;
        }

        /// <summary>
        /// Обработчик создания FIKA NetworkManager
        /// </summary>
        private void OnNetworkManagerCreated(FikaNetworkManagerCreatedEvent evt)
        {
            try
            {
                fikaNetworkManager = evt.Manager;
                fikaAvailable = true;

                UnityEngine.Debug.Log("[FIKA_COMM] FIKA NetworkManager создан, регистрируем пакеты");

                RegisterPackets();
                OnFikaBecameAvailable?.Invoke();

                UnityEngine.Debug.Log("[FIKA_COMM] FIKA пакеты зарегистрированы успешно");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка в OnNetworkManagerCreated: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработчик уничтожения FIKA NetworkManager
        /// </summary>
        private void OnNetworkManagerDestroyed(FikaNetworkManagerDestroyedEvent evt)
        {
            try
            {
                fikaNetworkManager = null;
                fikaAvailable = false;
                packetsRegistered = false;

                UnityEngine.Debug.Log("[FIKA_COMM] FIKA NetworkManager уничтожен");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка в OnNetworkManagerDestroyed: {ex.Message}");
            }
        }

        /// <summary>
        /// Регистрация FIKA пакетов
        /// </summary>
        private void RegisterPackets()
        {
            try
            {
                if (packetsRegistered)
                {
                    return;
                }

                if (fikaNetworkManager == null)
                {
                    UnityEngine.Debug.LogWarning("[FIKA_COMM] NetworkManager недоступен для регистрации пакетов");
                    return;
                }

                // Регистрируем обработчики пакетов (используем правильный API FIKA)
                try
                {
                    fikaNetworkManager.RegisterPacket<DesmatchFikaPackets.DesmatchServerResponsePacket>(OnServerResponsePacketReceived);
                    UnityEngine.Debug.Log("[FIKA_COMM] DesmatchServerResponsePacket зарегистрирован");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка регистрации DesmatchServerResponsePacket: {ex.Message}");
                }

                try
                {
                    fikaNetworkManager.RegisterPacket<DesmatchFikaPackets.DesmatchSettingsUpdatePacket>(OnSettingsUpdatePacketReceived);
                    UnityEngine.Debug.Log("[FIKA_COMM] DesmatchSettingsUpdatePacket зарегистрирован");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка регистрации DesmatchSettingsUpdatePacket: {ex.Message}");
                }

                try
                {
                    fikaNetworkManager.RegisterPacket<DesmatchFikaPackets.DesmatchInvulnerabilityPacket>(OnInvulnerabilityPacketReceived);
                    UnityEngine.Debug.Log("[FIKA_COMM] DesmatchInvulnerabilityPacket зарегистрирован");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка регистрации DesmatchInvulnerabilityPacket: {ex.Message}");
                }

                try
                {
                    fikaNetworkManager.RegisterPacket<DesmatchFikaPackets.DesmatchPlayerDataPacket>(OnPlayerDataPacketReceived);
                    UnityEngine.Debug.Log("[FIKA_COMM] DesmatchPlayerDataPacket зарегистрирован");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка регистрации DesmatchPlayerDataPacket: {ex.Message}");
                }

                try
                {
                    fikaNetworkManager.RegisterPacket<DesmatchFikaPackets.DesmatchRespawnPacket>(OnRespawnPacketReceived);
                    UnityEngine.Debug.Log("[FIKA_COMM] DesmatchRespawnPacket зарегистрирован");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка регистрации DesmatchRespawnPacket: {ex.Message}");
                }

                try
                {
                    fikaNetworkManager.RegisterPacket<DesmatchFikaPackets.DesmatchDeathNotificationPacket>(OnDeathNotificationPacketReceived);
                    UnityEngine.Debug.Log("[FIKA_COMM] DesmatchDeathNotificationPacket зарегистрирован");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка регистрации DesmatchDeathNotificationPacket: {ex.Message}");
                }

                packetsRegistered = true;
                UnityEngine.Debug.Log("[FIKA_COMM] FIKA пакеты зарегистрированы успешно");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка регистрации пакетов: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправка данных игрока на сервер
        /// </summary>
        public void SendPlayerData(DesmatchFikaPackets.DesmatchPlayerDataPacket packet)
        {
            try
            {
                if (!fikaAvailable || fikaNetworkManager == null)
                {
                    UnityEngine.Debug.LogWarning("[FIKA_COMM] FIKA недоступна для отправки данных игрока");
                    return;
                }

                packet.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                // Определяем, сервер это или клиент
                var fikaServer = Singleton<FikaServer>.Instance;
                var fikaClient = Singleton<FikaClient>.Instance;
                
                if (fikaServer != null)
                {
                    fikaServer.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast: true);
                }
                else if (fikaClient != null)
                {
                    fikaClient.SendData(ref packet, DeliveryMethod.ReliableOrdered);
                }
                
                UnityEngine.Debug.Log("[FIKA_COMM] Данные игрока отправлены через FIKA");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка отправки данных игрока: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправка запроса респавна
        /// </summary>
        public void SendRespawnRequest(DesmatchFikaPackets.DesmatchRespawnPacket packet)
        {
            try
            {
                if (!fikaAvailable || fikaNetworkManager == null)
                {
                    UnityEngine.Debug.LogWarning("[FIKA_COMM] FIKA недоступна для отправки запроса респавна");
                    return;
                }

                packet.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                // Определяем, сервер это или клиент
                var fikaServer = Singleton<FikaServer>.Instance;
                var fikaClient = Singleton<FikaClient>.Instance;
                
                if (fikaServer != null)
                {
                    fikaServer.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast: true);
                }
                else if (fikaClient != null)
                {
                    fikaClient.SendData(ref packet, DeliveryMethod.ReliableOrdered);
                }
                
                UnityEngine.Debug.Log("[FIKA_COMM] Запрос респавна отправлен через FIKA");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка отправки запроса респавна: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправка уведомления о смерти
        /// </summary>
        public void SendDeathNotification(DesmatchFikaPackets.DesmatchDeathNotificationPacket packet)
        {
            try
            {
                if (!fikaAvailable || fikaNetworkManager == null)
                {
                    UnityEngine.Debug.LogWarning("[FIKA_COMM] FIKA недоступна для отправки уведомления о смерти");
                    return;
                }

                packet.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                // Определяем, сервер это или клиент
                var fikaServer = Singleton<FikaServer>.Instance;
                var fikaClient = Singleton<FikaClient>.Instance;
                
                if (fikaServer != null)
                {
                    fikaServer.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast: true);
                }
                else if (fikaClient != null)
                {
                    fikaClient.SendData(ref packet, DeliveryMethod.ReliableOrdered);
                }
                
                UnityEngine.Debug.Log("[FIKA_COMM] Уведомление о смерти отправлено через FIKA");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка отправки уведомления о смерти: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправка синхронизации неуязвимости
        /// </summary>
        public void SendInvulnerabilitySync(DesmatchFikaPackets.DesmatchInvulnerabilityPacket packet)
        {
            try
            {
                if (!fikaAvailable || fikaNetworkManager == null)
                {
                    UnityEngine.Debug.LogWarning("[FIKA_COMM] FIKA недоступна для отправки синхронизации неуязвимости");
                    return;
                }

                packet.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                // Определяем, сервер это или клиент
                var fikaServer = Singleton<FikaServer>.Instance;
                var fikaClient = Singleton<FikaClient>.Instance;
                
                if (fikaServer != null)
                {
                    fikaServer.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast: true);
                }
                else if (fikaClient != null)
                {
                    fikaClient.SendData(ref packet, DeliveryMethod.ReliableOrdered);
                }
                
                UnityEngine.Debug.Log("[FIKA_COMM] Синхронизация неуязвимости отправлена через FIKA");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка отправки синхронизации неуязвимости: {ex.Message}");
            }
        }

        // Обработчики входящих пакетов
        private void OnServerResponsePacketReceived(DesmatchFikaPackets.DesmatchServerResponsePacket packet)
        {
            try
            {
                UnityEngine.Debug.Log($"[FIKA_COMM] Получен ответ сервера: Success={packet.Success}, Message={packet.Message}");
                OnServerResponseReceived?.Invoke(packet);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка обработки ответа сервера: {ex.Message}");
            }
        }

        private void OnSettingsUpdatePacketReceived(DesmatchFikaPackets.DesmatchSettingsUpdatePacket packet)
        {
            try
            {
                UnityEngine.Debug.Log($"[FIKA_COMM] Получено обновление настроек: RespawnDelay={packet.RespawnDelay}, InvulnSeconds={packet.InvulnSeconds}");
                OnSettingsUpdateReceived?.Invoke(packet);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка обработки обновления настроек: {ex.Message}");
            }
        }

        private void OnInvulnerabilityPacketReceived(DesmatchFikaPackets.DesmatchInvulnerabilityPacket packet)
        {
            try
            {
                UnityEngine.Debug.Log($"[FIKA_COMM] Получена синхронизация неуязвимости: IsInvulnerable={packet.IsInvulnerable}, TimeRemaining={packet.InvulnTimeRemaining}");
                OnInvulnerabilityUpdateReceived?.Invoke(packet);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка обработки синхронизации неуязвимости: {ex.Message}");
            }
        }

        private void OnPlayerDataPacketReceived(DesmatchFikaPackets.DesmatchPlayerDataPacket packet)
        {
            try
            {
                UnityEngine.Debug.Log($"[FIKA_COMM] Получены данные игрока: ProfileId={packet.PlayerProfileId}, Health={packet.Health}");
                // Здесь можно добавить обработку данных игрока
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка обработки данных игрока: {ex.Message}");
            }
        }

        private void OnRespawnPacketReceived(DesmatchFikaPackets.DesmatchRespawnPacket packet)
        {
            try
            {
                UnityEngine.Debug.Log($"[FIKA_COMM] Получен запрос респавна: IsManual={packet.IsManualRespawn}, Position={packet.RespawnPosition}");
                OnRespawnReceived?.Invoke(packet);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка обработки запроса респавна: {ex.Message}");
            }
        }

        private void OnDeathNotificationPacketReceived(DesmatchFikaPackets.DesmatchDeathNotificationPacket packet)
        {
            try
            {
                UnityEngine.Debug.Log($"[FIKA_COMM] Получено уведомление о смерти: ProfileId={packet.PlayerProfileId}, Cause={packet.DeathCause}");
                // Здесь можно добавить обработку уведомления о смерти
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка обработки уведомления о смерти: {ex.Message}");
            }
        }

        /// <summary>
        /// Очистка ресурсов
        /// </summary>
        public void Cleanup()
        {
            try
            {
                // Отписка от событий FIKA
                FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
                FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnNetworkManagerDestroyed);

                fikaNetworkManager = null;
                fikaAvailable = false;
                packetsRegistered = false;

                UnityEngine.Debug.Log("[FIKA_COMM] FIKA Communication Manager очищен");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FIKA_COMM] Ошибка очистки FIKA Communication Manager: {ex.Message}");
            }
        }
    }
}