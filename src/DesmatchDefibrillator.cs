using System;
using System.Collections.Generic;
using UnityEngine;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.Communications;
using Fika.Core.Networking;
using Fika.Core.Modding.Events;
using Fika.Core.Modding;
using Fika.Core.Networking.LiteNetLib.Utils;
using Fika.Core.Networking.LiteNetLib;
using Newtonsoft.Json;
using Comfort.Common;
using DesmatchMode4.Settings;
using DesmatchMode4.Player;

namespace DesmatchMode4
{
    /// <summary>
    /// Модуль для работы с дефибриллятором
    /// Обрабатывает использование дефибриллятора по клавише F5 в рейде
    /// </summary>
    public class DesmatchDefibrillator
    {
        // Константы для дефибриллятора
        public static class DefibrillatorConstants
        {
            // ID предметов
            public const string DEFIBRILLATOR_ID = "64f8a1b2c3d4e5f678901234";
            public const string CUSTOM_BATTERY_CHARGED_ID = "64f8a1b2c3d4e5f678901235";
            public const string CUSTOM_BATTERY_DISCHARGED_ID = "64f8a1b2c3d4e5f678901236";
            public const string ORIGINAL_BATTERY_ID = "5e2aedd986f7746d404f3aa4";
            public const string BATTERY_PARENT_ID = "57864ee62459775490116fc1";
            
            // Клавиши
            public const string DEFIBRILLATOR_USE_KEY = "F5";
            
            // Время уведомлений
            public const int NOTIFICATION_DURATION_SECONDS = 10;
            
            // Количество слотов дефибриллятора
            public const int DEFIBRILLATOR_SLOTS_COUNT = 5;
            
            // FIKA пакеты
            public const string FIKA_PACKET_DEFIBRILLATOR_REQUEST = "DesmatchDefibrillatorRequest";
            public const string FIKA_PACKET_DEFIBRILLATOR_RESPONSE = "DesmatchDefibrillatorResponse";
            
            // Сообщения
            public const string MSG_DEFIBRILLATOR_NOT_FOUND = "Дефибриллятор не найден в спецслоте";
            public const string MSG_DEFIBRILLATOR_FOUND = "Дефибриллятор найден в спецслоте";
            public const string MSG_NO_BATTERIES = "В дефибрилляторе нет батареек";
            public const string MSG_BATTERY_FOUND = "Найдена батарейка в слоте {0}";
            public const string MSG_BATTERY_REMOVED = "Обычная батарейка удалена из слота {0}";
            public const string MSG_BATTERY_CHARGE_USED = "Заряд усиленной батарейки использован в слоте {0}";
            public const string MSG_BATTERY_NO_CHARGE = "Усиленная батарейка в слоте {0} разряжена";
            public const string MSG_DEFIBRILLATOR_USED = "Дефибриллятор использован успешно";
            public const string MSG_DEFIBRILLATOR_ERROR = "Ошибка использования дефибриллятора";
            
            // Эмодзи
            public const string EMOJI_DEFIBRILLATOR = "";
            public const string EMOJI_BATTERY = "";
            public const string EMOJI_CHARGE = "";
            public const string EMOJI_REMOVED = "";
        }
        
        /// <summary>
        /// Структура данных для запроса использования дефибриллятора
        /// </summary>
        [Serializable]
        public class DefibrillatorUsageRequest
        {
            public string PlayerId { get; set; }
            public string PlayerNickname { get; set; }
            public string DefibrillatorId { get; set; }
            public List<BatterySlotInfo> BatterySlots { get; set; }
            public long Timestamp { get; set; }
            
            public DefibrillatorUsageRequest()
            {
                BatterySlots = new List<BatterySlotInfo>();
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        
        /// <summary>
        /// Информация о слоте батарейки
        /// </summary>
        [Serializable]
        public class BatterySlotInfo
        {
            public int SlotIndex { get; set; }
            public string BatteryId { get; set; }
            public string BatteryName { get; set; }
            public int CurrentCharge { get; set; }
            public int MaxCharge { get; set; }
            public bool IsCustomBattery { get; set; }
            public bool IsOriginalBattery { get; set; }
            
            public BatterySlotInfo()
            {
                SlotIndex = -1;
                BatteryId = "";
                BatteryName = "";
                CurrentCharge = 0;
                MaxCharge = 0;
                IsCustomBattery = false;
                IsOriginalBattery = false;
            }
        }
        
        /// <summary>
        /// Ответ сервера на использование дефибриллятора
        /// </summary>
        [Serializable]
        public class DefibrillatorUsageResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public List<BatterySlotUpdate> UpdatedSlots { get; set; }
            public long Timestamp { get; set; }
            
            public DefibrillatorUsageResponse()
            {
                UpdatedSlots = new List<BatterySlotUpdate>();
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        
        /// <summary>
        /// Обновление слота батарейки
        /// </summary>
        [Serializable]
        public class BatterySlotUpdate
        {
            public int SlotIndex { get; set; }
            public string Action { get; set; } // "removed", "charge_used", "no_change"
            public string BatteryId { get; set; }
            public int NewCharge { get; set; }
            public string Message { get; set; }
            
            public BatterySlotUpdate()
            {
                SlotIndex = -1;
                Action = "no_change";
                BatteryId = "";
                NewCharge = 0;
                Message = "";
            }
        }
        
        /// <summary>
        /// Ответ сервера (HTTP)
        /// </summary>
        [Serializable]
        public class DefibrillatorServerResponse
        {
            [JsonProperty("status")]
            public string Status { get; set; }
            
            [JsonProperty("message")]
            public string Message { get; set; }
            
            [JsonProperty("sessionId")]
            public string SessionId { get; set; }
            
            [JsonProperty("result")]
            public DefibrillatorUsageResponse Result { get; set; }
            
            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }
            
            public DefibrillatorServerResponse()
            {
                Status = "";
                Message = "";
                SessionId = "";
                Result = new DefibrillatorUsageResponse();
                Timestamp = "";
            }
        }
        
        /// <summary>
        /// FIKA пакет для запроса использования дефибриллятора
        /// </summary>
        public class DefibrillatorRequestPacket : DesmatchFikaPackets.DesmatchNetworkEvent
        {
            public DefibrillatorUsageRequest Request { get; set; }
            
            public DefibrillatorRequestPacket()
            {
                Request = new DefibrillatorUsageRequest();
            }
            
            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.Put(JsonConvert.SerializeObject(Request));
            }
            
            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                string json = reader.GetString();
                Request = JsonConvert.DeserializeObject<DefibrillatorUsageRequest>(json);
            }
        }
        
        /// <summary>
        /// FIKA пакет для ответа на использование дефибриллятора
        /// </summary>
        public class DefibrillatorResponsePacket : DesmatchFikaPackets.DesmatchNetworkEvent
        {
            public DefibrillatorUsageResponse Response { get; set; }
            
            public DefibrillatorResponsePacket()
            {
                Response = new DefibrillatorUsageResponse();
            }
            
            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.Put(JsonConvert.SerializeObject(Response));
            }
            
            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                string json = reader.GetString();
                Response = JsonConvert.DeserializeObject<DefibrillatorUsageResponse>(json);
            }
        }
        
        // Основной класс для работы с дефибриллятором
        public class DefibrillatorManager
        {
            private DesmatchMode4Plugin mainPlugin;
            // Статические классы, не нужны переменные
            private float lastF5Check = 0f;
            private const float F5_CHECK_INTERVAL = 0.1f; // Проверяем F5 каждые 100мс
            
            public DefibrillatorManager(DesmatchMode4Plugin plugin)
            {
                mainPlugin = plugin;
            }
            
            /// <summary>
            /// Инициализация менеджера дефибриллятора
            /// </summary>
            public void Initialize()
            {
                try
                {
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Инициализация менеджера дефибриллятора");
                    
                    // Регистрация FIKA пакетов
                    RegisterFikaPackets();
                    
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Менеджер дефибриллятора инициализирован успешно");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка инициализации: {ex.Message}");
                }
            }
            
            /// <summary>
            /// Регистрация FIKA пакетов
            /// </summary>
            private void RegisterFikaPackets()
            {
                try
                {
                    // Временно отключаем регистрацию пакетов - будет добавлено позже
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] FIKA пакеты будут зарегистрированы позже");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка регистрации FIKA пакетов: {ex.Message}");
                }
            }
            
            /// <summary>
            /// Обработка запроса использования дефибриллятора
            /// </summary>
            private void OnDefibrillatorRequest(DefibrillatorRequestPacket packet)
            {
                try
                {
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Получен запрос использования дефибриллятора от {packet.PlayerNickname}");
                    
                    // Здесь будет логика обработки запроса на сервере
                    // Пока просто логируем
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка обработки запроса: {ex.Message}");
                }
            }
            
            /// <summary>
            /// Обработка ответа на использование дефибриллятора
            /// </summary>
            private void OnDefibrillatorResponse(DefibrillatorResponsePacket packet)
            {
                try
                {
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Получен ответ на использование дефибриллятора");
                    
                    if (packet.Response.Success)
                    {
                        ShowNotification($"{DefibrillatorConstants.EMOJI_DEFIBRILLATOR} {packet.Response.Message}", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                        
                        // Обработка обновлений слотов
                        foreach (var slotUpdate in packet.Response.UpdatedSlots)
                        {
                            UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Слот {slotUpdate.SlotIndex}: {slotUpdate.Action} - {slotUpdate.Message}");
                        }
                    }
                    else
                    {
                        ShowNotification($"{DesmatchConstants.EMOJI_ERROR} {packet.Response.Message}", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка обработки ответа: {ex.Message}");
                }
            }
            
            /// <summary>
            /// Показать уведомление игроку
            /// </summary>
            private void ShowNotification(string message, int durationSeconds)
            {
                try
                {
                    // Используем систему уведомлений из основного мода
                    DesmatchMode4Plugin.ShowNotification(message, false);
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Уведомление показано: {message}");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка показа уведомления: {ex.Message}");
                    // Fallback: простое логирование
                    UnityEngine.Debug.Log($"[DEFIBRILLATOR] {message}");
                }
            }
            
            /// <summary>
            /// Показать уведомление об ошибке
            /// </summary>
            private void ShowErrorNotification(string message, int durationSeconds)
            {
                try
                {
                    // Используем систему уведомлений из основного мода для ошибок
                    DesmatchMode4Plugin.ShowNotification(message, true);
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Уведомление об ошибке показано: {message}");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка показа уведомления об ошибке: {ex.Message}");
                    // Fallback: простое логирование
                    UnityEngine.Debug.LogError($"[DEFIBRILLATOR] {message}");
                }
            }
            
            /// <summary>
            /// Проверка, находится ли игрок в рейде
            /// </summary>
            public bool IsInRaid()
            {
                try
                {
                    var inRaid = PlayerState.IsInRaid;
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Проверка рейда: {inRaid}");
                    return inRaid;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка проверки рейда: {ex.Message}");
                    return false;
                }
            }
            
            /// <summary>
            /// Валидация данных запроса
            /// </summary>
            private bool ValidateDefibrillatorRequest(DefibrillatorUsageRequest request)
            {
                try
                {
                    if (request == null)
                    {
                        UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Запрос пустой");
                        return false;
                    }
                    
                    if (string.IsNullOrEmpty(request.PlayerId))
                    {
                        UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] PlayerId пустой");
                        return false;
                    }
                    
                    if (string.IsNullOrEmpty(request.DefibrillatorId))
                    {
                        UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] DefibrillatorId пустой");
                        return false;
                    }
                    
                    if (request.BatterySlots == null || request.BatterySlots.Count == 0)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Нет батареек в запросе");
                        return false;
                    }
                    
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Валидация запроса прошла успешно");
                    return true;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка валидации: {ex.Message}");
                    return false;
                }
            }
            
            /// <summary>
            /// Получить локального игрока
            /// </summary>
            public EFT.Player GetLocalPlayer()
            {
                try
                {
                    var gameWorld = Comfort.Common.Singleton<GameWorld>.Instance;
                    if (gameWorld != null)
                    {
                        return gameWorld.MainPlayer as LocalPlayer;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"❌ [PLAYER] Ошибка получения игрока: {ex.Message}");
                }
                return null;
            }
            
            /// <summary>
            /// Обновление менеджера дефибриллятора (вызывается из Update)
            /// </summary>
            public void Update()
            {
                try
                {
                    // Проверяем F5 с интервалом (вне зависимости от рейда)
                    if (Time.time - lastF5Check > F5_CHECK_INTERVAL)
                    {
                        lastF5Check = Time.time;
                        
                        if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
                        {
                            UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Нажата клавиша F5");
                            
                            if (IsInRaid())
                            {
                                UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] F5 нажата в рейде - обрабатываем");
                                HandleDefibrillatorUsage();
                            }
                            else
                            {
                                UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] F5 нажата вне рейда - показываем уведомление");
                                ShowNotification($"{DesmatchConstants.EMOJI_WARNING} Дефибриллятор можно использовать только в рейде!", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка в Update: {ex.Message}");
                }
            }
            
            /// <summary>
            /// Обработка использования дефибриллятора
            /// </summary>
            public void HandleDefibrillatorUsage()
            {
                try
                {
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] ===== НАЧИНАЕМ ОБРАБОТКУ ИСПОЛЬЗОВАНИЯ ДЕФИБРИЛЛЯТОРА =====");
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Метод HandleDefibrillatorUsage вызван");
                    
                    // Дополнительное логирование через BepInEx
                    BepInEx.Logging.Logger.CreateLogSource("DesmatchDefibrillator").LogInfo("[DEFIBRILLATOR] ===== НАЧИНАЕМ ОБРАБОТКУ ИСПОЛЬЗОВАНИЯ ДЕФИБРИЛЛЯТОРА =====");
                    BepInEx.Logging.Logger.CreateLogSource("DesmatchDefibrillator").LogInfo("[DEFIBRILLATOR] Метод HandleDefibrillatorUsage вызван");
                    
                    // Проверяем наличие дефибриллятора в спецслоте
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Поиск дефибриллятора в спецслоте...");
                    BepInEx.Logging.Logger.CreateLogSource("DesmatchDefibrillator").LogInfo("[DEFIBRILLATOR] Поиск дефибриллятора в спецслоте...");
                    var defibrillator = FindDefibrillatorInSpecialSlot();
                    if (defibrillator == null)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Дефибриллятор не найден в спецслоте");
                        ShowErrorNotification($"{DesmatchConstants.EMOJI_ERROR} {DefibrillatorConstants.MSG_DEFIBRILLATOR_NOT_FOUND}", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                        return;
                    }
                    
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] {DefibrillatorConstants.MSG_DEFIBRILLATOR_FOUND}");
                    ShowNotification($"{DefibrillatorConstants.EMOJI_DEFIBRILLATOR} {DefibrillatorConstants.MSG_DEFIBRILLATOR_FOUND}", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                    
                    // Проверяем батарейки в слотах дефибриллятора
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Анализ батареек в слотах дефибриллятора...");
                    var batterySlots = CheckDefibrillatorBatterySlots(defibrillator);
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Найдено батареек: {batterySlots.Count}");
                    
                    if (batterySlots.Count == 0)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] В дефибрилляторе нет батареек");
                        ShowErrorNotification($"{DesmatchConstants.EMOJI_WARNING} {DefibrillatorConstants.MSG_NO_BATTERIES}", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                        return;
                    }
                    
                    // Отправляем запрос на сервер
                    SendDefibrillatorUsageRequest(defibrillator, batterySlots);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка обработки использования: {ex.Message}");
                        ShowErrorNotification($"{DesmatchConstants.EMOJI_ERROR} {DefibrillatorConstants.MSG_DEFIBRILLATOR_ERROR}", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                }
            }
            
            /// <summary>
            /// ДИАГНОСТИЧЕСКИЙ СКАНЕР ИНВЕНТАРЯ
            /// </summary>
            public void ScanFullInventory()
            {
                try
                {
                   UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] ===== НАЧИНАЕМ ПОЛНОЕ СКАНИРОВАНИЕ ИНВЕНТАРЯ =====");
                   UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Ищем дефибриллятор с ID: {DefibrillatorConstants.DEFIBRILLATOR_ID}");
                   
                   var localPlayer = GetLocalPlayer();
                    if (localPlayer == null)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Локальный игрок не найден");
                        return;
                    }

                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Игрок найден: {localPlayer.Profile?.Nickname}");

                    // Получаем инвентарь игрока
                    var inventory = localPlayer.Inventory;
                    if (inventory == null)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Инвентарь игрока не найден");
                        return;
                    }

                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Инвентарь найден");

                    // Получаем экипировку
                    var equipment = inventory.Equipment;
                    if (equipment == null)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Экипировка игрока не найдена");
                        return;
                    }

                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Экипировка найдена");

                   // Сканируем все EquipmentSlot с детальным логированием
                   UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] === СКАНИРОВАНИЕ ВСЕХ СЛОТОВ ЭКИПИРОВКИ ===");
                   
                   var equipmentSlots = Enum.GetValues(typeof(EFT.InventoryLogic.EquipmentSlot));
                   UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Всего слотов для сканирования: {equipmentSlots.Length}");
                   
                   foreach (EFT.InventoryLogic.EquipmentSlot slotType in equipmentSlots)
                   {
                       try
                       {
                           var slot = equipment.GetSlot(slotType);
                           if (slot != null)
                           {
                               var item = slot.ContainedItem;
                               if (item != null)
                               {
                                   // Детальная информация о предмете
                                   var itemId = item.Id;
                                   var templateId = item.TemplateId;
                                   var itemName = item.Name;
                                   var itemType = item.GetType().Name;
                                   
                                   UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Слот {slotType}: {templateId} - {itemName} (ID: {itemId}, Type: {itemType})");
                                   
                                   // Проверяем, не дефибриллятор ли это
                                   if (templateId == DefibrillatorConstants.DEFIBRILLATOR_ID)
                                   {
                                       UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] *** НАЙДЕН ДЕФИБРИЛЛЯТОР В СЛОТЕ {slotType}! ***");
                                   }
                                   
                                   // Дополнительная информация о слоте
                                   try
                                   {
                                       var slotName = slot.Name;
                                       var slotTypeInfo = slot.GetType().Name;
                                       UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Детали слота {slotType}: Name='{slotName}', Type={slotTypeInfo}");
                                   }
                                   catch (Exception ex)
                                   {
                                       UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Не удалось получить детали слота {slotType}: {ex.Message}");
                                   }
                               }
                               else
                               {
                                   UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Слот {slotType}: ПУСТОЙ");
                               }
                           }
                           else
                           {
                               UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Слот {slotType}: НЕ НАЙДЕН");
                           }
                       }
                       catch (Exception ex)
                       {
                           UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Ошибка сканирования слота {slotType}: {ex.Message}");
                       }
                   }

                    // Сканируем все предметы в инвентаре (упрощенная версия)
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] === СКАНИРОВАНИЕ ВСЕХ ПРЕДМЕТОВ В ИНВЕНТАРЕ ===");
                    
                    try
                    {
                        // Пока пропускаем GetAllItems - будет добавлено позже
                        UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] GetAllItems временно отключен");
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Ошибка получения всех предметов: {ex.Message}");
                    }

                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] ===== СКАНИРОВАНИЕ ЗАВЕРШЕНО =====");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [INVENTORY_SCAN] Критическая ошибка сканирования: {ex.Message}");
                }
            }

            /// <summary>
            /// Поиск дефибриллятора в спецслоте (ОБНОВЛЕННАЯ ВЕРСИЯ)
            /// </summary>
            private object FindDefibrillatorInSpecialSlot()
            {
                try
                {
                UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Начинаем поиск дефибриллятора в спецслоте");
                var logSrc = BepInEx.Logging.Logger.CreateLogSource("DesmatchDefibSearch");
                logSrc.LogInfo("[SEARCH] Старт детального поиска дефибриллятора (включая специальные слоты)");
                logSrc.LogInfo($"[SEARCH] Ищем дефибриллятор с ID: {DefibrillatorConstants.DEFIBRILLATOR_ID}");
                    
                    var localPlayer = GetLocalPlayer();
                    if (localPlayer == null)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Локальный игрок не найден");
                        return null;
                    }

                    // Получаем инвентарь игрока
                    var inventory = localPlayer.Inventory;
                    if (inventory == null)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Инвентарь игрока не найден");
                        return null;
                    }

                    // Получаем экипировку
                    var equipment = inventory.Equipment;
                    if (equipment == null)
                    {
                        UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Экипировка игрока не найдена");
                        return null;
                    }

                // ПРИОРИТЕТ 1: Поиск в спецслотах (SpecialSlots)
                logSrc.LogInfo("[SEARCH] ПРИОРИТЕТ 1: Поиск в спецслотах (SpecialSlots)");
                try
                {
                    var specialSlotsField = equipment.GetType().GetField("SpecialSlots", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (specialSlotsField != null)
                    {
                        var specialSlots = specialSlotsField.GetValue(equipment);
                        if (specialSlots != null)
                        {
                            logSrc.LogInfo($"[SEARCH] SpecialSlots найден, тип: {specialSlots.GetType().Name}");
                            
                            // Получаем Items из SpecialSlots
                            var itemsProperty = specialSlots.GetType().GetProperty("Items", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (itemsProperty != null)
                            {
                                var items = itemsProperty.GetValue(specialSlots) as System.Collections.IEnumerable;
                                if (items != null)
                                {
                                    logSrc.LogInfo("[SEARCH] SpecialSlots.Items найден, начинаем обход");
                                    
                                    foreach (var item in items)
                                    {
                                        if (item != null)
                                        {
                                            var itemType = item.GetType();
                                            var tplProp = itemType.GetProperty("TemplateId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            var nameProp = itemType.GetProperty("Name", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            var idProp = itemType.GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            
                                            var tpl = tplProp?.GetValue(item) as string;
                                            var nm = nameProp?.GetValue(item) as string;
                                            var iid = idProp?.GetValue(item) as string;
                                            
                                            logSrc.LogInfo($"[SEARCH] SpecialSlot: {tpl} - {nm} (ID:{iid})");
                                            
                                            if (tpl == DefibrillatorConstants.DEFIBRILLATOR_ID)
                                            {
                                                logSrc.LogInfo("[SEARCH] *** НАЙДЕН ДЕФИБРИЛЛЯТОР В СПЕЦСЛОТЕ! ***");
                                                return item;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        logSrc.LogWarning("[SEARCH] SpecialSlots поле не найдено");
                    }
                }
                catch (System.Exception ex)
                {
                    logSrc.LogWarning($"[SEARCH] Ошибка поиска в SpecialSlots: {ex.Message}");
                }

                // 2) Перебор всех EquipmentSlot с детальным логированием
                var equipmentSlots = Enum.GetValues(typeof(EFT.InventoryLogic.EquipmentSlot));
                logSrc.LogInfo($"[SEARCH] Начинаем детальное сканирование {equipmentSlots.Length} слотов экипировки");
                
                foreach (EFT.InventoryLogic.EquipmentSlot slotType in equipmentSlots)
                {
                    try
                    {
                        var slot = equipment.GetSlot(slotType);
                        if (slot == null)
                        {
                            logSrc.LogInfo($"[SEARCH] {slotType}: слот отсутствует");
                            continue;
                        }
                        
                        var item = slot.ContainedItem;
                        if (item == null)
                        {
                            logSrc.LogInfo($"[SEARCH] {slotType}: ПУСТОЙ");
                        }
                        else
                        {
                            // Детальная информация о предмете
                            var itemId = item.Id;
                            var templateId = item.TemplateId;
                            var itemName = item.Name;
                            var itemType = item.GetType().Name;
                            
                            logSrc.LogInfo($"[SEARCH] {slotType}: {templateId} - {itemName} (ID:{itemId}, Type:{itemType})");
                            
                            // Проверяем, не дефибриллятор ли это
                            if (templateId == DefibrillatorConstants.DEFIBRILLATOR_ID)
                            {
                                logSrc.LogInfo($"[SEARCH] *** НАЙДЕН ДЕФИБРИЛЛЯТОР В СЛОТЕ {slotType}! ***");
                                return item;
                            }
                            
                            // Рекурсивный обход дочерних слотов этого предмета
                            var found = FindItemInChildrenByTpl(item, DefibrillatorConstants.DEFIBRILLATOR_ID, logSrc, $"{slotType}");
                            if (found != null) return found;
                        }
                    }
                    catch (Exception ex)
                    {
                        logSrc.LogWarning($"[SEARCH] Ошибка проверки {slotType}: {ex.Message}");
                    }
                }

                // 2) Попытка через внутренние коллекции Equipment (рефлексия _slots/_slotDict)
                try
                {
                    var slotsField = equipment.GetType().GetField("_slots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var slotsObj = slotsField?.GetValue(equipment) as System.Collections.IEnumerable;
                    if (slotsObj != null)
                    {
                        foreach (var s in slotsObj)
                        {
                            try
                            {
                                var slotNameProp = s.GetType().GetProperty("Name");
                                var slotName = slotNameProp?.GetValue(s)?.ToString() ?? "<unknown>";
                                var containedProp = s.GetType().GetProperty("ContainedItem");
                                var sItem = containedProp?.GetValue(s);
                                if (sItem != null)
                                {
                                    var tplProp = sItem.GetType().GetProperty("TemplateId");
                                    var nameProp = sItem.GetType().GetProperty("Name");
                                    var idProp = sItem.GetType().GetProperty("Id");
                                    var tpl = tplProp?.GetValue(sItem)?.ToString();
                                    var nm = nameProp?.GetValue(sItem)?.ToString();
                                    var iid = idProp?.GetValue(sItem)?.ToString();
                                    logSrc.LogInfo($"[SEARCH-R] {slotName}: {tpl} - {nm} (ID:{iid})");
                                    if (tpl == DefibrillatorConstants.DEFIBRILLATOR_ID)
                                    {
                                        logSrc.LogInfo($"[SEARCH-R] НАЙДЕН дефибриллятор в {slotName}");
                                        return sItem;
                                    }
                                    var foundChild = FindItemInChildrenByTpl(sItem, DefibrillatorConstants.DEFIBRILLATOR_ID, logSrc, slotName);
                                    if (foundChild != null) return foundChild;
                                }
                                else
                                {
                                    logSrc.LogInfo($"[SEARCH-R] {slotName}: пусто");
                                }
                            }
                            catch (Exception ex)
                            {
                                logSrc.LogWarning($"[SEARCH-R] Ошибка обхода внутреннего слота: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logSrc.LogWarning($"[SEARCH-R] Ошибка доступа к _slots: {ex.Message}");
                }

                // 3) РЕКУРСИВНЫЙ ПОИСК В КОНТЕЙНЕРАХ (рюкзак, карманы, контейнер)
                logSrc.LogInfo("[SEARCH-CONTAINERS] === НАЧИНАЕМ ПОИСК В КОНТЕЙНЕРАХ ===");
                
                // Поиск в рюкзаке
                var backpackSlot = equipment.GetSlot(EFT.InventoryLogic.EquipmentSlot.Backpack);
                if (backpackSlot?.ContainedItem != null)
                {
                    logSrc.LogInfo("[SEARCH-CONTAINERS] Поиск в рюкзаке...");
                    var found = FindItemInChildrenByTpl(backpackSlot.ContainedItem, DefibrillatorConstants.DEFIBRILLATOR_ID, logSrc, "Backpack");
                    if (found != null) return found;
                }
                
                // Поиск в карманах
                var pocketsSlot = equipment.GetSlot(EFT.InventoryLogic.EquipmentSlot.Pockets);
                if (pocketsSlot?.ContainedItem != null)
                {
                    logSrc.LogInfo("[SEARCH-CONTAINERS] Поиск в карманах...");
                    var found = FindItemInChildrenByTpl(pocketsSlot.ContainedItem, DefibrillatorConstants.DEFIBRILLATOR_ID, logSrc, "Pockets");
                    if (found != null) return found;
                }
                
                // Поиск в контейнере
                var containerSlot = equipment.GetSlot(EFT.InventoryLogic.EquipmentSlot.SecuredContainer);
                if (containerSlot?.ContainedItem != null)
                {
                    logSrc.LogInfo("[SEARCH-CONTAINERS] Поиск в контейнере...");
                    var found = FindItemInChildrenByTpl(containerSlot.ContainedItem, DefibrillatorConstants.DEFIBRILLATOR_ID, logSrc, "SecuredContainer");
                    if (found != null) return found;
                }
                
                // Поиск в разгрузке
                var vestSlot = equipment.GetSlot(EFT.InventoryLogic.EquipmentSlot.TacticalVest);
                if (vestSlot?.ContainedItem != null)
                {
                    logSrc.LogInfo("[SEARCH-CONTAINERS] Поиск в разгрузке...");
                    var found = FindItemInChildrenByTpl(vestSlot.ContainedItem, DefibrillatorConstants.DEFIBRILLATOR_ID, logSrc, "TacticalVest");
                    if (found != null) return found;
                }

                logSrc.LogInfo("[SEARCH] Полный обход предметов в Equipment и контейнерах завершен, дефибриллятор не найден");
                UnityEngine.Debug.LogWarning($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Дефибриллятор не найден ни в одном слоте или контейнере");
                return null;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка поиска дефибриллятора: {ex.Message}");
                    return null;
                }
            }

        // Рекурсивный обход дочерних слотов предмета через Containers/Slots/ContainedItem
        private object FindItemInChildrenByTpl(object rootItem, string tplToFind, BepInEx.Logging.ManualLogSource log, string path)
        {
            try
            {
                if (rootItem == null) return null;
                var t = rootItem.GetType();
                
                // 1) Попробуем через свойство Grids (для контейнеров)
                var gridsProp = t.GetProperty("Grids", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (gridsProp != null)
                {
                    var gridsVal = gridsProp.GetValue(rootItem) as System.Collections.IEnumerable;
                    if (gridsVal != null)
                    {
                        log.LogInfo($"[SEARCH-C] {path}: найден Grids, начинаем обход сеток");
                        
                        foreach (var grid in gridsVal)
                        {
                            try
                            {
                                // Получаем Items из сетки
                                var gridType = grid.GetType();
                                var itemsProp = gridType.GetProperty("Items", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                
                                if (itemsProp != null)
                                {
                                    var itemsVal = itemsProp.GetValue(grid) as System.Collections.IEnumerable;
                                    if (itemsVal != null)
                                    {
                                        log.LogInfo($"[SEARCH-C] {path}: найден Items в сетке, обходим предметы");
                                        foreach (var item in itemsVal)
                                        {
                                            if (item != null)
                                            {
                                                var itemType = item.GetType();
                                                var tplProp = itemType.GetProperty("TemplateId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                if (tplProp != null)
                                                {
                                                    var tpl = tplProp.GetValue(item) as string;
                                                    if (tpl == tplToFind)
                                                    {
                                                        log.LogInfo($"[SEARCH-C] {path}: НАЙДЕН ДЕФИБРИЛЛЯТОР в сетке! Tpl: {tpl}");
                                                        return item;
                                                    }
                                                }
                                                
                                                // Рекурсивный поиск в дочерних предметах
                                                var result = FindItemInChildrenByTpl(item, tplToFind, log, $"{path}->Grid");
                                                if (result != null)
                                                {
                                                    return result;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {
                                log.LogInfo($"[SEARCH-C] {path}: ошибка при обходе сетки: {ex.Message}");
                            }
                        }
                    }
                }
                
                // 2) Попробуем через свойство Containers (для слотов)
                var containersProp = t.GetProperty("Containers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (containersProp != null)
                {
                    var containersVal = containersProp.GetValue(rootItem);
                    if (containersVal != null)
                    {
                        log.LogInfo($"[SEARCH-C] {path}: найден Containers, тип: {containersVal.GetType().Name}");
                        
                        // ДЕТАЛЬНОЕ ЛОГИРОВАНИЕ СТРУКТУРЫ Class2229
                        var containerType = containersVal.GetType();
                        log.LogInfo($"[SEARCH-C] {path}: Детальный анализ типа {containerType.Name}");
                        
                        // Логируем все поля и свойства
                        var fields = containerType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        foreach (var field in fields)
                        {
                            try
                            {
                                var fieldValue = field.GetValue(containersVal);
                                log.LogInfo($"[SEARCH-C] {path}: Поле {field.Name} ({field.FieldType.Name}) = {fieldValue?.GetType().Name ?? "null"}");
                            }
                            catch (System.Exception ex)
                            {
                                log.LogWarning($"[SEARCH-C] {path}: Ошибка чтения поля {field.Name}: {ex.Message}");
                            }
                        }
                        
                        var properties = containerType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        foreach (var prop in properties)
                        {
                            try
                            {
                                var propValue = prop.GetValue(containersVal);
                                log.LogInfo($"[SEARCH-C] {path}: Свойство {prop.Name} ({prop.PropertyType.Name}) = {propValue?.GetType().Name ?? "null"}");
                            }
                            catch (System.Exception ex)
                            {
                                log.LogWarning($"[SEARCH-C] {path}: Ошибка чтения свойства {prop.Name}: {ex.Message}");
                            }
                        }
                        
                        // Попробуем разные способы доступа к содержимому
                        try
                        {
                            // Способ 1: Прямой доступ к Grid
                            var gridProp = containersVal.GetType().GetProperty("Grid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (gridProp != null)
                            {
                                var grid = gridProp.GetValue(containersVal);
                                log.LogInfo($"[SEARCH-C] {path}: найден Grid, тип: {grid?.GetType().Name}");
                                
                                // Получаем слоты из Grid
                                var gridSlotsProp = grid.GetType().GetProperty("Slots", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (gridSlotsProp != null)
                                {
                                    var gridSlots = gridSlotsProp.GetValue(grid) as System.Collections.IEnumerable;
                                    if (gridSlots != null)
                                    {
                                        log.LogInfo($"[SEARCH-C] {path}: найден Grid.Slots, начинаем обход");
                                        
                                        foreach (var slot in gridSlots)
                                        {
                                            var containedProp = slot.GetType().GetProperty("ContainedItem");
                                            var childItem = containedProp?.GetValue(slot);
                                            
                                            if (childItem != null)
                                            {
                                                var tplProp = childItem.GetType().GetProperty("TemplateId");
                                                var nameProp = childItem.GetType().GetProperty("Name");
                                                var idProp = childItem.GetType().GetProperty("Id");
                                                
                                                var tpl = tplProp?.GetValue(childItem)?.ToString();
                                                var nm = nameProp?.GetValue(childItem)?.ToString();
                                                var iid = idProp?.GetValue(childItem)?.ToString();
                                                var itemType = childItem.GetType().Name;
                                                
                                                log.LogInfo($"[SEARCH-C] {path}/Grid: {tpl} - {nm} (ID:{iid}, Type:{itemType})");
                                                
                                                // Проверяем, не дефибриллятор ли это
                                                if (tpl == tplToFind)
                                                {
                                                    log.LogInfo($"[SEARCH-C] *** НАЙДЕН ДЕФИБРИЛЛЯТОР В {path}/Grid! ***");
                                                    return childItem;
                                                }
                                                
                                                // Рекурсивный поиск глубже
                                                var deeper = FindItemInChildrenByTpl(childItem, tplToFind, log, $"{path}/Grid");
                                                if (deeper != null) return deeper;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.LogWarning($"[SEARCH-C] Ошибка доступа к Grid {path}: {ex.Message}");
                        }
                        
                        // Способ 2: Прямой доступ к Slots
                        try
                        {
                            var directSlotsProp = containersVal.GetType().GetProperty("Slots", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (directSlotsProp != null)
                            {
                                var directSlots = directSlotsProp.GetValue(containersVal) as System.Collections.IEnumerable;
                                if (directSlots != null)
                                {
                                    log.LogInfo($"[SEARCH-C] {path}: найден прямые Slots, начинаем обход");
                                    
                                    foreach (var slot in directSlots)
                                    {
                                        var containedProp = slot.GetType().GetProperty("ContainedItem");
                                        var childItem = containedProp?.GetValue(slot);
                                        
                                        if (childItem != null)
                                        {
                                            var tplProp = childItem.GetType().GetProperty("TemplateId");
                                            var nameProp = childItem.GetType().GetProperty("Name");
                                            var idProp = childItem.GetType().GetProperty("Id");
                                            
                                            var tpl = tplProp?.GetValue(childItem)?.ToString();
                                            var nm = nameProp?.GetValue(childItem)?.ToString();
                                            var iid = idProp?.GetValue(childItem)?.ToString();
                                            var itemType = childItem.GetType().Name;
                                            
                                            log.LogInfo($"[SEARCH-C] {path}/Direct: {tpl} - {nm} (ID:{iid}, Type:{itemType})");
                                            
                                            // Проверяем, не дефибриллятор ли это
                                            if (tpl == tplToFind)
                                            {
                                                log.LogInfo($"[SEARCH-C] *** НАЙДЕН ДЕФИБРИЛЛЯТОР В {path}/Direct! ***");
                                                return childItem;
                                            }
                                            
                                            // Рекурсивный поиск глубже
                                            var deeper = FindItemInChildrenByTpl(childItem, tplToFind, log, $"{path}/Direct");
                                            if (deeper != null) return deeper;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.LogWarning($"[SEARCH-C] Ошибка доступа к прямым Slots {path}: {ex.Message}");
                        }
                    }
                }
                
                // 2) Попробуем через свойство Slots (для обычных предметов)
                var slotsProp = t.GetProperty("Slots", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var slotsVal = slotsProp?.GetValue(rootItem) as System.Collections.IEnumerable;
                if (slotsVal != null) 
                {
                    log.LogInfo($"[SEARCH-C] {path}: найден Slots, начинаем обход слотов");
                    
                    foreach (var s in slotsVal)
                    {
                        try
                        {
                            var slotNameProp = s.GetType().GetProperty("Name");
                            var slotName = slotNameProp?.GetValue(s)?.ToString() ?? "<child>";
                            var containedProp = s.GetType().GetProperty("ContainedItem");
                            var childItem = containedProp?.GetValue(s);
                            
                            if (childItem == null)
                            {
                                log.LogInfo($"[SEARCH-C] {path}/{slotName}: ПУСТОЙ");
                                continue;
                            }
                            
                            // Детальная информация о дочернем предмете
                            var tplProp = childItem.GetType().GetProperty("TemplateId");
                            var nameProp = childItem.GetType().GetProperty("Name");
                            var idProp = childItem.GetType().GetProperty("Id");
                            
                            var tpl = tplProp?.GetValue(childItem)?.ToString();
                            var nm = nameProp?.GetValue(childItem)?.ToString();
                            var iid = idProp?.GetValue(childItem)?.ToString();
                            var itemType = childItem.GetType().Name;
                            
                            log.LogInfo($"[SEARCH-C] {path}/{slotName}: {tpl} - {nm} (ID:{iid}, Type:{itemType})");
                            
                            // Проверяем, не дефибриллятор ли это
                            if (tpl == tplToFind)
                            {
                                log.LogInfo($"[SEARCH-C] *** НАЙДЕН ДЕФИБРИЛЛЯТОР В {path}/{slotName}! ***");
                                return childItem;
                            }
                            
                            // Рекурсивный поиск глубже
                            var deeper = FindItemInChildrenByTpl(childItem, tplToFind, log, $"{path}/{slotName}");
                            if (deeper != null) return deeper;
                        }
                        catch (Exception ex)
                        {
                            log.LogWarning($"[SEARCH-C] Ошибка обхода дочернего слота {path}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    log.LogInfo($"[SEARCH-C] {path}: нет дочерних слотов (Slots=null, Containers=null)");
                }
                
                log.LogInfo($"[SEARCH-C] {path}: обход дочерних слотов завершен");
                return null;
            }
            catch (Exception ex)
            {
                log.LogError($"[SEARCH-C] Критическая ошибка в {path}: {ex.Message}");
                return null;
            }
        }
            
            /// <summary>
            /// Проверка батареек в слотах дефибриллятора
            /// </summary>
            private List<BatterySlotInfo> CheckDefibrillatorBatterySlots(object defibrillator)
            {
                var batterySlots = new List<BatterySlotInfo>();
                
                try
                {
                    // Используем рефлексию для получения слотов дефибриллятора
                    var defibrillatorType = defibrillator.GetType();
                    var slotsProperty = defibrillatorType.GetProperty("Slots");
                    if (slotsProperty != null)
                    {
                        var slots = slotsProperty.GetValue(defibrillator);
                        if (slots is System.Collections.IEnumerable slotList)
                        {
                            int slotIndex = 0;
                            foreach (var slot in slotList)
                            {
                                var slotType = slot.GetType();
                                var containedItemProperty = slotType.GetProperty("ContainedItem");
                                if (containedItemProperty != null)
                                {
                                    var containedItem = containedItemProperty.GetValue(slot);
                                    if (containedItem != null)
                                    {
                                        var batteryInfo = AnalyzeBatteryItem(containedItem, slotIndex);
                                        if (batteryInfo != null)
                                        {
                                            batterySlots.Add(batteryInfo);
                                            UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] {string.Format(DefibrillatorConstants.MSG_BATTERY_FOUND, slotIndex)}: {batteryInfo.BatteryName}");
                                        }
                                    }
                                }
                                slotIndex++;
                            }
                        }
                    }
                    
                    return batterySlots;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка проверки слотов батареек: {ex.Message}");
                    return batterySlots;
                }
            }
            
            /// <summary>
            /// Анализ батарейки в слоте
            /// </summary>
            private BatterySlotInfo AnalyzeBatteryItem(object batteryItem, int slotIndex)
            {
                try
                {
                    var batteryInfo = new BatterySlotInfo
                    {
                        SlotIndex = slotIndex
                    };
                    
                    // Получаем ID батарейки
                    var itemType = batteryItem.GetType();
                    var tplProperty = itemType.GetProperty("Tpl");
                    if (tplProperty != null)
                    {
                        batteryInfo.BatteryId = tplProperty.GetValue(batteryItem)?.ToString() ?? "";
                    }
                    
                    // Определяем тип батарейки
                    batteryInfo.IsOriginalBattery = (batteryInfo.BatteryId == DefibrillatorConstants.ORIGINAL_BATTERY_ID);
                    batteryInfo.IsCustomBattery = (batteryInfo.BatteryId == DefibrillatorConstants.CUSTOM_BATTERY_CHARGED_ID || 
                                                 batteryInfo.BatteryId == DefibrillatorConstants.CUSTOM_BATTERY_DISCHARGED_ID);
                    
                    // Получаем название батарейки
                    var nameProperty = itemType.GetProperty("Name");
                    if (nameProperty != null)
                    {
                        batteryInfo.BatteryName = nameProperty.GetValue(batteryItem)?.ToString() ?? "Неизвестная батарейка";
                    }
                    
                    // Получаем заряды для усиленных батареек
                    if (batteryInfo.IsCustomBattery)
                    {
                        var resourceProperty = itemType.GetProperty("Resource");
                        var maxResourceProperty = itemType.GetProperty("MaxResource");
                        
                        if (resourceProperty != null)
                        {
                            var resource = resourceProperty.GetValue(batteryItem);
                            if (resource != null && int.TryParse(resource.ToString(), out int currentCharge))
                            {
                                batteryInfo.CurrentCharge = currentCharge;
                            }
                        }
                        
                        if (maxResourceProperty != null)
                        {
                            var maxResource = maxResourceProperty.GetValue(batteryItem);
                            if (maxResource != null && int.TryParse(maxResource.ToString(), out int maxCharge))
                            {
                                batteryInfo.MaxCharge = maxCharge;
                            }
                        }
                    }
                    
                    return batteryInfo;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка анализа батарейки: {ex.Message}");
                    return null;
                }
            }
            
            /// <summary>
            /// Отправка запроса на использование дефибриллятора
            /// </summary>
            private void SendDefibrillatorUsageRequest(object defibrillator, List<BatterySlotInfo> batterySlots)
            {
                try
                {
                    var localPlayer = GetLocalPlayer();
                    if (localPlayer == null)
                    {
                        UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Локальный игрок недоступен");
                        return;
                    }
                    
                    // Создаем запрос
                    var request = new DefibrillatorUsageRequest
                    {
                        PlayerId = localPlayer.ProfileId,
                        PlayerNickname = localPlayer.Profile.Nickname,
                        DefibrillatorId = DefibrillatorConstants.DEFIBRILLATOR_ID,
                        BatterySlots = batterySlots
                    };
                    
                    // Валидируем запрос
                    if (!ValidateDefibrillatorRequest(request))
                    {
                        UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Валидация запроса не прошла");
                        ShowErrorNotification($"{DesmatchConstants.EMOJI_ERROR} Ошибка валидации запроса", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                        return;
                    }
                    
                    // Создаем пакет
                    var packet = new DefibrillatorRequestPacket
                    {
                        PlayerProfileId = localPlayer.ProfileId,
                        PlayerNickname = localPlayer.Profile.Nickname,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Request = request
                    };
                    
                    // Отправляем через HTTP запрос на сервер
                    var requestData = new
                    {
                        request = request,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    
                    var json = JsonConvert.SerializeObject(requestData);
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Отправляем HTTP запрос на сервер");
                    
                    var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/defibrillator-usage", json);
                    
                    if (!string.IsNullOrEmpty(response))
                    {
                        UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ответ сервера: {response}");
                        
                        // Парсим ответ сервера
                        try
                        {
                            var serverResponse = JsonConvert.DeserializeObject<DefibrillatorServerResponse>(response);
                            if (serverResponse?.Status == "success")
                            {
                                ShowNotification($"{DefibrillatorConstants.EMOJI_DEFIBRILLATOR} Дефибриллятор использован успешно!", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                                
                                // Обрабатываем обновления слотов
                                if (serverResponse.Result?.UpdatedSlots != null)
                                {
                                    foreach (var slotUpdate in serverResponse.Result.UpdatedSlots)
                                    {
                                        UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Слот {slotUpdate.SlotIndex}: {slotUpdate.Action} - {slotUpdate.Message}");
                                    }
                                }
                            }
                            else
                            {
                                ShowErrorNotification($"{DesmatchConstants.EMOJI_ERROR} Ошибка использования дефибриллятора: {serverResponse?.Message}", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                            }
                        }
                        catch (Exception parseEx)
                        {
                            UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка парсинга ответа: {parseEx.Message}");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Пустой ответ от сервера");
                        ShowErrorNotification($"{DesmatchConstants.EMOJI_ERROR} Ошибка связи с сервером", DefibrillatorConstants.NOTIFICATION_DURATION_SECONDS);
                    }
                    
                    UnityEngine.Debug.Log($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Запрос отправлен на сервер: {batterySlots.Count} батареек");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"{DesmatchConstants.LOG_PREFIX_CLIENT} [DEFIBRILLATOR] Ошибка отправки запроса: {ex.Message}");
                }
            }
        }
    }
}
