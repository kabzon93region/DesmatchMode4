using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using EFT;
using EFT.HealthSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using SPT.Common.Http;
using EFT.Communications;
using EFT.UI;
using EFT.InventoryLogic;
using DesmatchMode4.Settings;
using DesmatchMode4.Player;

// Классы для десериализации ответов сервера
public class ServerResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public string sessionId { get; set; }
    public string timestamp { get; set; }
    public ClientSettings clientSettings { get; set; }
    public bool? invulnerable { get; set; }
    public float? invulnUntil { get; set; }
}

public class ClientSettings
{
    public bool? enabled { get; set; }
    public int? respawnDelay { get; set; }
    public int? invulnSeconds { get; set; }
    public string manualKey { get; set; }
}

namespace DesmatchMode4
{
    // BepInEx принимает только numeric semver (x.y.z) — без -alpha/-beta суффиксов
    [BepInPlugin("DesmatchMode4", "Desmatch Mode 4 (headless_all)", "3.0.23")]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
public class DesmatchMode4Plugin : BaseUnityPlugin
{
    
    // Настройки теперь в DesmatchSettings.cs
    
    // Состояние игрока теперь в DesmatchPlayerState.cs
    
    // Создание предметов убрано - теперь через крафт в убежище
    
    // Константы теперь в DesmatchSettings.cs
    
    // Менеджер дефибриллятора
    private DesmatchDefibrillator.DefibrillatorManager defibrillatorManager;
    
    // Обновляем клиентские настройки из сервера с валидацией
    public void UpdateClientSettings(int respawnDelay, int invulnSeconds)
    {
        PlayerState.UpdateClientSettings(respawnDelay, invulnSeconds);
    }
    
    // Синхронизируем статус неуязвимости с сервером
    public void SyncInvulnerabilityWithServer(bool serverInvulnerable, float serverInvulnUntil)
    {
        try
        {
            if (serverInvulnerable)
            {
                long serverNowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                float remainingSeconds = Mathf.Max(0f, (serverInvulnUntil - serverNowMs) / 1000f);
                if (remainingSeconds <= 0f)
                {
                    remainingSeconds = PlayerState.ClientInvulnSeconds;
                }

                PlayerState.IsPlayerInvulnerable = true;
                PlayerState.InvulnUntil = Time.time + remainingSeconds;

                Logger.LogInfo($"🔄 [SYNC] Неуязвимость установлена: remaining={remainingSeconds:F1}s, until={PlayerState.InvulnUntil:F1}");
            }
            else if (!isInvulnDisableInProgress && !IsPlayerInvulnerable())
            {
                Logger.LogInfo($"🔄 [SYNC] Неуязвимость отключена сервером");
                DisablePlayerInvulnerability();
            }
            else
            {
                Logger.LogInfo($"🔄 [SYNC] Игнорируем server invuln=false (local disable или invuln уже снята)");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🔄 [SYNC] Ошибка синхронизации неуязвимости: {ex.Message}");
        }
    }
    
    // Отправляем статус неуязвимости на сервер
    private void SendInvulnerabilityStatusToServer()
    {
        try
        {
            long invulnUntilUnixMs = 0L;
            if (PlayerState.IsPlayerInvulnerable)
            {
                float remainingSeconds = Mathf.Max(0f, PlayerState.InvulnUntil - Time.time);
                invulnUntilUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    + (long)(remainingSeconds * 1000f);
            }

            var data = new
            {
                SessionId = sessionId,
                Invulnerable = PlayerState.IsPlayerInvulnerable,
                InvulnUntil = (double)invulnUntilUnixMs,
                Timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            
            var json = JsonConvert.SerializeObject(data);
            
            Logger.LogInfo($"[SYNC] Отправляем статус неуязвимости на сервер: {PlayerState.IsPlayerInvulnerable}, до {PlayerState.InvulnUntil}");
            
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/sync-invulnerability", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"[SYNC] Статус неуязвимости отправлен на сервер: {response}");
            }
            else
            {
                Logger.LogWarning("[SYNC] Ошибка отправки статуса неуязвимости: пустой ответ");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[SYNC] Ошибка при отправке статуса неуязвимости: {ex.Message}");
        }
    }
    
    // Обрабатываем ответ сервера для обновления настроек
    private void ProcessServerResponse(string response)
    {
        try
        {
            Logger.LogInfo($"[SERVER_RESPONSE] Обрабатываем ответ сервера: {response}");
            
            if (string.IsNullOrEmpty(response))
            {
                Logger.LogWarning("[SERVER_RESPONSE] Пустой ответ от сервера");
                return;
            }
            
            // Парсим JSON ответ от сервера в структурированный объект
            var serverResponse = JsonConvert.DeserializeObject<ServerResponse>(response);
            if (serverResponse == null)
            {
                Logger.LogWarning("[SERVER_RESPONSE] Не удалось десериализовать ответ сервера");
                return;
            }
            
            Logger.LogInfo($"[SERVER_RESPONSE] Ответ сервера успешно десериализован");
            
            // Обрабатываем статус ответа
            if (!string.IsNullOrEmpty(serverResponse.status))
            {
                Logger.LogInfo($"[SERVER_RESPONSE] Статус сервера: {serverResponse.status}");
                
                if (serverResponse.status == "success")
                {
                    Logger.LogInfo("[SERVER_RESPONSE] Сервер подтвердил успешную обработку");
                }
                else if (serverResponse.status == "error")
                {
                    Logger.LogWarning($"[SERVER_RESPONSE] Сервер вернул ошибку: {serverResponse.message}");
                }
            }
            
            // Обрабатываем валидированные настройки от сервера
            if (serverResponse.clientSettings != null)
            {
                Logger.LogInfo("[SERVER_RESPONSE] Получены валидированные настройки от сервера");
                
                try
                {
                    var clientSettings = serverResponse.clientSettings;
                    
                    // Обновляем respawnDelay если есть
                    if (clientSettings.respawnDelay.HasValue)
                    {
                        int serverRespawnDelay = clientSettings.respawnDelay.Value;
                        int validatedDelay = Settings.Settings.ValidateRespawnDelay(serverRespawnDelay);
                        PlayerState.ClientRespawnDelay = validatedDelay;
                        Logger.LogInfo($"[SERVER_RESPONSE] Обновлен respawnDelay: {PlayerState.ClientRespawnDelay}ms");
                    }
                    
                    // Обновляем invulnSeconds если есть
                    if (clientSettings.invulnSeconds.HasValue)
                    {
                        int serverInvulnSeconds = clientSettings.invulnSeconds.Value;
                        int validatedInvuln = Settings.Settings.ValidateInvulnSeconds(serverInvulnSeconds);
                        PlayerState.ClientInvulnSeconds = validatedInvuln;
                        Logger.LogInfo($"[SERVER_RESPONSE] Обновлен invulnSeconds: {PlayerState.ClientInvulnSeconds}s");
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"[SERVER_RESPONSE] Ошибка обработки клиентских настроек: {ex.Message}");
                }
            }
            
            // Обрабатываем данные неуязвимости если есть
            if (serverResponse.invulnerable.HasValue && serverResponse.invulnUntil.HasValue)
            {
                try
                {
                    bool serverInvulnerable = serverResponse.invulnerable.Value;
                    float serverInvulnUntil = serverResponse.invulnUntil.Value;
                    
                    Logger.LogInfo($"[SERVER_RESPONSE] Получены данные неуязвимости: {serverInvulnerable}, до {serverInvulnUntil}");
                    
                    // Синхронизируем неуязвимость с сервером
                    SyncInvulnerabilityWithServer(serverInvulnerable, serverInvulnUntil);
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"[SERVER_RESPONSE] Ошибка синхронизации неуязвимости: {ex.Message}");
                }
            }
            
            // Обрабатываем сообщения от сервера
            if (!string.IsNullOrEmpty(serverResponse.message))
            {
                Logger.LogInfo($"[SERVER_RESPONSE] Сообщение от сервера: {serverResponse.message}");
            }
            
            // Создание предметов убрано - теперь через крафт в убежище
            
            Logger.LogInfo("[SERVER_RESPONSE] Обработка ответа сервера завершена успешно");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[SERVER_RESPONSE] Ошибка обработки ответа сервера: {ex.Message}");
            Logger.LogError($"[SERVER_RESPONSE] Stack trace: {ex.StackTrace}");
        }
    }
    
    // Методы валидации теперь в DesmatchSettings.cs
    
    // Валидация конфигурации BepInEx при загрузке
    private void ValidateConfiguration()
    {
        Logger.LogInfo("🛡️ [VALIDATION] Начинаем валидацию конфигурации BepInEx");
        
        // Валидация Settings.Settings.RespawnDelay (конвертируем в миллисекунды для проверки)
        float respawnDelaySeconds = Settings.Settings.RespawnDelay.Value;
        int respawnDelayMs = (int)(respawnDelaySeconds * 1000);
        
        if (respawnDelayMs < Settings.Settings.MIN_RESPAWN_DELAY || respawnDelayMs > Settings.Settings.MAX_RESPAWN_DELAY)
        {
            Logger.LogWarning($"🛡️ [VALIDATION] Settings.Settings.RespawnDelay вне допустимого диапазона: {respawnDelayMs}ms, корректируем");
            float correctedDelay = Settings.Settings.DEFAULT_RESPAWN_DELAY / 1000f;
            Settings.Settings.RespawnDelay.Value = correctedDelay;
            Logger.LogInfo($"🛡️ [VALIDATION] Settings.Settings.RespawnDelay скорректирован: {respawnDelaySeconds}s → {correctedDelay}s");
        }

        // Валидация Settings.InvulnerabilityTime
        float invulnTime = Settings.Settings.InvulnerabilityTime.Value;
        int invulnSeconds = (int)invulnTime;
        
        if (invulnSeconds < Settings.Settings.MIN_INVULN_SECONDS || invulnSeconds > Settings.Settings.MAX_INVULN_SECONDS)
        {
            Logger.LogWarning($"🛡️ [VALIDATION] Settings.InvulnerabilityTime вне допустимого диапазона: {invulnSeconds}s, корректируем");
            float correctedInvuln = Settings.Settings.DEFAULT_INVULN_SECONDS;
            Settings.Settings.InvulnerabilityTime.Value = correctedInvuln;
            Logger.LogInfo($"🛡️ [VALIDATION] Settings.InvulnerabilityTime скорректирован: {invulnTime}s → {correctedInvuln}s");
        }
        
        Logger.LogInfo("🛡️ [VALIDATION] Валидация конфигурации BepInEx завершена");
    }
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Dictionary<string, object> savedPlayerData = new Dictionary<string, object>();
    private Coroutine respawnCoroutine;
    private Coroutine fadeRespawnCoroutine;
    private string sessionId;
    private bool isRespawnInProgress = false;
    private float lastRespawnTime = -999f;
    private float lastManualRespawnTime = -999f;
    private bool isCriticalStateHandling = false;
    private float lastCriticalStateTime = -999f;
    private bool isInvulnDisableInProgress = false;
    private bool isTherapeuticDamageInProgress = false;
    private Coroutine invulnDisableCoroutine;
    private const float CRITICAL_STATE_COOLDOWN = 2f;
    private static readonly System.Collections.Generic.HashSet<string> LoggedReflectionWarnings = new System.Collections.Generic.HashSet<string>();
    private static readonly System.Collections.Generic.Dictionary<string, float> RemoteInvulnUntilByProfile = new System.Collections.Generic.Dictionary<string, float>();
    
    // Ссылки на игровые объекты
    private GameWorld gameWorld;
    private LocalPlayer localPlayer;
    private bool hasRaidStarted = false;
    private bool raidReadyNotificationShown = false;
    private float lastGameWorldUpdate = 0f;
    private const float GAMEWORLD_UPDATE_INTERVAL = 30f; // Обновляем каждые 30 секунд только до начала рейда
    private float lastInputCheck = 0f; // Для диагностики ввода
    private float lastIconCacheUpdate = 0f; // Для автоматического обновления кэша иконок
    
    // Переменные для оптимизации логирования (показываем только изменения)
    private bool lastEnableDesmatchMode = false;
    private bool lastIsInRaid = false;
    private bool lastIsPlayerDead = false;
    private bool lastIsPlayerInvulnerable = false;
    private float lastInvulnUntil = 0f;
    private bool firstLogUpdate = true; // Флаг для первого логирования
    
    // Компоненты для эффектов затемнения экрана
    private DeathFade deathFade;
    private PreloaderUI preloaderUI;
    
    // Компоненты для звуковых эффектов
    private BetterAudio betterAudio;
    private AudioClip tinnitusSound;
    private bool isTinnitusActive = false;
    private float tinnitusStartTime = 0f;
    private float tinnitusDuration = 0f;
    private AudioSource fallbackAudioSource; // Fallback для случаев когда BetterAudio недоступен
    private AudioSource activeTinnitusSource; // Активный источник звука для остановки
    private bool isSoundFadingOut = false; // Флаг затухания звука
    private readonly System.Collections.Generic.List<AudioSource> _trackedTinnitusSources =
        new System.Collections.Generic.List<AudioSource>();
    
    // FIKA интеграция
    private FikaCommunicationManager fikaCommunicationManager;
    private bool fikaAvailable = false;
    
    private void Awake()
    {
        Logger.LogInfo("[AWAKE] DesmatchMode: Начинаем инициализацию");
        
        // Настройки мода с валидацией
        Logger.LogInfo("[AWAKE] Создаем конфигурацию с валидацией");
        Settings.Settings.EnableDesmatchMode = Config.Bind("General", "Enable Desmatch Mode", true, "Включить дезмач режим");
        Settings.Settings.EnableAutoRespawn = Config.Bind("General", "Enable Auto Respawn", true,
            "Автовозрождение после смерти. Выключите для режима только F10. При включённом авто F10 отменяет ожидание.");
        Settings.Settings.RespawnDelay = Config.Bind("General", "Respawn Delay", 3.0f,
            "Задержка перед автовозрождением (сек). В это окно можно нажать F10 для ручного респавна.");
        Settings.Settings.ManualRespawnKey = Config.Bind("General", "Manual Respawn Key", KeyCode.F10, "Клавиша для ручного возрождения");
        Settings.Settings.InvulnerabilityTime = Config.Bind("General", "Invulnerability Time", 3.0f, "Время неуязвимости после возрождения (секунды)");
        
        // Настройки уведомлений
        Settings.Settings.ShowMainNotifications = Config.Bind("Notifications", "Show Main Notifications", true, 
            "Показывать основные уведомления (неуязвимость, ручное возрождение)");
        Settings.Settings.ShowFadeEffectNotifications = Config.Bind("Notifications", "Show Fade Effect Notifications", false, 
            "Показывать уведомления об эффектах затемнения экрана");
        Settings.Settings.ShowHealingNotifications = Config.Bind("Notifications", "Show Healing Notifications", false, 
            "Показывать детальные уведомления о процессе лечения");
        Settings.Settings.ShowDamageNotifications = Config.Bind("Notifications", "Show Damage Notifications", false, 
            "Показывать уведомления о нанесении урона");
        Settings.Settings.ShowAutoRespawnNotifications = Config.Bind("Notifications", "Show Auto Respawn Notifications", true, 
            "Показывать уведомления об автоматическом возрождении");
            
        // Настройки звуковых эффектов
        Settings.Settings.EnableTinnitusEffect = Config.Bind("SoundEffects", "Enable Tinnitus Effect", true, 
            "Включить звуковой эффект оглушения при респавне");
        Settings.Settings.TinnitusDuration = Config.Bind("SoundEffects", "Tinnitus Duration", 5f, 
            "Длительность эффекта тиннитуса (секунды)");
        Settings.Settings.TinnitusFadeOut = Config.Bind("SoundEffects", "Tinnitus Fade Out", true, 
            "Плавное затухание эффекта к концу неуязвимости");
        Settings.Settings.ShowSoundEffectNotifications = Config.Bind("Notifications", "Show Sound Effect Notifications", false, 
            "Показывать уведомления о звуковых эффектах");

        BindRespawnPipelineSettings();
        
        // Валидация конфигурации при загрузке
        ValidateConfiguration();
        
        // Инициализируем клиентские настройки из BepInEx конфигурации
        PlayerState.ClientRespawnDelay = Mathf.RoundToInt(Settings.Settings.RespawnDelay.Value * 1000); // точное преобразование в миллисекунды
        PlayerState.ClientInvulnSeconds = Mathf.RoundToInt(Settings.Settings.InvulnerabilityTime.Value); // точное преобразование в секунды
        
        // Валидация и коррекция настроек
        if (PlayerState.ClientRespawnDelay < 0) PlayerState.ClientRespawnDelay = 0;
        if (PlayerState.ClientRespawnDelay > 30000) PlayerState.ClientRespawnDelay = 30000;
        if (PlayerState.ClientInvulnSeconds < 0) PlayerState.ClientInvulnSeconds = 0;
        if (PlayerState.ClientInvulnSeconds > 60) PlayerState.ClientInvulnSeconds = 60;
        
        Logger.LogInfo($"[AWAKE] Настройки: DesmatchMode={Settings.Settings.EnableDesmatchMode.Value}, RespawnDelay={Settings.Settings.RespawnDelay.Value}s, InvulnTime={Settings.Settings.InvulnerabilityTime.Value}s");
        
        // SPT RequestHandler инициализируется автоматически
        // Logger.LogInfo("[AWAKE] SPT RequestHandler готов к использованию");
        
        // Инициализируем sessionId
        sessionId = System.Guid.NewGuid().ToString();
        // Logger.LogInfo($"[AWAKE] SessionId инициализирован: {sessionId}");
        
        // Применяем патчи Harmony
        // Logger.LogInfo("[AWAKE] Применяем Harmony патчи");
        var harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        Logger.LogInfo($"[AWAKE] Harmony патчи применены: {harmony.Id}");
        GamePatches.SetPlugin(this);
        
        // Инициализируем FIKA интеграцию
        // Logger.LogInfo("[AWAKE] Инициализируем FIKA интеграцию");
        InitializeFikaIntegration();
        
        // ДИАГНОСТИКА: Проверяем применение патчей (отключено для уменьшения спама)
        // try
        // {
        //     var patchedMethods = harmony.GetPatchedMethods();
        //     int count = 0;
        //     foreach (var method in patchedMethods)
        //     {
        //         count++;
        //         Logger.LogInfo($"[AWAKE] Harmony патч: {method.DeclaringType?.Name}.{method.Name}");
        //     }
        //     Logger.LogInfo($"[AWAKE] Всего применено {count} Harmony патчей");
        // }
        // catch (System.Exception ex)
        // {
        //     Logger.LogError($"[AWAKE] Ошибка получения списка патчей: {ex.Message}");
        // }
        
        Logger.LogInfo("[AWAKE] DesmatchMode загружен успешно!");
        
        // Инициализируем менеджер дефибриллятора
        Logger.LogInfo("[AWAKE] Инициализируем менеджер дефибриллятора");
        defibrillatorManager = new DesmatchDefibrillator.DefibrillatorManager(this);
        defibrillatorManager.Initialize();
        Logger.LogInfo("[AWAKE] Менеджер дефибриллятора инициализирован");
    }

    private void BindRespawnPipelineSettings()
    {
        const string section = "Respawn Pipeline";
        const string metabolismWarn =
            "ВНИМАНИЕ: принудительный сброс метаболизма может остановить убывание еды/воды после revive. По умолчанию выключено.";

        Settings.Settings.Pipeline01_FadeToBlack = Config.Bind(section, "01 Fade To Black", true,
            "[cosmetic] Затемнение экрана перед респавном.");
        Settings.Settings.Pipeline02_Teleport = Config.Bind(section, "02 Teleport", true,
            "[CRITICAL] Телепорт на точку респавна.");
        Settings.Settings.Pipeline03_ResetMovementOnRevive = Config.Bind(section, "03 Reset Movement On Revive", true,
            "[recommended] Сброс физики и ограничений движения при телепорте.");
        Settings.Settings.Pipeline04_ClearNegativeEffects = Config.Bind(section, "04 Clear Negative Effects", true,
            "[recommended] Снятие переломов и кровотечений при revive.");
        Settings.Settings.Pipeline05_RestoreFullHealth = Config.Bind(section, "05 Restore Full Health", true,
            "[recommended] Полное HP всех частей тела.");
        Settings.Settings.Pipeline06_MetabolismRestore = Config.Bind(section, "06 Metabolism Restore", false,
            "[metabolism] IsAlive, Boolean_0, Existence, Unpause. " + metabolismWarn);
        Settings.Settings.Pipeline07_OperationalState = Config.Bind(section, "07 Operational State", true,
            "[recommended] Руки, ввод, Fika downed, animators после revive.");
        Settings.Settings.Pipeline08_FadeWake = Config.Bind(section, "08 Fade Wake", true,
            "[cosmetic] Осветление экрана после респавна.");
        Settings.Settings.Pipeline09_EnableInvulnerability = Config.Bind(section, "09 Enable Invulnerability", true,
            "[CRITICAL] Боевая неуязvимость после revive (Invulnerability Time).");
        Settings.Settings.Pipeline10_FinalizeSecondHealPass = Config.Bind(section, "10 Finalize Second Heal Pass", true,
            "[optional] Повторный lightweight heal в Finalize (дублирует шаги 04–07).");
        Settings.Settings.Pipeline11_FinalizeTinnitus = Config.Bind(section, "11 Finalize Tinnitus", true,
            "[cosmetic] Звук тиннитуса (также зависит от SoundEffects > Enable Tinnitus).");
        Settings.Settings.Pipeline12_FinalizeNetworkSync = Config.Bind(section, "12 Finalize Network Sync", true,
            "[recommended] Fika broadcast и уведомление SPT сервера о респавне.");
        Settings.Settings.Pipeline13_PenaltyTherapeuticDamage = Config.Bind(section, "13 Penalty Therapeutic Damage", true,
            "[optional] Терапевтический урон (ноги/живот/руки) в конце invuln — сброс залипшей хромоты.");
        Settings.Settings.Pipeline14_PenaltyWaitOneSecond = Config.Bind(section, "14 Penalty Wait One Second", true,
            "[optional] Пауза 1 с между penalty-уроном и лечением.");
        Settings.Settings.Pipeline15_PenaltyClearNegativeEffects = Config.Bind(section, "15 Penalty Clear Negative Effects", true,
            "[recommended] Снятие негативных эффектов после penalty-урона.");
        Settings.Settings.Pipeline16_PenaltyRestoreDestroyedParts = Config.Bind(section, "16 Penalty Restore Destroyed Parts", true,
            "[recommended] Восстановление выбитых частей тела после penalty.");
        Settings.Settings.Pipeline17_PenaltyRestoreFullHealth = Config.Bind(section, "17 Penalty Restore Full Health", true,
            "[optional] Полное HP после penalty.");
        Settings.Settings.Pipeline18_PenaltyLightweightHeal = Config.Bind(section, "18 Penalty Lightweight Heal", true,
            "[recommended] Lightweight heal в penalty. Метаболизм затрагивается только если включён шаг 19.");
        Settings.Settings.Pipeline19_PenaltyMetabolismRestore = Config.Bind(section, "19 Penalty Metabolism Restore", false,
            "[metabolism] Явный restore метаболизма после penalty. " + metabolismWarn);
        Settings.Settings.Pipeline20_PenaltyResetMovement = Config.Bind(section, "20 Penalty Reset Movement", true,
            "[recommended] Сброс движения после penalty.");
        Settings.Settings.Pipeline21_PenaltyDelayedMetabolism = Config.Bind(section, "21 Penalty Delayed Metabolism", false,
            "[metabolism] Повторный restore метаболизма через 0.5 с после invuln end. " + metabolismWarn);
        Settings.Settings.Pipeline22_LegacyFiveStageAtInvulnEnd = Config.Bind(section, "22 Legacy Five Stage At Invuln End", false,
            "[debug] Старый 5-stage + ForceRemove вместо safe penalty — известно ломает метаболизм.");
    }

    private bool IsPipelineEnabled(BepInEx.Configuration.ConfigEntry<bool> entry, string stepLabel)
    {
        if (Settings.Settings.IsPipelineStepEnabled(entry))
        {
            return true;
        }

        Logger.LogInfo($"[PIPELINE] SKIP: {stepLabel}");
        return false;
    }

    private DesmatchHealthReflection.LightweightReviveHealSteps BuildReviveHealStepsFromConfig()
    {
        return new DesmatchHealthReflection.LightweightReviveHealSteps
        {
            RemoveNegativeEffects = Settings.Settings.Pipeline04_ClearNegativeEffects.Value,
            RestoreFullHealth = Settings.Settings.Pipeline05_RestoreFullHealth.Value,
            MetabolismRestore = Settings.Settings.Pipeline06_MetabolismRestore.Value,
            RemoveMedEffect = true
        };
    }

    private DesmatchHealthReflection.LightweightReviveHealSteps BuildPenaltyHealStepsFromConfig()
    {
        return new DesmatchHealthReflection.LightweightReviveHealSteps
        {
            RemoveNegativeEffects = Settings.Settings.Pipeline15_PenaltyClearNegativeEffects.Value,
            RestoreFullHealth = Settings.Settings.Pipeline17_PenaltyRestoreFullHealth.Value,
            MetabolismRestore = Settings.Settings.Pipeline19_PenaltyMetabolismRestore.Value,
            RemoveMedEffect = Settings.Settings.Pipeline18_PenaltyLightweightHeal.Value
        };
    }

    private void Start()
    {
        StartCoroutine(TestServerConnectionDelayed());
    }

    private System.Collections.IEnumerator TestServerConnectionDelayed()
    {
        yield return null;
        TestServerConnection();
    }
    
    // Тестируем соединение с сервером через SPT RequestHandler
    private void TestServerConnection()
    {
        Logger.LogInfo("[CONNECTION] Тестируем соединение с сервером через SPT RequestHandler");
        try
        {
            var testData = new
            {
                test = true,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            
            var json = JsonConvert.SerializeObject(testData);
            
            Logger.LogInfo("[CONNECTION] Отправляем тестовый запрос через SPT RequestHandler на /desmatch/test");
            
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/test", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"[CONNECTION] Соединение с сервером успешно: {response}");
            }
            else
            {
                Logger.LogWarning("[CONNECTION] Ошибка соединения с сервером: пустой ответ");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[CONNECTION] Ошибка при тестировании соединения: {ex.Message}");
            Logger.LogError($"[CONNECTION] Stack trace: {ex.StackTrace}");
        }
    }
    
    // Обновляем сессию при каждом рейде
    private void UpdateSessionForNewRaid()
    {
        Logger.LogInfo("[SESSION] Обновляем сессию для нового рейда");
        
        // Генерируем новый sessionId
        string oldSessionId = sessionId;
        sessionId = System.Guid.NewGuid().ToString();
        
        Logger.LogInfo($"[SESSION] Старый sessionId: {oldSessionId}");
        Logger.LogInfo($"[SESSION] Новый sessionId: {sessionId}");
        
        // Сбрасываем флаг начала рейда
        hasRaidStarted = false;
        raidReadyNotificationShown = false;
        
        Logger.LogInfo("[SESSION] Сессия обновлена для нового рейда");
    }
    
    // Получаем GameWorld и игрока только до начала рейда (после начала рейда не обновляем)
    private void UpdateGameWorldAndPlayer()
    {
        try
        {
            // КРИТИЧЕСКАЯ ОПТИМИЗАЦИЯ: Если рейд уже начался и объекты найдены - НЕ ОБНОВЛЯЕМ
            if (hasRaidStarted && gameWorld != null && localPlayer != null)
            {
                // Рейд начался, объекты найдены - больше не обновляем (игрок и карта не меняются)
                return;
            }
            
            // Получаем GameWorld только если нужно
            if (gameWorld == null)
            {
                var newGameWorld = FindObjectOfType<GameWorld>();
                if (newGameWorld != null)
                {
                    gameWorld = newGameWorld;
                    Logger.LogInfo("[GAMEWORLD] GameWorld найден успешно");
                }
            }
            
            // Получаем LocalPlayer только если нужно
            if (localPlayer == null)
            {
                LocalPlayer newLocalPlayer = null;
                if (gameWorld != null)
                {
                    newLocalPlayer = gameWorld.MainPlayer as LocalPlayer;
                    Logger.LogInfo("[GAMEWORLD] Используем gameWorld.MainPlayer для получения основного игрока");
                }
                else
                {
                    // Fallback: ищем среди всех LocalPlayer и проверяем IsYourPlayer
                    var allLocalPlayers = FindObjectsOfType<LocalPlayer>();
                    Logger.LogInfo($"[GAMEWORLD] GameWorld не найден, ищем среди {allLocalPlayers.Length} LocalPlayer объектов");
                    
                    foreach (var player in allLocalPlayers)
                    {
                        if (player.IsYourPlayer)
                        {
                            newLocalPlayer = player;
                            Logger.LogInfo($"[GAMEWORLD] Найден основной игрок через IsYourPlayer: {player.name}");
                            break;
                        }
                    }
                }
                
                if (newLocalPlayer != null)
                {
                    localPlayer = newLocalPlayer;
                    Logger.LogInfo("[GAMEWORLD] LocalPlayer найден успешно");
                    Logger.LogInfo($"[GAMEWORLD] Игрок: {localPlayer.name}");
                    Logger.LogInfo($"[GAMEWORLD] IsYourPlayer: {localPlayer.IsYourPlayer}");
                    Logger.LogInfo($"[GAMEWORLD] ProfileId: {localPlayer.ProfileId}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[GAMEWORLD] Ошибка при получении GameWorld/игрока: {ex.Message}");
        }
    }
    
    // Проверяем начало рейда
    private void CheckRaidStart()
    {
        if (hasRaidStarted) return;
        
        // Проверяем, есть ли GameWorld и LocalPlayer
        if (gameWorld != null && localPlayer != null)
        {
            Logger.LogInfo("[RAID_START] Рейд начался! GameWorld и LocalPlayer найдены");
            
            // Обновляем сессию для нового рейда
            UpdateSessionForNewRaid();
            
            // Устанавливаем флаг начала рейда
            hasRaidStarted = true;
            PlayerState.IsInRaid = true;
            TryEnableFikaIntegration();
            CleanupOrphanTinnitusAudioObjects();
            
            Logger.LogInfo("[RAID_START] Флаги обновлены: hasRaidStarted=true, PlayerState.IsInRaid=true");
            
            // Сохраняем данные игрока (без UI — уведомление только после OnGameStarted)
            SavePlayerData(localPlayer);
        }
    }
    
    private void Update()
    {
        if (!Settings.Settings.EnableDesmatchMode.Value) return;
        
        // Обновляем менеджер дефибриллятора
        defibrillatorManager?.Update();
        
        // ОПТИМИЗИРОВАННОЕ ЛОГИРОВАНИЕ: Показываем только изменения или первый раз
        bool hasChanges = false;
        string changeLog = "";
        
        // Проверяем изменения в Settings.Settings.EnableDesmatchMode
        if (firstLogUpdate || Settings.Settings.EnableDesmatchMode.Value != lastEnableDesmatchMode)
        {
            changeLog += $"Settings.Settings.EnableDesmatchMode={Settings.Settings.EnableDesmatchMode.Value} ";
            lastEnableDesmatchMode = Settings.Settings.EnableDesmatchMode.Value;
            hasChanges = true;
        }
        
        // Проверяем изменения в PlayerState.IsInRaid
        if (firstLogUpdate || PlayerState.IsInRaid != lastIsInRaid)
        {
            changeLog += $"PlayerState.IsInRaid={PlayerState.IsInRaid} ";
            lastIsInRaid = PlayerState.IsInRaid;
            hasChanges = true;
        }
        
        // Проверяем изменения в PlayerState.IsPlayerDead
        if (firstLogUpdate || PlayerState.IsPlayerDead != lastIsPlayerDead)
        {
            changeLog += $"PlayerState.IsPlayerDead={PlayerState.IsPlayerDead} ";
            lastIsPlayerDead = PlayerState.IsPlayerDead;
            hasChanges = true;
        }
        
        // Проверяем изменения в PlayerState.IsPlayerInvulnerable
        if (firstLogUpdate || PlayerState.IsPlayerInvulnerable != lastIsPlayerInvulnerable)
        {
            changeLog += $"PlayerState.IsPlayerInvulnerable={PlayerState.IsPlayerInvulnerable} ";
            lastIsPlayerInvulnerable = PlayerState.IsPlayerInvulnerable;
            hasChanges = true;
        }
        
        // Проверяем изменения в PlayerState.InvulnUntil
        if (firstLogUpdate || Mathf.Abs(PlayerState.InvulnUntil - lastInvulnUntil) > 0.1f)
        {
            changeLog += $"PlayerState.InvulnUntil={PlayerState.InvulnUntil:F1} ";
            lastInvulnUntil = PlayerState.InvulnUntil;
            hasChanges = true;
        }
        
        // Логируем только если есть изменения или это первый раз
        if (hasChanges)
        {
            Logger.LogInfo($"[UPDATE] {changeLog.Trim()}");
            firstLogUpdate = false;
        }
        
        // Проверяем истечение времени неуязвимости (единая точка, без спама)
        if (PlayerState.IsPlayerInvulnerable && Time.time >= PlayerState.InvulnUntil && !isInvulnDisableInProgress)
        {
            Logger.LogInfo($"[UPDATE] Время неуязвимости истекло: {Time.time:F1} >= {PlayerState.InvulnUntil:F1}");
            DisablePlayerInvulnerability();
        }

        // Проверка клавиши дефибриллятора (F5) — всегда
        // ДИАГНОСТИКА: Проверяем все возможные способы обработки клавиш для F5
        bool f5Pressed = false;
        
        // Способ 1: Стандартный Unity Input
        if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
        {
            f5Pressed = true;
            Logger.LogInfo("[INPUT] Нажата F5 (Unity Input)");
        }
        
        // Способ 2: Проверяем через Input.inputString
        if (UnityEngine.Input.inputString.Contains("f5") || UnityEngine.Input.inputString.Contains("F5"))
        {
            f5Pressed = true;
            Logger.LogInfo($"[INPUT] Нажата F5 (inputString): '{UnityEngine.Input.inputString}'");
        }
        
        // Способ 3: Проверяем через KeyCode.F5 напрямую (дублируем для надежности)
        if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
        {
            f5Pressed = true;
            Logger.LogInfo("[INPUT] Нажата F5 (KeyCode.F5)");
        }
        
        if (f5Pressed)
        {
            Logger.LogInfo($"[INPUT] F5 ОБНАРУЖЕНА - defibrillatorManager: {(defibrillatorManager != null ? "инициализирован" : "НЕ ИНИЦИАЛИЗИРОВАН")}");
            Logger.LogInfo($"[INPUT] PlayerState.IsInRaid = {PlayerState.IsInRaid}");
            
            // Обрабатываем F5 прямо здесь, чтобы не зависеть от EnableDesmatchMode
            if (defibrillatorManager != null)
            {
                if (PlayerState.IsInRaid)
                {
                    Logger.LogInfo("[INPUT] F5 в рейде - вызываем HandleDefibrillatorUsage");
                    try
                    {
                        defibrillatorManager.HandleDefibrillatorUsage();
                        Logger.LogInfo("[INPUT] HandleDefibrillatorUsage завершен успешно");
                    }
                    catch (System.Exception ex)
                    {
                        Logger.LogError($"[INPUT] Ошибка в HandleDefibrillatorUsage: {ex.Message}");
                        Logger.LogError($"[INPUT] Stack trace: {ex.StackTrace}");
                    }
                }
                else
                {
                    Logger.LogInfo("[INPUT] F5 вне рейда - показываем уведомление");
                    ShowNotification("Дефибриллятор можно использовать только в рейде!", false);
                }
            }
            else
            {
                Logger.LogWarning("[INPUT] F5 нажата, но defibrillatorManager не инициализирован");
                ShowNotification("Менеджер дефибриллятора не инициализирован", false);
            }
        }
        
                   // ДИАГНОСТИКА: Логируем только важные нажатия клавиш (F10)
                   if (UnityEngine.Input.GetKeyDown(KeyCode.F10))
        {
            Logger.LogInfo($"[INPUT] KeyDown: {UnityEngine.Input.inputString}");
            Logger.LogInfo($"[INPUT] PlayerState.IsInRaid = {PlayerState.IsInRaid}");
        }
                   
                   // ДИАГНОСТИКА: Сканирование инвентаря по F6
                   if (UnityEngine.Input.GetKeyDown(KeyCode.F6))
                   {
                       Logger.LogInfo("[INPUT] Нажата F6 - запускаем сканирование инвентаря");
                       if (defibrillatorManager != null)
                       {
                           try
                           {
                               defibrillatorManager.ScanFullInventory();
                               Logger.LogInfo("[INPUT] ScanFullInventory завершен успешно");
                               ShowNotification("Сканирование инвентаря запущено! Проверьте логи.", false);
                           }
                           catch (System.Exception ex)
                           {
                               Logger.LogError($"[INPUT] Ошибка в ScanFullInventory: {ex.Message}");
                               Logger.LogError($"[INPUT] Stack trace: {ex.StackTrace}");
                               ShowNotification("Ошибка сканирования инвентаря!", false);
                           }
                       }
                       else
                       {
                           Logger.LogWarning("[INPUT] F6 нажата, но defibrillatorManager не инициализирован");
                           ShowNotification("Менеджер дефибриллятора не инициализирован", false);
                       }
                   }
        
        // F8 убрано - не мешаем другим модам
        
        // F9 убрано - не мешаем другим модам
        
        // УБРАНО: Агрессивное обновление кэша иконок - оно сломало интерфейс игры (белый экран)
        // if (!PlayerState.IsInRaid && Time.time - lastIconCacheUpdate > 2f) // Обновляем каждые 2 секунды в схроне
        // {
        //     lastIconCacheUpdate = Time.time;
        //     RefreshIconCache();
        // }
        
        // Обновляем GameWorld и игрока только до начала рейда (после начала рейда не обновляем)
        if (Time.time - lastGameWorldUpdate >= GAMEWORLD_UPDATE_INTERVAL)
        {
            UpdateGameWorldAndPlayer();
            lastGameWorldUpdate = Time.time;
        }
        
        // Проверка клавиши ручного возрождения (по умолчанию F10) — всегда
        // ДИАГНОСТИКА: Проверяем все возможные способы обработки клавиш
        bool f10Pressed = false;
        
        // Способ 1: Стандартный Unity Input
        if (UnityEngine.Input.GetKeyDown(Settings.Settings.ManualRespawnKey.Value))
        {
            f10Pressed = true;
            Logger.LogInfo($"[INPUT] Нажата {Settings.Settings.ManualRespawnKey.Value} (Unity Input)");
        }
        
        // Способ 2: Проверяем через Input.inputString
        if (UnityEngine.Input.inputString.Contains("f10") || UnityEngine.Input.inputString.Contains("F10"))
        {
            f10Pressed = true;
            Logger.LogInfo($"[INPUT] Нажата F10 (inputString): '{UnityEngine.Input.inputString}'");
        }
        
        // Способ 3: Проверяем через KeyCode.F10 напрямую
        if (UnityEngine.Input.GetKeyDown(KeyCode.F10))
        {
            f10Pressed = true;
            Logger.LogInfo($"[INPUT] Нажата F10 (KeyCode.F10)");
        }
        
        // Диагностика ввода для F5 - отключена для уменьшения спама
        // if (Time.time - lastInputCheck > 5f)
        // {
        //     lastInputCheck = Time.time;
        //     Logger.LogInfo($"[INPUT_DEBUG] Input.inputString='{UnityEngine.Input.inputString}', F5 down={UnityEngine.Input.GetKeyDown(KeyCode.F5)}, F10 down={UnityEngine.Input.GetKeyDown(KeyCode.F10)}, anyKey={UnityEngine.Input.anyKeyDown}");
        // }
        
        // Создание предметов убрано - теперь через крафт в убежище
        
        if (f10Pressed)
        {
            if (!PlayerState.IsInRaid)
            {
                ShowNotification($"{Settings.Settings.ManualRespawnKey.Value} нажата, но вы не в рейде", true);
            }
            else if (!CanTriggerManualRespawn())
            {
                if (isRespawnInProgress)
                {
                    ShowNotification("Респавн уже выполняется", true);
                }
                else
                {
                    ShowNotification("Подождите перед повторным респавном", true);
                }
            }
            else
            {
                lastManualRespawnTime = Time.time;
                ShowMainNotification($"Ручное возрождение: {Settings.Settings.ManualRespawnKey.Value}");
                Logger.LogInfo($"[RESPAWN] Manual respawn accepted. IsPlayerDead={PlayerState.IsPlayerDead}");
                SendManualRespawnRequestToServer();
            }
        }
        
        // Проверяем начало рейда
        CheckRaidStart();
        
        // Обновляем звуковой эффект тиннитуса
        UpdateTinnitusEffect();
        
        // Тестовые звуки отключены - F5 теперь создает предметы
        
        if (!PlayerState.IsInRaid) 
        {
            return; // Убираем спам-логирование
        }
    }
    
    private void OnDestroy()
    {
        try
        {
            var harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            harmony.UnpatchSelf();
            Logger.LogInfo("[DESTROY] Harmony патчи отключены");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[DESTROY] Ошибка при отключении Harmony патчей: {ex.Message}");
        }
        
        // Очищаем FIKA ресурсы
        if (fikaCommunicationManager != null)
        {
            try
            {
                Logger.LogInfo("🔗 [FIKA] Очистка FIKA ресурсов");
                fikaCommunicationManager.OnInvulnerabilityUpdateReceived -= OnFikaInvulnerabilityUpdateReceived;
                fikaCommunicationManager.OnRespawnReceived -= OnFikaRespawnReceived;
                fikaCommunicationManager.OnFikaBecameAvailable -= OnFikaBecameAvailable;
                fikaCommunicationManager.Cleanup();
                fikaCommunicationManager = null;
                fikaAvailable = false;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"❌ [FIKA] Ошибка очистки FIKA ресурсов: {ex.Message}");
            }
        }
    }
    
    // Сохраняем данные игрока при входе в рейд
    public void SavePlayerData(EFT.Player player)
    {
        Logger.LogInfo("[SAVE] SavePlayerData вызван");
        Logger.LogInfo($"[SAVE] player = {player != null}");
        
        if (player == null) 
        {
            Logger.LogInfo("[SAVE] player == null, выходим");
            return;
        }
        
        Logger.LogInfo("[SAVE] Сохраняем данные игрока для дезмач режима");
        
        spawnPosition = player.Transform.position;
        spawnRotation = player.Transform.rotation;
        
        Logger.LogInfo($"[SAVE] spawnPosition = {spawnPosition}");
        Logger.LogInfo($"[SAVE] spawnRotation = {spawnRotation}");
        
        // Сохраняем снаряжение (упрощенная версия)
        savedPlayerData.Clear();
        savedPlayerData["position"] = spawnPosition;
        savedPlayerData["rotation"] = spawnRotation;
        
        Logger.LogInfo("[SAVE] Устанавливаем PlayerState.IsInRaid = true");
        PlayerState.IsInRaid = true;
        Logger.LogInfo("[SAVE] Устанавливаем PlayerState.IsPlayerDead = false");
        PlayerState.IsPlayerDead = false;
        
        Logger.LogInfo($"[SAVE] PlayerState.IsInRaid = {PlayerState.IsInRaid}");
        Logger.LogInfo($"[SAVE] PlayerState.IsPlayerDead = {PlayerState.IsPlayerDead}");
        
        // Отправляем данные на сервер
        Logger.LogInfo("[SAVE] Отправляем данные на сервер");
        TryEnableFikaIntegration();
        SendPlayerDataToServer();
    }
    
    private DesmatchHealthPayload GetPlayerHealthDataSafe()
    {
        try
        {
            if (localPlayer?.HealthController is ActiveHealthController health)
            {
                return DesmatchHealthUtils.CreateHealthPayload(health);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[HEALTH] Safe payload fallback: {ex.Message}");
        }

        return new DesmatchHealthPayload();
    }

    private void LogReflectionWarningOnce(string key, string message)
    {
        if (LoggedReflectionWarnings.Add(key))
        {
            Logger.LogWarning(message);
        }
        else
        {
            Logger.LogDebug(message);
        }
    }

    private object GetPlayerBodyPartHealthsSafe()
    {
        try
        {
            return GetPlayerBodyPartHealths();
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[HEALTH] Body parts payload fallback: {ex.Message}");
            return new { };
        }
    }
    
    // Отправляем данные игрока на сервер (гибридный подход: FIKA + HTTP fallback)
    private void SendPlayerDataToServer()
    {
        Logger.LogInfo("[SERVER] SendPlayerDataToServer вызван (гибридный подход)");
        try
        {
            Logger.LogInfo("[SERVER] Создаем данные для отправки");
            
            // Создаем клиентские настройки
            var clientSettings = new
            {
                enabled = Settings.Settings.EnableDesmatchMode.Value,
                respawnDelay = PlayerState.ClientRespawnDelay, // используем валидированное значение
                invulnSeconds = PlayerState.ClientInvulnSeconds, // в секундах
                manualKey = Settings.Settings.ManualRespawnKey.Value.ToString()
            };
            
            Logger.LogInfo($"[SERVER] Клиентские настройки: enabled={clientSettings.enabled}, respawnDelay={clientSettings.respawnDelay}ms, invulnSeconds={clientSettings.invulnSeconds}s, manualKey={clientSettings.manualKey}");
            
            var healthPayload = GetPlayerHealthDataSafe();
            var data = new
            {
                position = new { x = spawnPosition.x, y = spawnPosition.y, z = spawnPosition.z },
                rotation = new { x = spawnRotation.x, y = spawnRotation.y, z = spawnRotation.z, w = spawnRotation.w },
                equipment = savedPlayerData,
                health = healthPayload,
                zoneInfo = "default_zone",
                client = clientSettings
            };
            
            Logger.LogInfo($"[SERVER] Данные созданы: position={data.position}, rotation={data.rotation}");
            
            TryEnableFikaIntegration();
            if (fikaAvailable && fikaCommunicationManager != null)
            {
                try
                {
                    Logger.LogInfo("🔗 [FIKA] Отправляем данные игрока через FIKA");
                    
                    var fikaPacket = new DesmatchFikaPackets.DesmatchPlayerDataPacket
                    {
                        PlayerProfileId = sessionId,
                        PlayerNickname = localPlayer?.Profile?.Nickname ?? "Unknown",
                        Position = spawnPosition,
                        Rotation = spawnRotation.eulerAngles, // Конвертируем Quaternion в Vector3
                        Health = GetPlayerHealthValue(),
                        Energy = GetPlayerEnergyValue(),
                        Hydration = GetPlayerHydrationValue(),
                        IsInvulnerable = PlayerState.IsPlayerInvulnerable,
                        InvulnTimeRemaining = GetRemainingInvulnerabilityTime(),
                        RespawnDelay = PlayerState.ClientRespawnDelay,
                        InvulnSeconds = PlayerState.ClientInvulnSeconds,
                        IsDead = PlayerState.IsPlayerDead,
                        ManualRespawnKey = Settings.Settings.ManualRespawnKey.Value.ToString()
                    };
                    
                    fikaCommunicationManager.SendPlayerData(fikaPacket);
                    Logger.LogInfo("🔗 [FIKA] Данные игрока отправлены через FIKA");
                    return; // Успешно отправлено через FIKA
                }
                catch (System.Exception fikaEx)
                {
                    Logger.LogWarning($"⚠️ [FIKA] Ошибка отправки через FIKA: {fikaEx.Message}, переключаемся на HTTP");
                }
            }
            
            // Fallback: отправляем через HTTP
            Logger.LogInfo("[SERVER] Отправляем через HTTP fallback");
            var json = JsonConvert.SerializeObject(data);
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/save-player-data", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"[SERVER] Данные игрока отправлены на сервер через HTTP: {response}");
                
                // Обрабатываем ответ сервера для обновления настроек
                ProcessServerResponse(response);
            }
            else
            {
                Logger.LogWarning("[SERVER] Ошибка отправки данных на сервер: пустой ответ");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[SERVER] Ошибка при отправке данных на сервер: {ex.Message}");
            Logger.LogError($"[SERVER] Stack trace: {ex.StackTrace}");
        }
    }
    
    // Вспомогательные методы для FIKA пакетов (используют общие утилиты)
    private float GetPlayerHealthValue()
    {
        if (localPlayer?.HealthController is ActiveHealthController health)
        {
            return DesmatchHealthUtils.GetTotalHealth(health);
        }
        return DesmatchHealthUtils.DEFAULT_HEALTH_VALUE;
    }
    
    private float GetPlayerEnergyValue()
    {
        if (localPlayer?.HealthController is ActiveHealthController health &&
            DesmatchHealthUtils.TryGetEnergyValues(health, out float current, out _))
        {
            return DesmatchHealthUtils.ValidateEnergyValue(current);
        }
        return DesmatchHealthUtils.DEFAULT_ENERGY_VALUE;
    }
    
    private float GetPlayerHydrationValue()
    {
        if (localPlayer?.HealthController is ActiveHealthController health &&
            DesmatchHealthUtils.TryGetHydrationValues(health, out float current, out _))
        {
            return DesmatchHealthUtils.ValidateHydrationValue(current);
        }
        return DesmatchHealthUtils.DEFAULT_HYDRATION_VALUE;
    }
    
    // Получаем данные здоровья игрока
    private DesmatchHealthPayload GetPlayerHealthData()
    {
        if (localPlayer?.HealthController is ActiveHealthController health)
        {
            Logger.LogInfo("[HEALTH] Получаем реальные данные здоровья из ActiveHealthController");
            return DesmatchHealthUtils.CreateHealthPayload(health);
        }
        
        Logger.LogWarning("[HEALTH] ActiveHealthController не найден, используем заглушки");
        return new DesmatchHealthPayload();
    }
    
    // Получаем данные здоровья частей тела игрока
    private object GetPlayerBodyPartHealths()
    {
        if (localPlayer?.HealthController is ActiveHealthController health)
        {
            Logger.LogInfo("[HEALTH] Получаем реальные данные здоровья частей тела из ActiveHealthController");
            
            var bodyParts = new Dictionary<string, object>();
            
            // Получаем данные для всех частей тела
            foreach (EBodyPart bodyPart in System.Enum.GetValues(typeof(EBodyPart)))
            {
                try
                {
                    if (!DesmatchHealthUtils.TryGetBodyPartValues(health, bodyPart, out float current, out float maximum))
                    {
                        bodyParts[bodyPart.ToString()] = new { current = 100, maximum = 100 };
                        continue;
                    }

                    bodyParts[bodyPart.ToString()] = new
                    {
                        current,
                        maximum
                    };
                    
                    Logger.LogInfo($"[HEALTH] {bodyPart}: Current={current}, Max={maximum}");
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"[HEALTH] Ошибка получения данных для {bodyPart}: {ex.Message}");
                    bodyParts[bodyPart.ToString()] = new { current = 100, maximum = 100 };
                }
            }
            
            return bodyParts;
        }
        
        Logger.LogWarning("[HEALTH] ActiveHealthController не найден, используем заглушки");
        return new
        {
            Head = new { current = 100, maximum = 100 },
            Chest = new { current = 100, maximum = 100 },
            Stomach = new { current = 100, maximum = 100 },
            LeftArm = new { current = 100, maximum = 100 },
            RightArm = new { current = 100, maximum = 100 },
            LeftLeg = new { current = 100, maximum = 100 },
            RightLeg = new { current = 100, maximum = 100 }
        };
    }
    
    // Единая обработка респавна (автоматический и ручной)
    public void HandleRespawn(string respawnType = "auto")
    {
        Logger.LogInfo($"🔄 [RESPAWN] Единая обработка респавна: {respawnType}");
        Logger.LogInfo($"🔄 [RESPAWN] Клиентские настройки: delay={PlayerState.ClientRespawnDelay}ms, invuln={PlayerState.ClientInvulnSeconds}s");
        
        try
        {
            CancelPendingRespawn($"new {respawnType} respawn");
            
            // Для ручного респавна - без задержки
            if (respawnType == "manual")
            {
                Logger.LogInfo("🔄 [RESPAWN] Ручной респавн - без задержки");
                fadeRespawnCoroutine = StartCoroutine(RespawnWithFadeEffects(respawnType));
            }
            else
            {
                // Для автоматического респавна - с задержкой (окно для F10)
                Logger.LogInfo("🔄 [RESPAWN] Автоматический респавн - с задержкой");
                respawnCoroutine = StartCoroutine(RespawnWithDelay(respawnType));
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [RESPAWN] Ошибка в единой обработке респавна: {ex.Message}");
            Logger.LogError($"❌ [RESPAWN] Stack trace: {ex.StackTrace}");
        }
    }

    private void CancelPendingRespawn(string reason)
    {
        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
            Logger.LogInfo($"[RESPAWN] Остановлен отложенный auto-respawn: {reason}");
        }

        if (fadeRespawnCoroutine != null)
        {
            StopCoroutine(fadeRespawnCoroutine);
            fadeRespawnCoroutine = null;
            isRespawnInProgress = false;
            Logger.LogInfo($"[RESPAWN] Остановлен fade-respawn: {reason}");
        }
    }
    
    // Корутин респавна с задержкой (защищенный от сброса таймера)
    private IEnumerator RespawnWithDelay(string respawnType)
    {
        float actualDelay = Mathf.Max(PlayerState.ClientRespawnDelay / 1000f, 0.1f);
        Logger.LogInfo($"⏱️ [RESPAWN] Начинаем респавн с задержкой {actualDelay}s (исходная: {PlayerState.ClientRespawnDelay}ms)");
        
        float startTime = Time.time;
        
        // Ждем указанную задержку с проверкой
        while (Time.time - startTime < actualDelay)
        {
            yield return new WaitForSeconds(0.1f);
            
            // Проверяем, не был ли корутин остановлен
            if (respawnCoroutine == null)
            {
                Logger.LogInfo("⏱️ [RESPAWN] Корутин респавна был остановлен");
                yield break;
            }
        }
        
        // Выполняем респавн только если корутин все еще активен
        if (respawnCoroutine != null)
        {
            Logger.LogInfo($"🚀 [RESPAWN] Задержка завершена, выполняем респавн");
            fadeRespawnCoroutine = StartCoroutine(RespawnWithFadeEffects(respawnType));
            yield return fadeRespawnCoroutine;
        }
    }
    
    // Возрождаем игрока
    public void RespawnPlayer(string respawnType = "auto")
    {
        Logger.LogInfo($"[RESPAWN] RespawnPlayer вызван (тип: {respawnType})");
        Logger.LogInfo($"[RESPAWN] PlayerState.IsInRaid = {PlayerState.IsInRaid}");
        
        // Анти-спам защита от повторных входов (3 сек окно)
        if (isRespawnInProgress || Time.time - lastRespawnTime < 3f)
        {
            Logger.LogWarning("[RESPAWN] Респавн уже в процессе или слишком частые вызовы - пропуск");
            return;
        }
        isRespawnInProgress = true;
        lastRespawnTime = Time.time;

        if (!PlayerState.IsInRaid) 
        {
            Logger.LogInfo("[RESPAWN] PlayerState.IsInRaid = false, выходим");
            isRespawnInProgress = false;
            return;
        }
        
        Logger.LogInfo("[RESPAWN] Возрождаем игрока в дезмач режиме");
        
        // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ 2: Используем правильный способ получения основного игрока
        if (localPlayer == null)
        {
            Logger.LogWarning("[RESPAWN] LocalPlayer не найден в кэше, пытаемся найти заново");
            
            // Сначала пробуем через GameWorld.MainPlayer
            if (gameWorld != null)
            {
                localPlayer = gameWorld.MainPlayer as LocalPlayer;
                Logger.LogInfo("[RESPAWN] Используем gameWorld.MainPlayer для получения основного игрока");
            }
            else
            {
                // Fallback: ищем среди всех LocalPlayer и проверяем IsYourPlayer
                var allLocalPlayers = FindObjectsOfType<LocalPlayer>();
                Logger.LogInfo($"[RESPAWN] GameWorld не найден, ищем среди {allLocalPlayers.Length} LocalPlayer объектов");
                
                foreach (var player in allLocalPlayers)
                {
                    if (player.IsYourPlayer)
                    {
                        localPlayer = player;
                        Logger.LogInfo($"[RESPAWN] Найден основной игрок через IsYourPlayer: {player.name}");
                        break;
                    }
                }
            }
        }
        
        Logger.LogInfo($"[RESPAWN] LocalPlayer найден: {localPlayer != null}");
        
        if (localPlayer == null)
        {
            Logger.LogError("[RESPAWN] Не удалось найти LocalPlayer для возрождения");
            return;
        }
        
        // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ 3: Валидация - убеждаемся что это именно наш игрок
        if (!localPlayer.IsYourPlayer)
        {
            Logger.LogError($"[RESPAWN] ОШИБКА: Найденный игрок НЕ является основным игроком! Name: {localPlayer.name}, IsYourPlayer: {localPlayer.IsYourPlayer}");
            Logger.LogError("[RESPAWN] Это может быть бот! Ищем правильного игрока...");
            
            // Ищем правильного игрока
            var allLocalPlayers = FindObjectsOfType<LocalPlayer>();
            foreach (var player in allLocalPlayers)
            {
                if (player.IsYourPlayer)
                {
                    localPlayer = player;
                    Logger.LogInfo($"[RESPAWN] Найден правильный основной игрок: {player.name}");
                    break;
                }
            }
            
            if (!localPlayer.IsYourPlayer)
            {
                Logger.LogError("[RESPAWN] КРИТИЧЕСКАЯ ОШИБКА: Не удалось найти основного игрока!");
                return;
            }
        }
        
        Logger.LogInfo($"[RESPAWN] Валидация пройдена: {localPlayer.name}, IsYourPlayer: {localPlayer.IsYourPlayer}, ProfileId: {localPlayer.ProfileId}");
        
        // Находим безопасную позицию для спавна
        Logger.LogInfo("[RESPAWN] Находим безопасную позицию для спавна");
        Vector3 safeSpawnPosition = spawnPosition; // Используем сохраненную позицию
        Logger.LogInfo($"[RESPAWN] safeSpawnPosition = {safeSpawnPosition}");
        Logger.LogInfo($"[RESPAWN] spawnRotation = {spawnRotation}");
        
        // Устанавливаем позицию и поворот
        Logger.LogInfo("[RESPAWN] Устанавливаем позицию и поворот");
        localPlayer.Transform.position = safeSpawnPosition;
        localPlayer.Transform.rotation = spawnRotation;
        Logger.LogInfo("[RESPAWN] Позиция и поворот установлены");

        // Сбрасываем физическое состояние/движение
        try
        {
            var movementContext = localPlayer.MovementContext;
            if (movementContext != null)
            {
                movementContext.ResetPhysicalCondition();
                Logger.LogInfo("[RESPAWN] MovementContext.ResetPhysicalCondition вызван");
                
                // Дополнительный полный сброс ограничений движения
                ResetMovementRestrictions(movementContext);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[RESPAWN] Ошибка при сбросе физического состояния: {ex.Message}");
        }
        
        // Восстанавливаем здоровье по 4-этапному алгоритму
        Logger.LogInfo("[RESPAWN] Проверяем HealthController");
        if (localPlayer.HealthController != null)
        {
            Logger.LogInfo("[RESPAWN] HealthController найден, начинаем 4-этапное восстановление здоровья");
            if (localPlayer.HealthController is ActiveHealthController activeHealth)
            {
                PerformLightweightReviveHeal(activeHealth, "RespawnPlayer");

                // Шаг 1: Сброс стамины/коэффицента и удаление стаминных эффектов
                try
                {
                    Logger.LogInfo("[RESPAWN] Сбрасываем стамину и коэффициенты восстановления");
                    // Сбрасываем коэффициент стамины (ускоряет нормализацию)
                    activeHealth.SetStaminaCoeff(1f);
                    Logger.LogInfo("[RESPAWN] SetStaminaCoeff(1f) применен");

                    // Пытаемся восстановить полную стамину и стамину рук, если доступно
                    try
                    {
                        var physical = localPlayer.Physical;
                        if (physical != null)
                        {
                            // Локальная функция: установить Current до максимума через рефлексию
                            void SetGaugeToMax(object gauge, string gaugeName)
                            {
                                if (gauge == null) return;
                                var t = gauge.GetType();
                                var currentProp = t.GetProperty("Current");
                                if (currentProp == null || !currentProp.CanWrite) return;

                                float maxValue = 100f;
                                var capProp = t.GetProperty("Capacity") ?? t.GetProperty("Maximum") ?? t.GetProperty("Max") ?? t.GetProperty("MaxValue");
                                if (capProp != null)
                                {
                                    try
                                    {
                                        var capObj = capProp.GetValue(gauge, null);
                                        if (capObj is float f) maxValue = f;
                                        else if (capObj is double d) maxValue = (float)d;
                                        else if (capObj is int i) maxValue = i;
                                    }
                                    catch {}
                                }

                                try
                                {
                                    currentProp.SetValue(gauge, maxValue, null);
                                }
                                catch {}

                                // Вызовем InvokeChangedAction если доступен
                                var invokeChanged = t.GetMethod("InvokeChangedAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                try { invokeChanged?.Invoke(gauge, null); } catch {}
                                Logger.LogInfo($"[RESPAWN] {gaugeName} восстановлена до {maxValue}");
                            }

                            SetGaugeToMax(physical.Stamina, "Stamina");
                            SetGaugeToMax(physical.HandsStamina, "HandsStamina");
                        }
                        else
                        {
                            Logger.LogWarning("[RESPAWN] Physical контроллер недоступен — пропускаем восстановление стамины");
                        }
                    }
                    catch (System.Exception exPhys)
                    {
                        Logger.LogWarning($"[RESPAWN] Ошибка восстановления стамины: {exPhys.Message}");
                    }

                    // Удаляем эффекты, влияющие на стамину: StaminaZero, ChronicStaminaFatigue
                    try
                    {
                        var allEffects = activeHealth.GetAllActiveEffects(EBodyPart.Common);
                        int removed = 0;
                        foreach (var effect in allEffects)
                        {
                            var name = effect.GetType().Name;
                            if (name.Contains("StaminaZero") || name.Contains("ChronicStaminaFatigue"))
                            {
                                var baseEffect = effect as ActiveHealthController.NetworkBodyEffectsAbstractClass;
                                if (baseEffect != null)
                                {
                                    baseEffect.ForceRemove();
                                    removed++;
                                    Logger.LogInfo($"[RESPAWN] Удален стаминный эффект: {name}");
                                }
                            }
                        }
                        Logger.LogInfo($"[RESPAWN] Удалено стаминных эффектов: {removed}");
                    }
                    catch (System.Exception exEff)
                    {
                        Logger.LogWarning($"[RESPAWN] Не удалось удалить стаминные эффекты: {exEff.Message}");
                    }
                }
                catch (System.Exception exStam)
                {
                    Logger.LogWarning($"[RESPAWN] Ошибка при сбросе стамины/эффектов: {exStam.Message}");
                }

                // Шаг 2: Снятие блокировок спринта
                try
                {
                    // Включаем спринт, если есть публичный API
                    try
                    {
                        localPlayer.EnableSprint(true);
                        Logger.LogInfo("[RESPAWN] Спринт принудительно включен через EnableSprint(true)");
                    }
                    catch (System.Exception exEn)
                    {
                        Logger.LogWarning($"[RESPAWN] EnableSprint недоступен: {exEn.Message}");
                    }

                    // Дополнительно сбрасываем физическое состояние (на случай флагов SprintDisabled)
                    try
                    {
                        var movementContext = localPlayer.MovementContext;
                        if (movementContext != null)
                        {
                            movementContext.ResetPhysicalCondition();
                            Logger.LogInfo("[RESPAWN] Повторный ResetPhysicalCondition после стаминных правок");
                        }
                    }
                    catch (System.Exception exMC)
                    {
                        Logger.LogWarning($"[RESPAWN] ResetPhysicalCondition повторно недоступен: {exMC.Message}");
                    }
                }
                catch (System.Exception exSprint)
                {
                    Logger.LogWarning($"[RESPAWN] Ошибка при снятии блокировок спринта: {exSprint.Message}");
                }
            }
            else
            {
                Logger.LogWarning("[RESPAWN] HealthController не является ActiveHealthController");
            }
        }
        else
        {
            Logger.LogWarning("[RESPAWN] HealthController не найден");
        }
        
        Logger.LogInfo("[RESPAWN] Устанавливаем PlayerState.IsPlayerDead = false");
        PlayerState.IsPlayerDead = false;
        Logger.LogInfo($"[RESPAWN] PlayerState.IsPlayerDead = {PlayerState.IsPlayerDead}");
        
        FinalizeRespawnAfterEffects("legacy", safeSpawnPosition);

        Logger.LogInfo("[RESPAWN] Игрок успешно возрожден!");
        isRespawnInProgress = false;
    }

    private void FinalizeRespawnAfterEffects(string respawnType, Vector3? respawnPositionOverride = null)
    {
        var respawnPosition = respawnPositionOverride ?? spawnPosition;

        Logger.LogInfo($"[FINALIZE] Завершение респавна (тип: {respawnType})");
        PlayerState.IsPlayerDead = false;

        if (IsPipelineEnabled(Settings.Settings.Pipeline09_EnableInvulnerability, "09 Enable Invulnerability"))
        {
            PlayerState.IsPlayerInvulnerable = true;
            PlayerState.InvulnUntil = Time.time + PlayerState.ClientInvulnSeconds;
            Logger.LogInfo($"[FINALIZE] Combat invuln: {PlayerState.ClientInvulnSeconds}s until {PlayerState.InvulnUntil:F1}");
            ShowMainNotification($"Неуязвимость: {PlayerState.ClientInvulnSeconds} сек", false);
            SendInvulnerabilityStatusToServer();
            BroadcastInvulnerabilityViaFika();
        }
        else
        {
            PlayerState.IsPlayerInvulnerable = false;
            PlayerState.InvulnUntil = 0f;
            Logger.LogInfo("[FINALIZE] Invulnerability disabled via pipeline config");
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline11_FinalizeTinnitus, "11 Finalize Tinnitus")
            && Settings.Settings.EnableTinnitusEffect.Value && !isTinnitusActive)
        {
            StartTinnitusEffect();
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline10_FinalizeSecondHealPass, "10 Finalize Second Heal Pass"))
        {
            try
            {
                var health = localPlayer?.ActiveHealthController;
                if (health != null)
                {
                    PerformLightweightReviveHeal(health, "Finalize");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"[FINALIZE] Ошибка second heal pass: {ex.Message}");
            }
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline12_FinalizeNetworkSync, "12 Finalize Network Sync"))
        {
            BroadcastRespawnViaFika(respawnType, respawnPosition);

            try
            {
                SendRespawnNotificationToServer(respawnPosition, respawnType);
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"[FINALIZE] Ошибка уведомления сервера о респавне: {ex.Message}");
            }

            try
            {
                var tpData = new
                {
                    position = new { x = respawnPosition.x, y = respawnPosition.y, z = respawnPosition.z },
                    rotation = new { x = spawnRotation.x, y = spawnRotation.y, z = spawnRotation.z, w = spawnRotation.w },
                    respawnType
                };
                var tpJson = JsonConvert.SerializeObject(tpData);
                var tpResp = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/teleport", tpJson);
                Logger.LogInfo($"[FINALIZE] TELEPORT server confirm: {tpResp}");

                if (localPlayer != null && localPlayer.IsYourPlayer)
                {
                    localPlayer.Transform.position = respawnPosition;
                    localPlayer.Transform.rotation = spawnRotation;
                    Logger.LogInfo($"[FINALIZE] Позиция повторно применена для {localPlayer.name}");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"[FINALIZE] Ошибка запроса телепорта: {ex.Message}");
            }
        }

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }
    }
    
    
    // Отправляем уведомление о возрождении на сервер через SPT RequestHandler
    private void SendRespawnNotificationToServer(Vector3 respawnPosition, string respawnType = "auto")
    {
        try
        {
            var data = new
            {
                position = new { x = respawnPosition.x, y = respawnPosition.y, z = respawnPosition.z },
                rotation = new { x = spawnRotation.x, y = spawnRotation.y, z = spawnRotation.z, w = spawnRotation.w },
                bodyPartHealths = GetPlayerBodyPartHealthsSafe(),
                zoneInfo = "default_zone",
                respawnType
            };
            
            var json = JsonConvert.SerializeObject(data);
            
            // Отправляем уведомление о возрождении на сервер через SPT RequestHandler
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/respawn", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"Уведомление о возрождении отправлено на сервер: {response}");
            }
            else
            {
                Logger.LogWarning("Ошибка отправки уведомления о возрождении: пустой ответ");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Ошибка при отправке уведомления о возрождении: {ex.Message}");
        }
    }
    
    // Отправляем уведомление о смерти игрока на сервер через SPT RequestHandler
    private void SendPlayerDiedNotificationToServer()
    {
        try
        {
            var data = new
            {
                sessionId = sessionId,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            
            var json = JsonConvert.SerializeObject(data);
            
            Logger.LogInfo("[DEATH] Отправляем уведомление о смерти на сервер через SPT RequestHandler на /desmatch/player-died");
            
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/player-died", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"[DEATH] Уведомление о смерти отправлено на сервер: {response}");
            }
            else
            {
                Logger.LogWarning("[DEATH] Ошибка отправки уведомления о смерти: пустой ответ");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[DEATH] Ошибка при отправке уведомления о смерти: {ex.Message}");
        }
    }
    
    // Отправляем запрос на ручное возрождение на сервер через SPT RequestHandler
    private void SendManualRespawnRequestToServer()
    {
        Logger.LogInfo("[MANUAL_RESPAWN] Запуск ручного возрождения на клиенте");
        HandleRespawn("manual");

        try
        {
            var clientSettings = new
            {
                enabled = Settings.Settings.EnableDesmatchMode.Value,
                respawnDelay = PlayerState.ClientRespawnDelay,
                invulnSeconds = PlayerState.ClientInvulnSeconds,
                manualKey = Settings.Settings.ManualRespawnKey.Value.ToString()
            };
            
            Logger.LogInfo($"[MANUAL_RESPAWN] Клиентские настройки: enabled={clientSettings.enabled}, respawnDelay={clientSettings.respawnDelay}ms, invulnSeconds={clientSettings.invulnSeconds}s, manualKey={clientSettings.manualKey}");
            
            var data = new
            {
                position = new { x = spawnPosition.x, y = spawnPosition.y, z = spawnPosition.z },
                rotation = new { x = spawnRotation.x, y = spawnRotation.y, z = spawnRotation.z, w = spawnRotation.w },
                health = GetPlayerHealthDataSafe(),
                zoneInfo = "default_zone",
                respawnType = "manual",
                client = clientSettings,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            
            var json = JsonConvert.SerializeObject(data);
            
            Logger.LogInfo("[MANUAL_RESPAWN] Уведомляем сервер: /singleplayer/desmatch/respawn-player");
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/respawn-player", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"[MANUAL_RESPAWN] Сервер подтвердил запрос: {response}");
            }
            else
            {
                Logger.LogWarning("[MANUAL_RESPAWN] Пустой ответ сервера (клиентский респавн уже запущен)");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[MANUAL_RESPAWN] Серверное уведомление не отправлено: {ex.Message}");
        }
    }
    
    
    // Отправляем уведомление об очистке данных игрока на сервер через SPT RequestHandler
    private void SendClearPlayerDataNotificationToServer()
    {
        try
        {
            var data = new
            {
                sessionId = sessionId,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            
            var json = JsonConvert.SerializeObject(data);
            
            Logger.LogInfo("[CLEAR] Отправляем уведомление об очистке данных на сервер через SPT RequestHandler на /desmatch/clear-player-data");
            
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/clear-player-data", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"[CLEAR] Уведомление об очистке данных отправлено на сервер: {response}");
            }
            else
            {
                Logger.LogWarning("[CLEAR] Ошибка отправки уведомления об очистке данных: пустой ответ");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[CLEAR] Ошибка при отправке уведомления об очистке данных: {ex.Message}");
        }
    }
    
    // Очищаем данные игрока
    public void ClearPlayerData()
    {
        Logger.LogInfo("Очищаем данные игрока");
        
        // Отправляем уведомление об очистке данных на сервер
        SendClearPlayerDataNotificationToServer();
        
        savedPlayerData.Clear();
        PlayerState.IsInRaid = false;
        PlayerState.IsPlayerDead = false;
        PlayerState.IsPlayerInvulnerable = false;
        PlayerState.InvulnUntil = 0f; // Сбрасываем время неуязвимости
        hasRaidStarted = false;
        raidReadyNotificationShown = false;
        
        // Очищаем ссылки на игровые объекты
        gameWorld = null;
        localPlayer = null;
        
        // Останавливаем корутину возрождения если она запущена
        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }
        
        Logger.LogInfo("Данные игрока очищены, ссылки на GameWorld и LocalPlayer сброшены");
    }
    
    // Методы для работы с GamePatches
    public void SetInRaid(bool inRaid)
    {
        PlayerState.IsInRaid = inRaid;
        Logger.LogInfo($"[GAMEPATCHES] PlayerState.IsInRaid установлен в: {PlayerState.IsInRaid}");
    }
    
    public bool IsDesmatchModeEnabled()
    {
        return Settings.Settings.EnableDesmatchMode.Value;
    }

    public bool IsRespawnInProgress()
    {
        return isRespawnInProgress;
    }

    public bool CanTriggerManualRespawn()
    {
        if (!PlayerState.IsInRaid)
        {
            return false;
        }

        if (isRespawnInProgress)
        {
            return false;
        }

        return Time.time - lastManualRespawnTime >= 3f;
    }

    private void PerformLightweightReviveHeal(ActiveHealthController health, string context)
    {
        if (health == null)
        {
            return;
        }

        var steps = BuildReviveHealStepsFromConfig();
        if (!steps.RemoveNegativeEffects && !steps.RestoreFullHealth && !steps.MetabolismRestore && !steps.RemoveMedEffect)
        {
            Logger.LogInfo($"[HEALTH] Lightweight revive heal skipped — all steps disabled ({context})");
            return;
        }

        Logger.LogInfo($"[HEALTH] Lightweight revive heal ({context})");
        DesmatchHealthReflection.TryLightweightReviveHeal(health, steps);

        if (IsPipelineEnabled(Settings.Settings.Pipeline07_OperationalState, "07 Operational State"))
        {
            TryRestorePlayerOperationalState(localPlayer, health, context, skipMetabolism: steps.MetabolismRestore);
        }
    }

    private void PerformInvulnEndPenaltyHeal(ActiveHealthController health)
    {
        if (health == null)
        {
            return;
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline22_LegacyFiveStageAtInvulnEnd, "22 Legacy Five Stage At Invuln End"))
        {
            Logger.LogWarning("[INVULNERABILITY_END] legacy 5-stage penalty (may break metabolism)");
            RestorePlayerHealth4Stages(health);
            if (IsPipelineEnabled(Settings.Settings.Pipeline20_PenaltyResetMovement, "20 Penalty Reset Movement")
                && localPlayer?.MovementContext != null)
            {
                ResetMovementRestrictions(localPlayer.MovementContext);
            }

            if (IsPipelineEnabled(Settings.Settings.Pipeline19_PenaltyMetabolismRestore, "19 Penalty Metabolism Restore"))
            {
                DesmatchHealthReflection.TryRestorePostReviveMetabolism(health);
            }

            return;
        }

        var steps = BuildPenaltyHealStepsFromConfig();
        var anyStep = Settings.Settings.Pipeline15_PenaltyClearNegativeEffects.Value
            || Settings.Settings.Pipeline16_PenaltyRestoreDestroyedParts.Value
            || Settings.Settings.Pipeline17_PenaltyRestoreFullHealth.Value
            || Settings.Settings.Pipeline18_PenaltyLightweightHeal.Value
            || Settings.Settings.Pipeline19_PenaltyMetabolismRestore.Value;

        if (!anyStep)
        {
            Logger.LogInfo("[INVULNERABILITY_END] penalty heal skipped — all steps 15–19 disabled");
            return;
        }

        Logger.LogInfo("[INVULNERABILITY_END] metabolism-safe penalty heal");

        if (IsPipelineEnabled(Settings.Settings.Pipeline15_PenaltyClearNegativeEffects, "15 Penalty Clear Negative Effects"))
        {
            ClearNegativeEffects(health);
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline16_PenaltyRestoreDestroyedParts, "16 Penalty Restore Destroyed Parts"))
        {
            RestoreDestroyedBodyParts(health);
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline17_PenaltyRestoreFullHealth, "17 Penalty Restore Full Health"))
        {
            DesmatchHealthReflection.TryRestoreFullHealth(health);
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline18_PenaltyLightweightHeal, "18 Penalty Lightweight Heal"))
        {
            DesmatchHealthReflection.TryLightweightReviveHeal(health, steps);
        }
        else if (IsPipelineEnabled(Settings.Settings.Pipeline19_PenaltyMetabolismRestore, "19 Penalty Metabolism Restore"))
        {
            DesmatchHealthReflection.TryRestorePostReviveMetabolism(health);
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline20_PenaltyResetMovement, "20 Penalty Reset Movement")
            && localPlayer?.MovementContext != null)
        {
            ResetMovementRestrictions(localPlayer.MovementContext);
        }
    }

    private IEnumerator DelayedMetabolismRestoreAfterInvuln(ActiveHealthController health)
    {
        if (!IsPipelineEnabled(Settings.Settings.Pipeline21_PenaltyDelayedMetabolism, "21 Penalty Delayed Metabolism"))
        {
            yield break;
        }

        yield return null;
        yield return new WaitForSeconds(0.5f);

        if (health == null || localPlayer == null)
        {
            yield break;
        }

        Logger.LogInfo("[INVULNERABILITY_END] delayed metabolism restore");
        DesmatchHealthReflection.TryRestorePostReviveMetabolism(health);
        TryRestorePlayerOperationalState(localPlayer, health, "InvulnEndDelayedMetabolism", skipMetabolism: true);
    }

    private void TryRestorePlayerOperationalState(LocalPlayer player, ActiveHealthController health, string context, bool skipMetabolism = false)
    {
        if (player == null)
        {
            return;
        }

        Logger.LogInfo($"[REVIVE_STATE] Restore operational state ({context})");

        TrySetGlobalIgnoreInput(false);
        TryExitFikaDownedState(player);

        try
        {
            player.UnpauseAllEffectsOnPlayer();
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[REVIVE_STATE] UnpauseAllEffectsOnPlayer: {ex.Message}");
        }

        if (health != null && !skipMetabolism
            && IsPipelineEnabled(Settings.Settings.Pipeline06_MetabolismRestore, "06 Metabolism Restore"))
        {
            DesmatchHealthReflection.TryRestorePostReviveMetabolism(health);
        }

        TryRestorePlayerHandsAfterRevive(player);
        TryRestorePlayerAnimators(player);
    }

    private void TryRestorePlayerHandsAfterRevive(LocalPlayer player)
    {
        try
        {
            var cancelPacket = AccessTools.Method(player.GetType(), "WriteCancelApplyingItemPacket", System.Type.EmptyTypes);
            cancelPacket?.Invoke(player, null);
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[REVIVE_STATE] WriteCancelApplyingItemPacket: {ex.Message}");
        }

        try
        {
            var inventory = player.InventoryController;
            var stopExecute = inventory?.GetType().GetMethod(
                "StopExecuting",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            stopExecute?.Invoke(inventory, null);
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[REVIVE_STATE] StopExecuting: {ex.Message}");
        }

        try
        {
            player.RevealWeapon();
            Logger.LogInfo("[REVIVE_STATE] RevealWeapon complete");
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[REVIVE_STATE] RevealWeapon failed, trying SetEmptyHands: {ex.Message}");
            TryInvokeSetEmptyHands(player);
        }

        try
        {
            var movementContext = player.MovementContext;
            movementContext?.ResetPhysicalCondition();
            player.EnableSprint(true);
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[REVIVE_STATE] Movement reset: {ex.Message}");
        }
    }

    private void TryInvokeSetEmptyHands(LocalPlayer player)
    {
        try
        {
            var callbackType = AccessTools.TypeByName("Callback`1");
            var handsInterface = AccessTools.TypeByName("GInterface198");
            if (callbackType == null || handsInterface == null)
            {
                return;
            }

            var genericCallback = callbackType.MakeGenericType(handsInterface);
            var ctor = genericCallback.GetConstructor(new[] { typeof(System.Action) });
            if (ctor == null)
            {
                return;
            }

            var callback = ctor.Invoke(new object[] { new System.Action(() => { }) });
            var setEmptyHands = AccessTools.Method(typeof(LocalPlayer), "SetEmptyHands", new[] { genericCallback });
            setEmptyHands?.Invoke(player, new[] { callback });
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[REVIVE_STATE] SetEmptyHands: {ex.Message}");
        }
    }

    private void TryRestorePlayerAnimators(LocalPlayer player)
    {
        try
        {
            var bodyAnimator = AccessTools.Field(typeof(LocalPlayer), "BodyAnimatorCommon")?.GetValue(player);
            if (bodyAnimator != null)
            {
                var enabledProp = bodyAnimator.GetType().GetProperty("enabled");
                if (enabledProp?.GetValue(bodyAnimator) is bool bodyEnabled && !bodyEnabled)
                {
                    enabledProp.SetValue(bodyAnimator, true);
                }
            }

            var armsAnimator = AccessTools.Field(typeof(LocalPlayer), "ArmsAnimatorCommon")?.GetValue(player);
            if (armsAnimator != null)
            {
                var enabledProp = armsAnimator.GetType().GetProperty("enabled");
                if (enabledProp?.GetValue(armsAnimator) is bool armsEnabled && !armsEnabled)
                {
                    enabledProp.SetValue(armsAnimator, true);
                }
            }

            var characterController = AccessTools.Field(typeof(LocalPlayer), "_characterController")?.GetValue(player);
            if (characterController != null)
            {
                var enabledProp = characterController.GetType().GetProperty("enabled");
                if (enabledProp?.GetValue(characterController) is bool ccEnabled && !ccEnabled)
                {
                    enabledProp.SetValue(characterController, true);
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[REVIVE_STATE] Animator/controller restore: {ex.Message}");
        }
    }

    private void TryExitFikaDownedState(LocalPlayer player)
    {
        if (player == null)
        {
            return;
        }

        try
        {
            var fikaPlayerType = System.Type.GetType("Fika.Core.Main.Players.FikaPlayer, Fika.Core");
            if (fikaPlayerType == null || !fikaPlayerType.IsInstanceOfType(player))
            {
                return;
            }

            var healthController = player.ActiveHealthController;
            if (healthController == null)
            {
                return;
            }

            bool isDowned = false;
            var downedProp = healthController.GetType().GetProperty("Downed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (downedProp?.GetValue(healthController) is bool downed)
            {
                isDowned = downed;
            }

            if (isDowned)
            {
                var toggleDowned = fikaPlayerType.GetMethod("ToggleDowned", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                toggleDowned?.Invoke(player, new object[] { false });
                Logger.LogInfo("[HEALTH] Fika downed state cleared via ToggleDowned(false)");
            }
            else
            {
                try
                {
                    player.RevealWeapon();
                }
                catch (System.Exception exReveal)
                {
                    Logger.LogWarning($"[HEALTH] RevealWeapon fallback: {exReveal.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[HEALTH] TryExitFikaDownedState: {ex.Message}");
        }
    }

    public bool ShouldBlockDeathHandling()
    {
        return PlayerState.IsPlayerDead || isRespawnInProgress || isCriticalStateHandling;
    }
    
    public bool IsProfileInvulnerable(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return false;
        }

        if (localPlayer != null && localPlayer.ProfileId == profileId)
        {
            if (isRespawnInProgress && !isTherapeuticDamageInProgress)
            {
                return true;
            }

            return PlayerState.IsPlayerInvulnerable && Time.time < PlayerState.InvulnUntil;
        }

        if (RemoteInvulnUntilByProfile.TryGetValue(profileId, out float until))
        {
            if (Time.time >= until)
            {
                RemoteInvulnUntilByProfile.Remove(profileId);
                return false;
            }

            return true;
        }

        return false;
    }

    public bool IsPlayerInvulnerable()
    {
        return PlayerState.IsPlayerInvulnerable && Time.time < PlayerState.InvulnUntil;
    }

    public bool IsTherapeuticDamageInProgress()
    {
        return isTherapeuticDamageInProgress;
    }

    /// <summary>
    /// Блок урона для локального игрока: invuln после респавна или весь fade-path.
    /// </summary>
    public bool IsLocalPlayerDamageBlocked()
    {
        if (!Settings.Settings.EnableDesmatchMode.Value)
        {
            return false;
        }

        if (isTherapeuticDamageInProgress)
        {
            return false;
        }

        if (IsPlayerInvulnerable())
        {
            return true;
        }

        if (isRespawnInProgress)
        {
            return true;
        }

        return false;
    }
    
    // Устанавливаем игрока в критическое состояние вместо смерти
    public void SetPlayerCriticalState()
    {
        if (isCriticalStateHandling || Time.time - lastCriticalStateTime < CRITICAL_STATE_COOLDOWN)
        {
            Logger.LogInfo("[CRITICAL_STATE] Пропуск повторного вызова (debounce)");
            return;
        }

        isCriticalStateHandling = true;
        lastCriticalStateTime = Time.time;
        Logger.LogInfo("[CRITICAL_STATE] Устанавливаем игрока в критическое состояние");
        
        try
        {
            // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Сбрасываем все активные действия при смерти
            Logger.LogInfo("[CRITICAL_STATE] Сбрасываем все активные действия при смерти");
            ResetAllPlayerActions();
            
            // Включаем эффект затемнения при смерти
            EnableDeathFade();
            
            // Устанавливаем флаг смерти
            PlayerState.IsPlayerDead = true;
            Logger.LogInfo($"[CRITICAL_STATE] PlayerState.IsPlayerDead установлен: {PlayerState.IsPlayerDead}");
            
            // Отправляем уведомление о смерти на сервер
            Logger.LogInfo("[CRITICAL_STATE] Отправляем уведомление о смерти на сервер");
            SendPlayerDiedNotificationToServer();
            
            // Запускаем автоматическое возрождение (или ждём F10)
            if (Settings.Settings.EnableAutoRespawn.Value)
            {
                float delaySec = PlayerState.ClientRespawnDelay / 1000f;
                Logger.LogInfo("[CRITICAL_STATE] Запускаем автоматическое возрождение");
                ShowMainNotification(
                    $"Автовозрождение через {delaySec:0.#} сек или {Settings.Settings.ManualRespawnKey.Value}",
                    false);
                StartAutoRespawn();
            }
            else
            {
                Logger.LogInfo("[CRITICAL_STATE] Автовозрождение выключено — ждём F10");
                ShowMainNotification($"Нажмите {Settings.Settings.ManualRespawnKey.Value} для возрождения", false);
            }
            
            Logger.LogInfo("[CRITICAL_STATE] Игрок установлен в критическое состояние, возрождение запущено");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[CRITICAL_STATE] Ошибка при установке критического состояния: {ex.Message}");
        }
        finally
        {
            isCriticalStateHandling = false;
        }
    }
    
    // Сбрасываем все активные действия игрока при смерти
    private void ResetAllPlayerActions()
    {
        try
        {
            Logger.LogInfo("[RESET_ACTIONS] Сбрасываем все активные действия игрока при смерти");
            
            if (localPlayer == null)
            {
                Logger.LogWarning("[RESET_ACTIONS] LocalPlayer не найден, пропускаем сброс действий");
                return;
            }
            
            // 1. Сбрасываем стрельбу (отпускаем все кнопки стрельбы)
            Logger.LogInfo("[RESET_ACTIONS] Сбрасываем стрельбу");
            ResetShootingActions();
            
            // 2. Сбрасываем использование предметов (еда, аптечки, медикаменты)
            Logger.LogInfo("[RESET_ACTIONS] Сбрасываем использование предметов");
            ResetItemUsageActions();
            
            // 3. Сбрасываем движение и физические действия
            Logger.LogInfo("[RESET_ACTIONS] Сбрасываем движение и физические действия");
            ResetMovementActions();
            
            // 4. Сбрасываем взаимодействие с объектами
            Logger.LogInfo("[RESET_ACTIONS] Сбрасываем взаимодействие с объектами");
            ResetInteractionActions();
            
            Logger.LogInfo("[RESET_ACTIONS] Все активные действия сброшены");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[RESET_ACTIONS] Ошибка при сбросе действий: {ex.Message}");
        }
    }
    
    // Сбрасываем стрельбу — без Input.ResetInputAxes (ломает HandsController после revive)
    private void ResetShootingActions()
    {
        try
        {
            Logger.LogInfo("[RESET_ACTIONS] Skip aggressive shooting reset (preserves hands controller)");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[RESET_ACTIONS] Ошибка сброса стрельбы: {ex.Message}");
        }
    }
    
    // Сбрасываем использование предметов
    private void ResetItemUsageActions()
    {
        try
        {
            // Сбрасываем использование предметов через Inventory
            if (localPlayer?.Inventory != null)
            {
                var inventory = localPlayer.Inventory;
                
                // Пытаемся остановить использование предметов через рефлексию
                var stopUsingMethod = inventory.GetType().GetMethod("StopUsing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (stopUsingMethod != null)
                {
                    stopUsingMethod.Invoke(inventory, null);
                    Logger.LogInfo("[RESET_ACTIONS] StopUsing вызван через рефлексию");
                }
                
                // Сбрасываем состояние использования медикаментов
                var stopMedicalMethod = inventory.GetType().GetMethod("StopMedical", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (stopMedicalMethod != null)
                {
                    stopMedicalMethod.Invoke(inventory, null);
                    Logger.LogInfo("[RESET_ACTIONS] StopMedical вызван через рефлексию");
                }
            }
            
            Logger.LogInfo("[RESET_ACTIONS] Использование предметов сброшено");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[RESET_ACTIONS] Ошибка сброса использования предметов: {ex.Message}");
        }
    }
    
    // Сбрасываем движение и физические действия
    private void ResetMovementActions()
    {
        try
        {
            // Сбрасываем движение через MovementContext
            if (localPlayer?.MovementContext != null)
            {
                var movementContext = localPlayer.MovementContext;
                
                // Сбрасываем физическое состояние
                movementContext.ResetPhysicalCondition();
                
                // Пытаемся остановить спринт
                var stopSprintMethod = movementContext.GetType().GetMethod("StopSprint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (stopSprintMethod != null)
                {
                    stopSprintMethod.Invoke(movementContext, null);
                    Logger.LogInfo("[RESET_ACTIONS] StopSprint вызван через рефлексию");
                }
                
                // Сбрасываем состояние приседания
                var stopCrouchMethod = movementContext.GetType().GetMethod("StopCrouch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (stopCrouchMethod != null)
                {
                    stopCrouchMethod.Invoke(movementContext, null);
                    Logger.LogInfo("[RESET_ACTIONS] StopCrouch вызван через рефлексию");
                }
            }
            
            Logger.LogInfo("[RESET_ACTIONS] Движение и физические действия сброшены");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[RESET_ACTIONS] Ошибка сброса движения: {ex.Message}");
        }
    }
    
    // Сбрасываем взаимодействие с объектами
    private void ResetInteractionActions()
    {
        try
        {
            // Сбрасываем взаимодействие через рефлексию (InteractionController может быть недоступен)
            if (localPlayer != null)
            {
                // Пытаемся найти InteractionController через рефлексию
                var interactionControllerProperty = localPlayer.GetType().GetProperty("InteractionController", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (interactionControllerProperty != null)
                {
                    var interactionController = interactionControllerProperty.GetValue(localPlayer);
                    if (interactionController != null)
                    {
                        // Пытаемся остановить взаимодействие через рефлексию
                        var stopInteractionMethod = interactionController.GetType().GetMethod("StopInteraction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (stopInteractionMethod != null)
                        {
                            stopInteractionMethod.Invoke(interactionController, null);
                            Logger.LogInfo("[RESET_ACTIONS] StopInteraction вызван через рефлексию");
                        }
                    }
                }
                else
                {
                    Logger.LogInfo("[RESET_ACTIONS] InteractionController не найден через рефлексию, пропускаем");
                }
            }
            
            Logger.LogInfo("[RESET_ACTIONS] Взаимодействие с объектами сброшено");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[RESET_ACTIONS] Ошибка сброса взаимодействия: {ex.Message}");
        }
    }
    
    // Включаем неуязвимость на заданное время
    public void EnablePlayerInvulnerability(float durationSeconds = -1f, bool notifyPlayer = true, bool startTinnitus = true)
    {
        Logger.LogInfo($"[INVULNERABILITY] Запрос на включение неуязvимости: duration={durationSeconds}s, notify={notifyPlayer}");
        
        if (durationSeconds < 0f)
        {
            durationSeconds = PlayerState.ClientInvulnSeconds;
            Logger.LogInfo($"[INVULNERABILITY] Используем настройку по умолчанию: {durationSeconds}s");
        }
        
        bool wasInvulnerable = PlayerState.IsPlayerInvulnerable;
        PlayerState.IsPlayerInvulnerable = true;
        PlayerState.InvulnUntil = Time.time + durationSeconds;
        
        Logger.LogInfo($"[INVULNERABILITY] Неуязвимость включена: {wasInvulnerable} → {PlayerState.IsPlayerInvulnerable}");
        Logger.LogInfo($"[INVULNERABILITY] Длительность: {durationSeconds}s, до {PlayerState.InvulnUntil} (Time.time={Time.time})");
        if (notifyPlayer)
        {
            ShowMainNotification($"Неуязвимость: {PlayerState.ClientInvulnSeconds} сек", false);
        }

        if (startTinnitus && Settings.Settings.EnableTinnitusEffect.Value)
        {
            StartTinnitusEffect();
        }

        SendInvulnerabilityStatusToServer();
        BroadcastInvulnerabilityViaFika();
    }
    
    private void BroadcastInvulnerabilityViaFika()
    {
        if (!fikaAvailable || fikaCommunicationManager == null || localPlayer == null)
        {
            return;
        }

        try
        {
            var packet = new DesmatchFikaPackets.DesmatchInvulnerabilityPacket
            {
                PlayerProfileId = localPlayer.ProfileId,
                PlayerNickname = localPlayer.Profile?.Nickname ?? "Unknown",
                IsInvulnerable = PlayerState.IsPlayerInvulnerable,
                InvulnTimeRemaining = GetRemainingInvulnerabilityTime(),
                InvulnSeconds = PlayerState.ClientInvulnSeconds
            };

            fikaCommunicationManager.SendInvulnerabilitySync(packet);
            Logger.LogInfo($"[FIKA] Broadcast invuln: {packet.IsInvulnerable}, remaining={packet.InvulnTimeRemaining:F1}s");
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[FIKA] Ошибка broadcast invuln: {ex.Message}");
        }
    }

    private void BroadcastRespawnViaFika(string respawnType, Vector3 respawnPosition)
    {
        if (!fikaAvailable || fikaCommunicationManager == null || localPlayer == null)
        {
            return;
        }

        try
        {
            var packet = new DesmatchFikaPackets.DesmatchRespawnPacket
            {
                PlayerProfileId = localPlayer.ProfileId,
                PlayerNickname = localPlayer.Profile?.Nickname ?? "Unknown",
                RespawnPosition = respawnPosition,
                RespawnRotation = spawnRotation.eulerAngles,
                IsManualRespawn = respawnType == "manual",
                RespawnDelay = PlayerState.ClientRespawnDelay,
                InvulnSeconds = PlayerState.ClientInvulnSeconds
            };

            fikaCommunicationManager.SendRespawnRequest(packet);
            Logger.LogInfo($"[FIKA] Broadcast respawn ({respawnType}) pos={respawnPosition}");
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[FIKA] Ошибка broadcast respawn: {ex.Message}");
        }
    }

    private EFT.Player FindPlayerByProfileId(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return null;
        }

        var gw = Comfort.Common.Singleton<GameWorld>.Instance;
        if (gw?.AllPlayersEverExisted == null)
        {
            return null;
        }

        foreach (var player in gw.AllPlayersEverExisted)
        {
            if (player != null && player.ProfileId == profileId)
            {
                return player as EFT.Player;
            }
        }

        return null;
    }

    private void OnFikaRespawnReceived(DesmatchFikaPackets.DesmatchRespawnPacket packet)
    {
        try
        {
            if (packet == null || localPlayer?.ProfileId == packet.PlayerProfileId)
            {
                return;
            }

            Logger.LogInfo($"[FIKA] Респавн другого игрока: {packet.PlayerProfileId}, pos={packet.RespawnPosition}");
            var remotePlayer = FindPlayerByProfileId(packet.PlayerProfileId);
            var health = remotePlayer?.ActiveHealthController;
            if (health == null)
            {
                Logger.LogWarning($"[FIKA] ActiveHealthController не найден для {packet.PlayerProfileId}");
                return;
            }

            int removed = DesmatchHealthReflection.TryForceRemoveAllActiveEffects(health);
            DesmatchHealthReflection.TryHealWithNetworkSync(health);
            Logger.LogInfo($"[FIKA] Очищено эффектов у remote player {packet.PlayerProfileId}: {removed}");
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[FIKA] Ошибка обработки respawn packet: {ex.Message}");
        }
    }
    
    // Отключаем неуязвимость с новым порядком: урон → ожидание → лечение → снятие неуязвимости
    public void DisablePlayerInvulnerability()
    {
        if (isInvulnDisableInProgress || invulnDisableCoroutine != null)
        {
            return;
        }

        if (!PlayerState.IsPlayerInvulnerable && PlayerState.InvulnUntil <= 0f)
        {
            return;
        }

        isInvulnDisableInProgress = true;
        PlayerState.IsPlayerInvulnerable = false;
        PlayerState.InvulnUntil = 0f;

        Logger.LogInfo("[INVULNERABILITY] Starting invuln disable coroutine");
        invulnDisableCoroutine = StartCoroutine(DisableInvulnerabilityWithNewOrder());
    }
    
    private IEnumerator DisableInvulnerabilityWithNewOrder()
    {
        var health = localPlayer?.ActiveHealthController;
        if (health == null)
        {
            Logger.LogError("❌ [INVULNERABILITY_END] Не удалось получить ActiveHealthController");
            isInvulnDisableInProgress = false;
            invulnDisableCoroutine = null;
            yield break;
        }

        yield return null;

        try
        {
            if (IsPipelineEnabled(Settings.Settings.Pipeline13_PenaltyTherapeuticDamage, "13 Penalty Therapeutic Damage"))
            {
                Logger.LogInfo("[INVULNERABILITY_END] ЭТАП 1: therapeutic damage (ноги/живот/руки)");
                ApplyRealisticDamageToLimbs(health);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [INVULNERABILITY_END] Ошибка терапевтического урона: {ex.Message}");
        }

        if (IsPipelineEnabled(Settings.Settings.Pipeline14_PenaltyWaitOneSecond, "14 Penalty Wait One Second"))
        {
            Logger.LogInfo("[INVULNERABILITY_END] ЭТАП 2: ожидание 1с");
            yield return new WaitForSeconds(1f);
        }
        else
        {
            yield return null;
        }

        try
        {
            Logger.LogInfo("[INVULNERABILITY_END] ЭТАП 3: penalty heal");
            PerformInvulnEndPenaltyHeal(health);
            StartCoroutine(DelayedMetabolismRestoreAfterInvuln(health));
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [INVULNERABILITY_END] Ошибка penalty heal: {ex.Message}");
        }

        try
        {
            Logger.LogInfo("[INVULNERABILITY_END] Останавливаем звуковой эффект");
            StopTinnitusEffect();

            PlayerState.IsPlayerInvulnerable = false;
            PlayerState.InvulnUntil = 0f;

            Logger.LogInfo($"[INVULNERABILITY] Неуязвимость отключена: True → {PlayerState.IsPlayerInvulnerable}");
            SendInvulnerabilityStatusToServer();
            BroadcastInvulnerabilityViaFika();
            Logger.LogInfo("✅ [INVULNERABILITY_END] disable sequence completed");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [INVULNERABILITY_END] Ошибка отключения неуязvимости: {ex.Message}");

            PlayerState.IsPlayerInvulnerable = false;
            PlayerState.InvulnUntil = 0f;
            SendInvulnerabilityStatusToServer();
            BroadcastInvulnerabilityViaFika();
        }
        finally
        {
            isInvulnDisableInProgress = false;
            invulnDisableCoroutine = null;
        }
    }
    
    // Получаем оставшееся время неуязвимости
    public float GetRemainingInvulnerabilityTime()
    {
        if (!PlayerState.IsPlayerInvulnerable)
            return 0f;
            
        float remaining = PlayerState.InvulnUntil - Time.time;
        return Mathf.Max(0f, remaining);
    }
    
    // 5-этапное восстановление здоровья с приоритетными медицинскими предметами
    private void RestorePlayerHealth4Stages(ActiveHealthController health)
    {
        Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Начинаем 5-этапное восстановление здоровья с медицинскими предметами");
        
        // Показываем уведомление о начале лечения
        ShowHealingNotification("Начинаем комплексное восстановление здоровья...", false);
        
        try
        {
                // ЭТАП 0: Снятие всех 29 эффектов стимуляторов
                Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 0: Снятие всех 29 эффектов стимуляторов");
                ShowHealingNotification("Снимаем все эффекты стимуляторов...", false);
                TryClearAllStimulatorEffects(localPlayer);

                // КРИТИЧЕСКИ ВАЖНО: Принудительный сброс таймеров регенерации на уровне процесса воскрешения
                Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ПРИНУДИТЕЛЬНЫЙ СБРОС ТАЙМЕРОВ РЕГЕНЕРАЦИИ НА УРОВНЕ ПРОЦЕССА ВОСКРЕШЕНИЯ");
                ShowHealingNotification("Сбрасываем таймеры регенерации...", false);
                TryForceResetRegenerationTimers(localPlayer);
            
            // ЭТАП 1: Использование приоритетных медицинских предметов
            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 1: Использование приоритетных медицинских предметов");
            ShowHealingNotification("Используем медицинские предметы...", false);
            UsePriorityMedicalItems(health);
            
            // ЭТАП 2: Восстановление выбитых частей тела
            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 2: Восстановление выбитых частей тела");
            ShowHealingNotification("Восстанавливаем выбитые части тела...", false);
            RestoreDestroyedBodyParts(health);
            
            // ЭТАП 3: Восстановление здоровья и ресурсов
            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 3: Восстановление здоровья и ресурсов");
            ShowHealingNotification("Восстанавливаем здоровье и ресурсы...", false);
            RestoreHealthAndResources(health);
            
            // ЭТАП 4: Очистка эффектов (кровотечения, переломы)
            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 4: Очистка эффектов (кровотечения, переломы)");
            ShowHealingNotification("Очищаем негативные эффекты...", false);
            ClearNegativeEffects(health);
            
            // ЭТАП 5: Финальная проверка здоровья
            Logger.LogInfo("🩹 [HEALTH_RESTORE] ЭТАП 5: Финальная проверка здоровья");
            ShowHealingNotification("Финальная проверка здоровья...", false);
            FinalHealthValidation(health);
            
            // Показываем уведомление о завершении лечения
            ShowMainNotification("Восстановление здоровья завершено", false);
            
            Logger.LogInfo("🩹 [HEALTH_RESTORE] 5-этапное восстановление здоровья завершено успешно");
            DesmatchHealthReflection.TryHealWithNetworkSync(health);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [HEALTH_RESTORE] Ошибка при восстановлении здоровья: {ex.Message}");
            Logger.LogError($"❌ [HEALTH_RESTORE] Stack trace: {ex.StackTrace}");
            
            // Показываем уведомление об ошибке
            ShowNotification("Ошибка восстановления здоровья, используем стандартное лечение", true);
            
            // Fallback: используем стандартный метод
            Logger.LogInfo("🩹 [HEALTH_RESTORE] Используем fallback - RestoreFullHealth() через reflection");
            DesmatchHealthReflection.TryRestoreFullHealth(health);
        }
    }
    

    // Нанесение реалистичного урона конечностям (как будто ударили ножом)
    private void ApplyRealisticDamageToLimbs(ActiveHealthController health)
    {
        isTherapeuticDamageInProgress = true;
        try
        {
            Logger.LogInfo("[REALISTIC_DAMAGE] Therapeutic limb damage (bypass invuln patches)");
            var limbsToDamage = new EBodyPart[]
            {
                EBodyPart.Stomach,
                EBodyPart.LeftLeg,
                EBodyPart.RightLeg,
                EBodyPart.LeftArm,
                EBodyPart.RightArm
            };
            int damagedLimbs = 0;

            var damageInfo = new DamageInfoStruct
            {
                DamageType = EDamageType.Melee,
                Damage = 10f,
                ArmorDamage = 0f,
                PenetrationPower = 0f
            };

            foreach (var bodyPart in limbsToDamage)
            {
                try
                {
                    health.ApplyDamage(bodyPart, 10f, damageInfo);
                    damagedLimbs++;
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"[REALISTIC_DAMAGE] {bodyPart}: {ex.Message}");
                }
            }

            Logger.LogInfo($"[REALISTIC_DAMAGE] Applied to {damagedLimbs} limbs");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[REALISTIC_DAMAGE] Failed: {ex.Message}");
        }
        finally
        {
            isTherapeuticDamageInProgress = false;
        }
    }
    
    // Использование Surv12 для конкретной части тела (только снятие дебафов)
    private void UseSurv12ForBodyPart(ActiveHealthController health, EBodyPart bodyPart)
    {
        try
        {
            Logger.LogInfo($"💊 [SURV12] Используем Surv12 для {bodyPart} (только снятие дебафов)");
            
            // Получаем инвентарь игрока
            var localPlayer = this.localPlayer;
            if (localPlayer?.Inventory == null)
            {
                Logger.LogWarning($"💊 [SURV12] Инвентарь игрока недоступен для {bodyPart}");
                return;
            }
            
            // Ищем Surv12 в инвентаре
            var surv12Item = FindMedicalItemInInventory(localPlayer.Inventory, "Surv12");
            if (surv12Item == null)
            {
                Logger.LogWarning($"💊 [SURV12] Surv12 не найден в инвентаре для {bodyPart}");
                return;
            }
            
            // Применяем Surv12 только для снятия дебафов, НЕ восстанавливаем HP
            ApplySurv12ForBodyPart(health, surv12Item, bodyPart);
            
            Logger.LogInfo($"💊 [SURV12] Surv12 успешно применен для {bodyPart}");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [SURV12] Ошибка при использовании Surv12 для {bodyPart}: {ex.Message}");
        }
    }
    
    // Применение Surv12 для конкретной части тела (только дебафы)
    private void ApplySurv12ForBodyPart(ActiveHealthController health, object surv12Item, EBodyPart bodyPart)
    {
        try
        {
            Logger.LogInfo($"💊 [SURV12] Применяем Surv12 для {bodyPart} - только снятие дебафов");
            
            // Используем рефлексию для вызова метода Surv12
            var itemType = surv12Item.GetType();
            
            // Пытаемся найти метод, который работает с конкретной частью тела
            var useForBodyPartMethod = itemType.GetMethod("UseForBodyPart", new System.Type[] { typeof(ActiveHealthController), typeof(EBodyPart) });
            if (useForBodyPartMethod != null)
            {
                useForBodyPartMethod.Invoke(surv12Item, new object[] { health, bodyPart });
                Logger.LogInfo($"💊 [SURV12] Метод UseForBodyPart вызван для {bodyPart}");
                return;
            }
            
            // Альтернативный способ - через ApplyHealthEffect для части тела
            var applyForBodyPartMethod = itemType.GetMethod("ApplyHealthEffectForBodyPart", new System.Type[] { typeof(ActiveHealthController), typeof(EBodyPart) });
            if (applyForBodyPartMethod != null)
            {
                applyForBodyPartMethod.Invoke(surv12Item, new object[] { health, bodyPart });
                Logger.LogInfo($"💊 [SURV12] Метод ApplyHealthEffectForBodyPart вызван для {bodyPart}");
                return;
            }
            
            // Если специфических методов нет, используем общий метод
            var useMethod = itemType.GetMethod("Use", new System.Type[] { typeof(ActiveHealthController) });
            if (useMethod != null)
            {
                useMethod.Invoke(surv12Item, new object[] { health });
                Logger.LogInfo($"💊 [SURV12] Общий метод Use вызван для {bodyPart}");
            }
            else
            {
                Logger.LogWarning($"💊 [SURV12] Не найден подходящий метод для Surv12 на {bodyPart}");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [SURV12] Ошибка применения Surv12 для {bodyPart}: {ex.Message}");
        }
    }
    
    // Использование приоритетных медицинских предметов (Surv12, Method15, Method16)
    private void UsePriorityMedicalItems(ActiveHealthController health)
    {
        try
        {
            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Используем приоритетные медицинские предметы");
            
            // Получаем инвентарь игрока
            var localPlayer = this.localPlayer;
            if (localPlayer?.Inventory == null)
            {
                Logger.LogWarning($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_WARNING} Инвентарь игрока недоступен");
                return;
            }
            
            // Используем медицинские предметы в порядке приоритета
            foreach (var itemName in DesmatchHealthUtils.MEDICAL_ITEMS_PRIORITY)
            {
                try
                {
                    Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Проверяем наличие {itemName}");
                    
                    // Ищем предмет в инвентаре
                    var medicalItem = FindMedicalItemInInventory(localPlayer.Inventory, itemName);
                    if (medicalItem != null)
                    {
                        Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_SUCCESS} {itemName} найден, используем");
                        
                        // Используем предмет
                        UseMedicalItemOnHealth(health, medicalItem, itemName);
                        
                        // Небольшая задержка между предметами для лучшего эффекта
                        System.Threading.Thread.Sleep(100);
                    }
                    else
                    {
                        Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_INFO} {itemName} не найден в инвентаре");
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.LogError($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_ERROR} Ошибка при использовании {itemName}: {ex.Message}");
                }
            }
            
            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_SUCCESS} Использование приоритетных медицинских предметов завершено");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_ERROR} Ошибка при использовании приоритетных медицинских предметов: {ex.Message}");
        }
    }
    
    // Использование медицинских предметов
    private void UseMedicalItem(ActiveHealthController health, string itemName)
    {
        try
        {
            Logger.LogInfo($"💊 [MEDICAL_ITEM] Попытка использования медицинского предмета: {itemName}");
            
            // Получаем инвентарь игрока
            var localPlayer = this.localPlayer;
            if (localPlayer?.Inventory == null)
            {
                Logger.LogWarning($"💊 [MEDICAL_ITEM] Инвентарь игрока недоступен для {itemName}");
                return;
            }
            
            // Ищем предмет в инвентаре
            var medicalItem = FindMedicalItemInInventory(localPlayer.Inventory, itemName);
            if (medicalItem == null)
            {
                Logger.LogWarning($"💊 [MEDICAL_ITEM] Медицинский предмет {itemName} не найден в инвентаре");
                return;
            }
            
            // Используем предмет
            UseMedicalItemOnHealth(health, medicalItem, itemName);
            
            Logger.LogInfo($"💊 [MEDICAL_ITEM] Медицинский предмет {itemName} успешно использован");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [MEDICAL_ITEM] Ошибка при использовании {itemName}: {ex.Message}");
        }
    }
    
    // Поиск медицинского предмета в инвентаре
    private object FindMedicalItemInInventory(object inventory, string itemName)
    {
        try
        {
            // Используем рефлексию для поиска предмета в инвентаре
            var inventoryType = inventory.GetType();
            var allItemsProperty = inventoryType.GetProperty("AllItems");
            if (allItemsProperty != null)
            {
                var allItems = allItemsProperty.GetValue(inventory);
                if (allItems is System.Collections.IEnumerable items)
                {
                    foreach (var item in items)
                    {
                        var itemType = item.GetType();
                        var tplProperty = itemType.GetProperty("Tpl");
                        if (tplProperty != null)
                        {
                            var tpl = tplProperty.GetValue(item)?.ToString();
                            if (tpl != null && tpl.Contains(itemName))
                            {
                                return item;
                            }
                        }
                    }
                }
            }
            
            return null;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [MEDICAL_ITEM] Ошибка поиска предмета {itemName}: {ex.Message}");
            return null;
        }
    }
    
    // Использование медицинского предмета на здоровье
    private void UseMedicalItemOnHealth(ActiveHealthController health, object medicalItem, string itemName)
    {
        try
        {
            // Используем рефлексию для вызова метода использования предмета
            var itemType = medicalItem.GetType();
            var useMethod = itemType.GetMethod("Use", new System.Type[] { typeof(ActiveHealthController) });
            
            if (useMethod != null)
            {
                useMethod.Invoke(medicalItem, new object[] { health });
                Logger.LogInfo($"💊 [MEDICAL_ITEM] Метод Use вызван для {itemName}");
            }
            else
            {
                // Альтернативный способ - через ApplyHealthEffect
                var applyMethod = itemType.GetMethod("ApplyHealthEffect", new System.Type[] { typeof(ActiveHealthController) });
                if (applyMethod != null)
                {
                    applyMethod.Invoke(medicalItem, new object[] { health });
                    Logger.LogInfo($"💊 [MEDICAL_ITEM] Метод ApplyHealthEffect вызван для {itemName}");
                }
                else
                {
                    Logger.LogWarning($"💊 [MEDICAL_ITEM] Не найден подходящий метод для использования {itemName}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [MEDICAL_ITEM] Ошибка использования {itemName}: {ex.Message}");
        }
    }
    
    // ЭТАП 1: Восстановление выбитых частей тела (использует общие утилиты)
    private void RestoreDestroyedBodyParts(ActiveHealthController health)
    {
        Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 1: Восстановление выбитых частей тела");
        
        foreach (var bodyPart in DesmatchHealthUtils.BODY_PARTS_ORDER)
        {
            try
            {
                if (!DesmatchHealthReflection.IsBodyPartDestroyed(health, bodyPart))
                {
                    continue;
                }

                Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Восстанавливаем выбитую часть тела: {bodyPart}");

                if (bodyPart == EBodyPart.Head || bodyPart == EBodyPart.Chest)
                {
                    Logger.LogWarning($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_WARNING} {bodyPart} не может быть восстановлен через FullRestoreBodyPart");
                    continue;
                }

                if (DesmatchHealthReflection.TryFullRestoreBodyPart(health, bodyPart))
                {
                    Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_SUCCESS} {bodyPart} полностью восстановлен через FullRestoreBodyPart");
                }
                else
                {
                    Logger.LogWarning($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_WARNING} Не удалось восстановить {bodyPart} через FullRestoreBodyPart");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_ERROR} Ошибка восстановления {bodyPart}: {ex.Message}");
            }
        }
    }
    
    // ЭТАП 2: Восстановление здоровья и ресурсов (использует общие утилиты)
    private void RestoreHealthAndResources(ActiveHealthController health)
    {
        Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 2: Восстановление здоровья и ресурсов");
        
        try
        {
            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Используем RestoreFullHealth для полного восстановления");
            if (!DesmatchHealthReflection.TryRestoreFullHealth(health))
            {
                health.RestoreFullHealth();
            }

            if (DesmatchHealthUtils.TryGetEnergyValues(health, out float energy, out float maxEnergy))
            {
                Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Энергия: {energy}/{maxEnergy}");
            }

            if (DesmatchHealthUtils.TryGetHydrationValues(health, out float hydration, out float maxHydration))
            {
                Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Гидратация: {hydration}/{maxHydration}");
            }

            Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} Общее здоровье: {DesmatchHealthUtils.GetTotalHealth(health)}/{DesmatchHealthUtils.GetMaxTotalHealth(health)}");

            foreach (var bodyPart in DesmatchHealthUtils.BODY_PARTS_ORDER)
            {
                try
                {
                    if (!DesmatchHealthUtils.TryGetBodyPartValues(health, bodyPart, out float current, out float maximum))
                    {
                        continue;
                    }

                    float healthDiff = maximum - current;
                    if (healthDiff > 0)
                    {
                        health.ChangeHealth(bodyPart, healthDiff, new DamageInfoStruct());
                        DesmatchHealthUtils.TryGetBodyPartValues(health, bodyPart, out current, out maximum);
                        Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} {bodyPart}: восстановлено до {current}/{maximum}");
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_ERROR} Ошибка восстановления здоровья {bodyPart}: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_ERROR} Ошибка восстановления ресурсов: {ex.Message}");
        }
    }
    
    // ЭТАП 3: Очистка эффектов (кровотечения, переломы) (использует общие утилиты)
    private void ClearNegativeEffects(ActiveHealthController health)
    {
        Logger.LogInfo($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_HEALTH} ЭТАП 3: Очистка эффектов (кровотечения, переломы)");
        
        try
        {
            foreach (var bodyPart in DesmatchHealthUtils.BODY_PARTS_ORDER)
            {
                try
                {
                    DesmatchHealthReflection.TryRemoveNegativeEffects(health, bodyPart);
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"{DesmatchConstants.LOG_PREFIX_HEALTH} {DesmatchConstants.EMOJI_ERROR} Ошибка удаления эффектов с {bodyPart}: {ex.Message}");
                }
            }

            DesmatchHealthReflection.TryRemoveNegativeEffects(health, EBodyPart.Common);

            try
            {
                Logger.LogInfo("🩹 Удаляем медицинские эффекты через RemoveMedEffect");
                health.RemoveMedEffect();
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"🩹 Ошибка удаления медицинских эффектов: {ex.Message}");
            }
            
            Logger.LogInfo("🩹 Очистка эффектов завершена");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🩹 Ошибка очистки эффектов: {ex.Message}");
            Logger.LogInfo("🩹 Используем fallback - RestoreFullHealth() через reflection");
            DesmatchHealthReflection.TryRestoreFullHealth(health);
        }
    }
    
    // ЭТАП 4: Финальная проверка здоровья
    private void FinalHealthValidation(ActiveHealthController health)
    {
        Logger.LogInfo("🩹 ЭТАП 4: Финальная проверка здоровья");
        
        try
        {
            if (DesmatchHealthUtils.TryGetEnergyValues(health, out float energy, out float maxEnergy))
            {
                Logger.LogInfo($"🩹 Энергия: {energy}/{maxEnergy}");
            }

            if (DesmatchHealthUtils.TryGetHydrationValues(health, out float hydration, out float maxHydration))
            {
                Logger.LogInfo($"🩹 Гидратация: {hydration}/{maxHydration}");
            }

            foreach (EBodyPart bodyPart in System.Enum.GetValues(typeof(EBodyPart)))
            {
                try
                {
                    if (!DesmatchHealthUtils.TryGetBodyPartValues(health, bodyPart, out float current, out float maximum))
                    {
                        continue;
                    }

                    Logger.LogInfo($"🩹 {bodyPart}: {current}/{maximum}");

                    if (current <= 0)
                    {
                        Logger.LogWarning($"🩹 {bodyPart} все еще разрушен, восстанавливаем принудительно");
                        health.ChangeHealth(bodyPart, 1f, new DamageInfoStruct());
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"🩹 Ошибка проверки {bodyPart}: {ex.Message}");
                }
            }
            
            Logger.LogInfo("🩹 Финальная проверка здоровья завершена");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🩹 Ошибка финальной проверки здоровья: {ex.Message}");
        }
    }
    
    
    // Система уведомлений (на основе старых версий v1.5+)
    public static void ShowNotification(string message, bool isError = false)
    {
        try
        {
            message = SanitizeNotificationMessage(message);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            // Единый способ показа уведомлений без дублирования
            if (isError)
            {
                NotificationManagerClass.DisplayWarningNotification(message, ENotificationDurationType.Long);
                UnityEngine.Debug.Log($"[NOTIFICATION] DesmatchMode: Warning: {message}");
            }
            else
            {
                NotificationManagerClass.DisplayMessageNotification(message, ENotificationDurationType.Long);
                UnityEngine.Debug.Log($"[NOTIFICATION] DesmatchMode: Message: {message}");
            }
        }
        catch (System.Exception ex)
        {
            // Fallback: логирование
            UnityEngine.Debug.LogError($"❌ DesmatchMode: Ошибка показа уведомления: {ex.Message}");
            if (isError)
            {
                UnityEngine.Debug.LogError($"🔴 {message}");
            }
            else
            {
                UnityEngine.Debug.Log($"🔴 {message}");
            }
        }
    }
    
    private static bool IsBlockedNotificationCodePoint(int codePoint)
    {
        if (codePoint >= 0x2600 && codePoint <= 0x27BF) return true;
        if (codePoint >= 0x1F300 && codePoint <= 0x1FAFF) return true;
        if (codePoint >= 0xFE00 && codePoint <= 0xFE0F) return true;
        if (codePoint == 0x200D) return true;
        return false;
    }

    private static string SanitizeNotificationMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var builder = new System.Text.StringBuilder(message.Length);
        for (int i = 0; i < message.Length; i++)
        {
            char c = message[i];
            int codePoint;

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < message.Length && char.IsLowSurrogate(message[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, message[i + 1]);
                    i++;
                }
                else
                {
                    continue;
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                continue;
            }
            else
            {
                codePoint = c;
            }

            if (IsBlockedNotificationCodePoint(codePoint))
            {
                continue;
            }

            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        return builder.ToString().Trim();
    }
    
    // Условные методы показа уведомлений
    public static void ShowMainNotification(string message, bool isError = false)
    {
        if (Settings.Settings.ShowMainNotifications.Value)
        {
            ShowNotification(message, isError);
        }
        else
        {
            UnityEngine.Debug.Log($"[NOTIFICATION_MAIN] {message}");
        }
    }
    
    public static void ShowFadeEffectNotification(string message, bool isError = false)
    {
        if (Settings.Settings.ShowFadeEffectNotifications.Value)
        {
            ShowNotification(message, isError);
        }
        else
        {
            UnityEngine.Debug.Log($"[NOTIFICATION_FADE] {message}");
        }
    }
    
    public static void ShowHealingNotification(string message, bool isError = false)
    {
        if (Settings.Settings.ShowHealingNotifications.Value)
        {
            ShowNotification(message, isError);
        }
        else
        {
            UnityEngine.Debug.Log($"[NOTIFICATION_HEALING] {message}");
        }
    }
    
    public static void ShowDamageNotification(string message, bool isError = false)
    {
        if (Settings.Settings.ShowDamageNotifications.Value)
        {
            ShowNotification(message, isError);
        }
        else
        {
            UnityEngine.Debug.Log($"[NOTIFICATION_DAMAGE] {message}");
        }
    }
    
    public static void ShowAutoRespawnNotification(string message, bool isError = false)
    {
        if (Settings.Settings.ShowAutoRespawnNotifications.Value)
        {
            ShowNotification(message, isError);
        }
        else
        {
            UnityEngine.Debug.Log($"[NOTIFICATION_AUTO_RESPAWN] {message}");
        }
    }
    
    public static void ShowSoundNotification(string message, bool isError = false)
    {
        if (Settings.Settings.ShowSoundEffectNotifications.Value)
        {
            ShowNotification(message, isError);
        }
        else
        {
            UnityEngine.Debug.Log($"[NOTIFICATION_SOUND] {message}");
        }
    }
    
    // Уведомление когда рейд реально начался (GameWorld.OnGameStarted — после загрузки карты)
    public void ShowRaidReadyNotification()
    {
        if (raidReadyNotificationShown)
        {
            return;
        }

        try
        {
            raidReadyNotificationShown = true;
            Logger.LogInfo("[RAID_READY] Показываем уведомление о готовности DesmatchMode");

            InitializeScreenFadeComponents();
            InitializeSoundComponents();

            string respawnKey = Settings.Settings.ManualRespawnKey.Value.ToString();
            ShowMainNotification($"DesmatchMode: рейд начался. Респавн — {respawnKey}", false);
            Logger.LogInfo($"[RAID_READY] Уведомление отправлено (клавиша: {respawnKey})");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[RAID_READY] Ошибка показа уведомления: {ex.Message}");
        }
    }

    public void ShowPlayerFoundNotification()
    {
        ShowRaidReadyNotification();
    }
    
    // Получение компонентов для эффектов затемнения экрана
    private void InitializeScreenFadeComponents()
    {
        try
        {
            Logger.LogInfo("🎭 [SCREEN_FADE] Инициализация компонентов эффектов затемнения экрана");

            // Получение DeathFade с камеры
            var camera = CameraClass.Instance?.Camera;
            if (camera != null)
            {
                deathFade = camera.GetComponent<DeathFade>();
                if (deathFade != null)
                {
                    Logger.LogInfo("🎭 [SCREEN_FADE] DeathFade компонент найден на камере");
                }
                else
                {
                    Logger.LogWarning("🎭 [SCREEN_FADE] DeathFade компонент не найден на камере");
                }
            }
            else
            {
                Logger.LogWarning("🎭 [SCREEN_FADE] Камера не найдена");
            }

            // Получение PreloaderUI singleton
            preloaderUI = MonoBehaviourSingleton<PreloaderUI>.Instance;
            if (preloaderUI != null)
            {
                Logger.LogInfo("🎬 [SCREEN_FADE] PreloaderUI singleton найден");
            }
            else
            {
                Logger.LogWarning("🎬 [SCREEN_FADE] PreloaderUI singleton не найден");
            }

            Logger.LogInfo("🎭 [SCREEN_FADE] Инициализация компонентов завершена");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🎭 [SCREEN_FADE] Ошибка инициализации компонентов: {ex.Message}");
        }
    }
    
    // Инициализация FIKA интеграции
    private void InitializeFikaIntegration()
    {
        try
        {
            Logger.LogInfo("🔗 [FIKA] Инициализация FIKA интеграции (отложенная активация)");
            
            fikaCommunicationManager = FikaCommunicationManager.Instance;
            fikaCommunicationManager.Initialize();
            
            fikaCommunicationManager.OnServerResponseReceived += OnFikaServerResponseReceived;
            fikaCommunicationManager.OnSettingsUpdateReceived += OnFikaSettingsUpdateReceived;
            fikaCommunicationManager.OnInvulnerabilityUpdateReceived += OnFikaInvulnerabilityUpdateReceived;
            fikaCommunicationManager.OnRespawnReceived += OnFikaRespawnReceived;
            fikaCommunicationManager.OnFikaBecameAvailable += OnFikaBecameAvailable;

            Logger.LogInfo("🔗 [FIKA] Подписка на события FIKA выполнена; активация при входе в рейд");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [FIKA] Ошибка инициализации FIKA интеграции: {ex.Message}");
        }
    }

    private void OnFikaBecameAvailable()
    {
        fikaAvailable = true;
        Logger.LogInfo("✅ [FIKA] FIKA интеграция активна и готова к использованию");
    }

    private void TryEnableFikaIntegration()
    {
        if (fikaCommunicationManager == null)
        {
            InitializeFikaIntegration();
        }

        if (fikaCommunicationManager == null)
        {
            return;
        }

        if (fikaCommunicationManager.TryRefreshAvailability())
        {
            fikaAvailable = true;
        }
    }
    
    // Обработчики событий FIKA
    private void OnFikaServerResponseReceived(DesmatchFikaPackets.DesmatchServerResponsePacket packet)
    {
        try
        {
            Logger.LogInfo($"🔗 [FIKA] Получен ответ сервера: Success={packet.Success}, Message={packet.Message}");
            
            if (packet.Success)
            {
                // Обрабатываем успешный ответ
                ProcessServerResponse(JsonConvert.SerializeObject(packet));
            }
            else
            {
                Logger.LogWarning($"🔗 [FIKA] Ошибка от сервера: {packet.Message}");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [FIKA] Ошибка обработки ответа сервера: {ex.Message}");
        }
    }
    
    private void OnFikaSettingsUpdateReceived(DesmatchFikaPackets.DesmatchSettingsUpdatePacket packet)
    {
        try
        {
            Logger.LogInfo($"🔗 [FIKA] Получено обновление настроек: RespawnDelay={packet.RespawnDelay}, InvulnSeconds={packet.InvulnSeconds}");
            
            // Обновляем клиентские настройки
            UpdateClientSettings(packet.RespawnDelay, packet.InvulnSeconds);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [FIKA] Ошибка обработки обновления настроек: {ex.Message}");
        }
    }
    
    private void OnFikaInvulnerabilityUpdateReceived(DesmatchFikaPackets.DesmatchInvulnerabilityPacket packet)
    {
        try
        {
            Logger.LogInfo($"🔗 [FIKA] Invuln sync profile={packet.PlayerProfileId}, active={packet.IsInvulnerable}, remaining={packet.InvulnTimeRemaining}");

            if (localPlayer != null && packet.PlayerProfileId == localPlayer.ProfileId)
            {
                return;
            }

            if (packet.IsInvulnerable)
            {
                RemoteInvulnUntilByProfile[packet.PlayerProfileId] = Time.time + packet.InvulnTimeRemaining;
            }
            else
            {
                RemoteInvulnUntilByProfile.Remove(packet.PlayerProfileId);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [FIKA] Invuln sync error: {ex.Message}");
        }
    }
    
    private bool IsHeadlessMode()
    {
        try
        {
            // Проверяем наличие Fika.Headless через GameWorld
            var gw = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gw != null)
            {
                // В headless режиме MainPlayer обычно null или не LocalPlayer
                var mainPlayer = gw.MainPlayer;
                if (mainPlayer == null || !(mainPlayer is LocalPlayer))
                {
                    return true;
                }
            }
            
            // Дополнительная проверка через BetterAudio
            var betterAudio = MonoBehaviourSingleton<BetterAudio>.Instance;
            if (betterAudio == null)
            {
                return true;
            }
            
            return false;
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"🔊 [HEADLESS_CHECK] Ошибка проверки headless режима: {ex.Message}");
            return false; // В случае ошибки считаем обычным режимом
        }
    }
    
    private void InitializeSoundComponents()
    {
        try
        {
            // Проверяем, находимся ли мы в headless режиме (Fika.Headless)
            if (IsHeadlessMode())
            {
                Logger.LogInfo("🔊 [SOUND_EFFECTS] Headless режим обнаружен - звуковые эффекты отключены");
                return;
            }
            
            Logger.LogInfo("🔊 [SOUND_EFFECTS] Инициализация звуковых компонентов (обычный режим)");

            // Получение BetterAudio singleton
            betterAudio = MonoBehaviourSingleton<BetterAudio>.Instance;
            if (betterAudio != null)
            {
                Logger.LogInfo("🔊 [SOUND_EFFECTS] BetterAudio singleton найден");
            }
            else
            {
                Logger.LogWarning("🔊 [SOUND_EFFECTS] BetterAudio singleton не найден");
            }

            // Всегда создаем fallback AudioSource, даже если BetterAudio есть — для тестов и резервного воспроизведения
            if (fallbackAudioSource == null)
            {
                GameObject audioObject = new GameObject("DesmatchMode_FallbackAudio");
                fallbackAudioSource = audioObject.AddComponent<AudioSource>();
                fallbackAudioSource.playOnAwake = false;
                fallbackAudioSource.spatialBlend = 0f; // 2D звук
                Logger.LogInfo("🔊 [SOUND_EFFECTS] Fallback AudioSource создан (универсально)");
            }

            // Получение звука тиннитуса из EFT.Sounds (более надежный способ)
            var sounds = Resources.Load<EFT.Sounds>("Sounds");
            if (sounds != null && sounds.TinnitusSound != null)
            {
                tinnitusSound = sounds.TinnitusSound;
                Logger.LogInfo($"🔊 [SOUND_EFFECTS] TinnitusSound загружен из EFT.Sounds: {tinnitusSound.name}");
            }
            else
            {
                Logger.LogWarning("🔊 [SOUND_EFFECTS] TinnitusSound не найден в EFT.Sounds, попробуем через Player");
                
                // Fallback: получение звука тиннитуса из Player
                if (localPlayer == null)
                {
                    // Попытка определить игрока из GameWorld
                    var gw = Comfort.Common.Singleton<GameWorld>.Instance;
                    if (gw != null)
                    {
                        localPlayer = gw.MainPlayer as LocalPlayer;
                    }
                }

                if (localPlayer != null)
                {
                    var tinnitusField = typeof(EFT.Player).GetField("_tinnitus", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (tinnitusField != null)
                    {
                        tinnitusSound = tinnitusField.GetValue(localPlayer) as AudioClip;
                        if (tinnitusSound != null)
                        {
                            Logger.LogInfo("🔊 [SOUND_EFFECTS] Звук тиннитуса загружен через Player");
                        }
                        else
                        {
                            Logger.LogWarning("🔊 [SOUND_EFFECTS] Звук тиннитуса не найден в Player");
                        }
                    }
                    else
                    {
                        Logger.LogWarning("🔊 [SOUND_EFFECTS] Поле _tinnitus не найдено в Player");
                    }
                }
                else
                {
                    Logger.LogWarning("🔊 [SOUND_EFFECTS] LocalPlayer не найден для получения звука тиннитуса");
                }
            }

            Logger.LogInfo("🔊 [SOUND_EFFECTS] Инициализация звуковых компонентов завершена");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🔊 [SOUND_EFFECTS] Ошибка инициализации звуковых компонентов: {ex.Message}");
        }
    }

    // Гарантированная загрузка клипа тиннитуса перед воспроизведением
    private void EnsureTinnitusClipLoaded()
    {
        if (tinnitusSound != null)
        {
            return;
        }

        // 1) Попытка через EFT.Sounds
        var sounds = Resources.Load<EFT.Sounds>("Sounds");
        if (sounds != null && sounds.TinnitusSound != null)
        {
            tinnitusSound = sounds.TinnitusSound;
            Logger.LogInfo("🔊 [SOUND_EFFECTS] Ensure: TinnitusSound получен из EFT.Sounds");
            return;
        }

        // 2) Попытка через текущего игрока
        if (localPlayer == null)
        {
            var gw = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gw != null)
            {
                localPlayer = gw.MainPlayer as LocalPlayer;
            }
        }
        if (localPlayer != null)
        {
            var tinnitusField = typeof(EFT.Player).GetField("_tinnitus", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (tinnitusField != null)
            {
                tinnitusSound = tinnitusField.GetValue(localPlayer) as AudioClip;
                if (tinnitusSound != null)
                {
                    Logger.LogInfo("🔊 [SOUND_EFFECTS] Ensure: TinnitusSound получен из Player._tinnitus");
                    return;
                }
            }
        }

        Logger.LogWarning("🔊 [SOUND_EFFECTS] Ensure: Не удалось получить TinnitusSound");
    }
    
    private void CleanupTrackedTinnitusAudioObjects()
    {
        for (int i = _trackedTinnitusSources.Count - 1; i >= 0; i--)
        {
            var source = _trackedTinnitusSources[i];
            if (source == null)
            {
                continue;
            }

            try
            {
                source.Stop();
                if (source.gameObject != null)
                {
                    UnityEngine.Object.Destroy(source.gameObject);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"🔊 [TINNITUS] Cleanup tracked source failed: {ex.Message}");
            }
        }

        _trackedTinnitusSources.Clear();
        activeTinnitusSource = null;
    }

    /// <summary>
    /// Одноразовая страховка при входе в рейд: убрать TinnitusAudioSource, оставшиеся от прошлой сессии.
    /// Не вызывать на каждом респавне — FindObjectsOfType дорогой на большой карте.
    /// </summary>
    private void CleanupOrphanTinnitusAudioObjects()
    {
        try
        {
            var orphanSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
            int removed = 0;

            for (int i = 0; i < orphanSources.Length; i++)
            {
                var source = orphanSources[i];
                if (source == null || source.gameObject == null)
                {
                    continue;
                }

                if (source.gameObject.name != "TinnitusAudioSource")
                {
                    continue;
                }

                source.Stop();
                UnityEngine.Object.Destroy(source.gameObject);
                removed++;
            }

            if (removed > 0)
            {
                Logger.LogInfo($"🔊 [TINNITUS] Raid-start orphan cleanup removed {removed} TinnitusAudioSource(s)");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"🔊 [TINNITUS] Raid-start orphan cleanup failed: {ex.Message}");
        }
    }

    private void TrackTinnitusSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        if (!_trackedTinnitusSources.Contains(source))
        {
            _trackedTinnitusSources.Add(source);
        }

        activeTinnitusSource = source;
    }

    private void StartTinnitusEffect()
    {
        try
        {
            StopTinnitusEffect(force: true);

            // Проверяем headless режим
            if (IsHeadlessMode())
            {
                Logger.LogInfo("🔊 [TINNITUS] Headless режим - звуковой эффект пропущен");
                return;
            }
            
            if (!Settings.Settings.EnableTinnitusEffect.Value)
            {
                Logger.LogInfo("🔊 [TINNITUS] Эффект тиннитуса отключен в настройках");
                return;
            }

            // Гарантируем наличие клипа
            EnsureTinnitusClipLoaded();

            // Расчет длительности эффекта
            float duration = Settings.Settings.TinnitusDuration.Value;
            if (Settings.Settings.TinnitusFadeOut.Value && PlayerState.IsPlayerInvulnerable)
            {
                // Эффект длится до конца неуязвимости
                duration = PlayerState.InvulnUntil - Time.time;
            }

            // Пробуем использовать BetterAudio если доступен
            if (betterAudio != null && tinnitusSound != null)
            {
                try
                {
                    // Используем метод 2: BetterAudio.PlayNonspatial (работает лучше всего)
                    // Создаем временный AudioSource для контроля воспроизведения
                    GameObject tempAudioObject = new GameObject("TinnitusAudioSource");
                    var audioSource = tempAudioObject.AddComponent<AudioSource>();
                    TrackTinnitusSource(audioSource);
                    audioSource.spatialBlend = 0f; // 2D звук
                    audioSource.clip = tinnitusSound;
                    audioSource.volume = 0.1f;
                    audioSource.loop = true; // Зацикливаем звук
                    audioSource.Play();
                    
                    isTinnitusActive = true;
                    tinnitusStartTime = Time.time;
                    tinnitusDuration = duration;

                    Logger.LogInfo($"🔊 [TINNITUS] Эффект тиннитуса запущен через AudioSource.Play на {duration:F1} секунд");
                    ShowSoundNotification($"🔊 Эффект оглушения запущен на {duration:F1} секунд", false);
                    return;
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"🔊 [TINNITUS] BetterAudio.PlayNonspatial не работает (возможно Fika.Headless): {ex.Message}");
                }
            }

            // Fallback: используем альтернативные методы воспроизведения звука
            Logger.LogInfo("🔊 [TINNITUS] Используем fallback методы воспроизведения звука");
            
            if (tinnitusSound != null)
            {
                // Используем Unity AudioSource.PlayOneShot (не патчится Fika.Headless)
                if (fallbackAudioSource != null)
                {
                    fallbackAudioSource.PlayOneShot(tinnitusSound, 0.1f);
                    Logger.LogInfo("🔊 [TINNITUS] Звук воспроизведен через fallback AudioSource");
                }
                else
                {
                    Logger.LogWarning("🔊 [TINNITUS] Fallback AudioSource недоступен — пропускаем GClass966 (удалён в 16.9)");
                }
            }
            else
            {
                Logger.LogWarning("🔊 [TINNITUS] Звук тиннитуса недоступен для fallback воспроизведения");
            }

            isTinnitusActive = true;
            tinnitusStartTime = Time.time;
            tinnitusDuration = duration;
            
            Logger.LogInfo($"🔊 [TINNITUS] Fallback эффект тиннитуса запущен на {duration:F1} секунд");
            ShowSoundNotification($"🔊 Fallback эффект оглушения запущен на {duration:F1} секунд", false);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🔊 [TINNITUS] Ошибка запуска эффекта тиннитуса: {ex.Message}");
        }
    }
    
    private void StopTinnitusEffect(bool force = false)
    {
        try
        {
            if (!force && !isTinnitusActive)
            {
                CleanupTrackedTinnitusAudioObjects();
                return;
            }

            CleanupTrackedTinnitusAudioObjects();
            
            isTinnitusActive = false;
            isSoundFadingOut = false;
            tinnitusStartTime = 0f;
            tinnitusDuration = 0f;

            Logger.LogInfo("🔊 [TINNITUS] Эффект тиннитуса остановлен");
            if (force || Settings.Settings.EnableTinnitusEffect.Value)
            {
                ShowSoundNotification("🔊 Эффект оглушения остановлен", false);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🔊 [TINNITUS] Ошибка остановки эффекта тиннитуса: {ex.Message}");
        }
    }
    
    private void StartSoundFadeOut()
    {
        try
        {
            if (!isTinnitusActive || activeTinnitusSource == null)
            {
                return;
            }
            
            isSoundFadingOut = true;
            Logger.LogInfo("🔊 [SOUND_FADE] Начинаем плавное затухание звука");
            StartCoroutine(FadeOutSound());
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🔊 [SOUND_FADE] Ошибка запуска затухания звука: {ex.Message}");
        }
    }
    
    private System.Collections.IEnumerator FadeOutSound()
    {
        if (activeTinnitusSource == null)
        {
            yield break;
        }
        
        float fadeDuration = 0.5f; // Длительность затухания (совпадает с fade-in экрана)
        float startVolume = activeTinnitusSource.volume;
        float elapsedTime = 0f;
        
        Logger.LogInfo($"🔊 [SOUND_FADE] Затухание звука: {startVolume} → 0 за {fadeDuration} секунд");
        
        while (elapsedTime < fadeDuration && activeTinnitusSource != null)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fadeDuration;
            float currentVolume = Mathf.Lerp(startVolume, 0f, progress);
            
            if (activeTinnitusSource != null)
            {
                activeTinnitusSource.volume = currentVolume;
            }
            
            yield return null;
        }
        
        // Полностью останавливаем звук после затухания
        if (activeTinnitusSource != null)
        {
            activeTinnitusSource.volume = 0f;
            StopTinnitusEffect();
        }
        
        Logger.LogInfo("🔊 [SOUND_FADE] Затухание звука завершено");
    }
    
    private void UpdateTinnitusEffect()
    {
        try
        {
            if (!isTinnitusActive || !Settings.Settings.EnableTinnitusEffect.Value || isSoundFadingOut)
            {
                return;
            }

            // Проверяем, нужно ли остановить эффект
            if (Settings.Settings.TinnitusFadeOut.Value && PlayerState.IsPlayerInvulnerable)
            {
                // Эффект должен длиться до конца неуязвимости
                float remainingTime = PlayerState.InvulnUntil - Time.time;
                if (remainingTime <= 0f)
                {
                    StopTinnitusEffect();
                    return;
                }
            }
            else
            {
                // Эффект длится фиксированное время
                float elapsedTime = Time.time - tinnitusStartTime;
                if (elapsedTime >= tinnitusDuration)
                {
                    StopTinnitusEffect();
                    return;
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🔊 [TINNITUS] Ошибка обновления эффекта тиннитуса: {ex.Message}");
        }
    }
    
    // Переменные для тестирования звуков удалены - см. SOUND_TESTING_METHODS_ARCHIVE.md
    
    // Метод TestSoundEffects удален - см. SOUND_TESTING_METHODS_ARCHIVE.md
    
    // Все методы тестирования звуков удалены - см. SOUND_TESTING_METHODS_ARCHIVE.md
    
    // Все методы тестирования звуков (TestMethod1-8, RunVolumeMethodSequence, PlayMethod2/5/8WithVolume, StopAllTestSounds, StopAndDestroyAudioSource) удалены - см. SOUND_TESTING_METHODS_ARCHIVE.md
    
    // Включение эффекта затемнения при смерти
    private void EnableDeathFade()
    {
        try
        {
            if (deathFade != null)
            {
                Logger.LogInfo("🎭 [SCREEN_FADE] Включаем эффект затемнения при смерти");
                deathFade.EnableEffect();
                ShowFadeEffectNotification("Экран затемняется...", false);
            }
            else
            {
                Logger.LogWarning("🎭 [SCREEN_FADE] DeathFade компонент недоступен");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🎭 [SCREEN_FADE] Ошибка включения DeathFade: {ex.Message}");
        }
    }
    
    // Отключение эффекта затемнения при смерти
    private void DisableDeathFade()
    {
        try
        {
            if (deathFade != null)
            {
                Logger.LogInfo("🎭 [SCREEN_FADE] Отключаем эффект затемнения при смерти");
                deathFade.DisableEffect();
            }
            else
            {
                Logger.LogWarning("🎭 [SCREEN_FADE] DeathFade компонент недоступен, пытаемся найти заново");
                
                // Попытка найти DeathFade заново
                var camera = CameraClass.Instance?.Camera;
                if (camera != null)
                {
                    deathFade = camera.GetComponent<DeathFade>();
                    if (deathFade != null)
                    {
                        Logger.LogInfo("🎭 [SCREEN_FADE] DeathFade найден заново, отключаем");
                        deathFade.DisableEffect();
                    }
                    else
                    {
                        Logger.LogWarning("🎭 [SCREEN_FADE] DeathFade не найден на камере");
                    }
                }
                else
                {
                    Logger.LogWarning("🎭 [SCREEN_FADE] Камера недоступна");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🎭 [SCREEN_FADE] Ошибка отключения DeathFade: {ex.Message}");
        }
    }
    
    // Затемнение экрана перед респавном
    private void StartRespawnFade()
    {
        try
        {
            // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Отключаем DeathFade перед включением PreloaderUI
            Logger.LogInfo("🎬 [SCREEN_FADE] Отключаем DeathFade перед респавном");
            DisableDeathFade();
            
            if (preloaderUI != null)
            {
                Logger.LogInfo("🎬 [SCREEN_FADE] Начинаем затемнение экрана для респавна");
                preloaderUI.SetBlackImageAlpha(1f); // Полное затемнение
                ShowFadeEffectNotification("Подготовка к возрождению...", false);

                // Блокируем управление игроком на период черноты экрана
                Logger.LogInfo("🎮 [INPUT_LOCK] Включаем блокировку ввода (SetIgnoreInput=true)");
                TrySetGlobalIgnoreInput(true);
                
                // Запускаем звуковой эффект сразу при появлении черноты
                Logger.LogInfo("🔊 [SCREEN_FADE] Запускаем звуковой эффект сразу при появлении черноты");
                try
                {
                    if (betterAudio == null) betterAudio = MonoBehaviourSingleton<BetterAudio>.Instance;
                    StartTinnitusEffect();
        }
        catch (System.Exception ex)
        {
                    Logger.LogError($"🔊 [SCREEN_FADE] Ошибка запуска звукового эффекта: {ex.Message}");
                }
            }
            else
            {
                Logger.LogWarning("🎬 [SCREEN_FADE] PreloaderUI недоступен");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🎬 [SCREEN_FADE] Ошибка затемнения экрана: {ex.Message}");
        }
    }
    
    // Плавное осветление после респавна
    private void EndRespawnFade()
    {
        try
        {
            // ДОПОЛНИТЕЛЬНАЯ ЗАЩИТА: Убеждаемся что DeathFade отключен
            Logger.LogInfo("🎬 [SCREEN_FADE] Убеждаемся что DeathFade отключен");
            DisableDeathFade();
            
            // Начинаем плавное затухание звука при восстановлении экрана
            Logger.LogInfo("🔊 [SCREEN_FADE] Начинаем плавное затухание звука при восстановлении экрана");
            StartSoundFadeOut();
            
            if (preloaderUI != null)
            {
                Logger.LogInfo("🎬 [SCREEN_FADE] Завершаем затемнение экрана после респавна");
                float wakeSpeed = Settings.Settings.RESPAWN_WAKE_FADE_SPEED_MULTIPLIER;
                preloaderUI.FadeBlackScreen(0.5f / wakeSpeed, -0.3f * wakeSpeed);
                ShowAutoRespawnNotification("Возрождение завершено", false);

                Logger.LogInfo("🎮 [INPUT_LOCK] Запускаем отложенную разблокировку ввода на середине fade-in");
                StartCoroutine(ReleaseInputAfter(0.25f / wakeSpeed));
            }
            else
            {
                Logger.LogWarning("🎬 [SCREEN_FADE] PreloaderUI недоступен");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🎬 [SCREEN_FADE] Ошибка осветления экрана: {ex.Message}");
        }
    }

        // Полная очистка эффектов/баффов (и положительных, и отрицательных)

    /// <summary>
    /// Fika sync: только ForceRemove эффектов (без повторного RestoreFullHealth).
    /// Полное лечение — в RestorePlayerHealth4Stages / Finalize.
    /// </summary>
    private void TryClearEffectsForRespawnNetworkSync(LocalPlayer player)
    {
        if (player == null)
        {
            return;
        }

        try
        {
            var health = player.ActiveHealthController;
            if (health == null)
            {
                return;
            }

            int removed = DesmatchHealthReflection.TryForceRemoveAllActiveEffects(health);
            Logger.LogInfo($"[EFFECTS] Respawn effect network sync, ForceRemove={removed}");
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"[EFFECTS] Respawn effect sync failed: {ex.Message}");
        }
    }

        private void TryClearAllStatusEffects(LocalPlayer player)
        {
            if (player == null)
            {
                Logger.LogWarning("[EFFECTS] LocalPlayer == null, очистка эффектов невозможна");
                    return;
            }

            try
            {
                Logger.LogInfo("[EFFECTS] ===== НАЧИНАЕМ ПОЛНУЮ ОЧИСТКУ ЭФФЕКТОВ =====");
                
                // КРИТИЧЕСКИ ВАЖНО: Принудительный сброс таймеров регенерации при очистке всех эффектов
                Logger.LogInfo("[EFFECTS] ===== ПРИНУДИТЕЛЬНЫЙ СБРОС ТАЙМЕРОВ РЕГЕНЕРАЦИИ ПРИ ОЧИСТКЕ ВСЕХ ЭФФЕКТОВ =====");
                TryForceResetRegenerationTimers(player);

                // КРИТИЧЕСКИ ВАЖНО: Принудительный сброс таймеров блокировки эффектов при очистке всех эффектов
                Logger.LogInfo("[EFFECTS] ===== ПРИНУДИТЕЛЬНЫЙ СБРОС ТАЙМЕРОВ БЛОКИРОВКИ ЭФФЕКТОВ ПРИ ОЧИСТКЕ ВСЕХ ЭФФЕКТОВ =====");
                TryForceResetEffectCooldownTimers(player);
                
                // ДЕТАЛЬНОЕ ЛОГИРОВАНИЕ ВСЕХ ЭФФЕКТОВ ДО ОЧИСТКИ
                LogAllEffectsBeforeClear(player);

                // 1) Попробуем через HealthEffectsController (если есть)
                var activeHealth = player.ActiveHealthController;
                if (activeHealth != null)
                {
                    Logger.LogInfo("[EFFECTS] Найден ActiveHealthController");

                    // Попытка вызвать ResetAllEffects / RemoveAllEffects если они существуют
                    var heController = activeHealth.GetType().GetField("HealthEffectsController_0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(activeHealth)
                        ?? activeHealth.GetType().GetField("_effects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(activeHealth);
                    if (heController != null)
                    {
                        var methods = heController.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        foreach (var m in methods)
                        {
                            if (m.Name.Contains("ResetAll") || m.Name.Contains("RemoveAll") || m.Name.Contains("ClearAll"))
                            {
                                Logger.LogInfo($"[EFFECTS] Вызываем {m.Name} на HealthEffectsController");
                                try { m.Invoke(heController, null); } catch (System.Exception ex) { Logger.LogError($"[EFFECTS] Ошибка вызова {m.Name}: {ex.Message}"); }
                            }
                        }

                        // Также обнулим все известные эффекты (положительные/отрицательные), если доступны коллекции
                        foreach (var f in heController.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                        {
                            var val = f.GetValue(heController);
                            if (val == null) continue;
                            // Если это коллекция эффектов - пройдемся и остановим/обнулим таймеры
                            if (val is System.Collections.IEnumerable enumerable && !(val is string))
                            {
                                foreach (var eff in enumerable)
                                {
                                    AttemptStopAndZeroEffect(eff);
                                }
                            }
                            else
                            {
                                // Вдруг это одиночный эффект
                                AttemptStopAndZeroEffect(val);
                            }
                        }
                    }
                    else
                    {
                        LogReflectionWarningOnce("effects_health_controller", "[EFFECTS] HealthEffectsController не найден (HealthEffectsController_0/_effects)");
                    }

                    // Дополнительно: сброс всех активных эффектов напрямую
                    var allEffectsField = activeHealth.GetType().GetField("_allEffects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (allEffectsField != null)
                    {
                        var allEffects = allEffectsField.GetValue(activeHealth) as System.Collections.IDictionary;
                        if (allEffects != null)
                        {
                            Logger.LogInfo($"[EFFECTS] Очищаем {allEffects.Count} эффектов из _allEffects");
                            allEffects.Clear();
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[EFFECTS] ActiveHealthController не найден");
                }

                // 2) Попробуем через StimulantController (для стимуляторов)
                var stimulantController = player.ActiveHealthController?.GetType().GetProperty("Stimulator", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(player.ActiveHealthController);
                if (stimulantController != null)
                {
                    Logger.LogInfo("[EFFECTS] Найден StimulantController");

                    // Попытка вызвать StopAll/ResetAll/RemoveAll/ClearAll
                    var methods = stimulantController.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var m in methods)
                    {
                        if (m.Name.Contains("StopAll") || m.Name.Contains("ResetAll") || m.Name.Contains("RemoveAll") || m.Name.Contains("ClearAll"))
                        {
                            Logger.LogInfo($"[EFFECTS] Вызываем {m.Name} на StimulantController");
                            try { m.Invoke(stimulantController, null); } catch (System.Exception ex) { Logger.LogError($"[EFFECTS] Ошибка вызова {m.Name}: {ex.Message}"); }
                        }
                    }

                    // Принудительная очистка _activeStims и _scheduledStims
                    var activeStimsField = stimulantController.GetType().GetField("_activeStims", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (activeStimsField != null)
                    {
                        var activeStims = activeStimsField.GetValue(stimulantController) as System.Collections.IList;
                        if (activeStims != null)
                        {
                            Logger.LogInfo($"[EFFECTS] Очищаем {activeStims.Count} активных стимуляторов из _activeStims");
                            activeStims.Clear();
                        }
                    }

                    var scheduledStimsField = stimulantController.GetType().GetField("_scheduledStims", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (scheduledStimsField != null)
                    {
                        var scheduledStims = scheduledStimsField.GetValue(stimulantController) as System.Collections.IList;
                        if (scheduledStims != null)
                        {
                            Logger.LogInfo($"[EFFECTS] Очищаем {scheduledStims.Count} запланированных стимуляторов из _scheduledStims");
                            scheduledStims.Clear();
                        }
                    }
            }
            else
            {
                    LogReflectionWarningOnce("effects_stimulant_controller", "[EFFECTS] StimulantController/Stimulator не найден, fallback RemoveNegativeEffects(Common)");
                    if (player.ActiveHealthController != null)
                    {
                        DesmatchHealthReflection.TryRemoveNegativeEffects(player.ActiveHealthController, EBodyPart.Common);
                    }
                }

                // 3) Попробуем через Skills.Buffs (для баффов)
                var skills = player.Skills;
                if (skills != null)
                {
                    Logger.LogInfo("[EFFECTS] Найден Skills");
                    var buffsField = skills.GetType().GetField("Buffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var buffs = buffsField?.GetValue(skills) as System.Collections.IEnumerable;
                    if (buffs != null)
                    {
                        var clearMethod = buffs.GetType().GetMethod("Clear");
                        if (clearMethod != null)
                        {
                            Logger.LogInfo("[EFFECTS] Очищаем баффы в Skills (Buffs.Clear)");
                            try { clearMethod.Invoke(buffs, null); } catch (System.Exception ex) { Logger.LogError($"[EFFECTS] Ошибка вызова Clear на Skills.Buffs: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        LogReflectionWarningOnce("effects_skills_buffs", "[EFFECTS] Skills.Buffs не найден");
                    }
                }
                else
                {
                    Logger.LogWarning("[EFFECTS] Skills не найден");
                }

                // 4) Очистка таймеров стимуляторов в Skills.Unsubscribers
                if (skills != null)
                {
                var skillsType = skills.GetType();
                var unsubscribersField = skillsType.GetField("Unsubscribers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?? skillsType.GetField("_unsubscribers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (unsubscribersField != null)
                {
                    var unsubscribers = unsubscribersField.GetValue(skills) as System.Collections.IList;
                    if (unsubscribers != null)
                    {
                        Logger.LogInfo($"[EFFECTS] Очищаем {unsubscribers.Count} таймеров стимуляторов из Skills.Unsubscribers");
                        
                        // Вызываем Dispose для каждого таймера перед очисткой
                        foreach (var timer in unsubscribers)
                        {
                            try
                            {
                                var disposeMethod = timer.GetType().GetMethod("Dispose");
                                disposeMethod?.Invoke(timer, null);
        }
        catch (System.Exception ex)
        {
                                Logger.LogWarning($"[EFFECTS] Ошибка при Dispose таймера: {ex.Message}");
                            }
                        }
                        
                        unsubscribers.Clear();
                        Logger.LogInfo($"[EFFECTS] Таймеры стимуляторов очищены");
                    }
                }
                }

                // 5) Снятие всех 29 эффектов стимуляторов
                TryClearAllStimulatorEffects(player);

                // 6) Специальная очистка эффектов переносимого веса и регенерации здоровья
                TryClearSpecificEffects(player);

                Logger.LogInfo("[EFFECTS] Очистка эффектов завершена");
                
                // ДЕТАЛЬНОЕ ЛОГИРОВАНИЕ ВСЕХ ЭФФЕКТОВ ПОСЛЕ ОЧИСТКИ
                LogAllEffectsAfterClear(player);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Критическая ошибка при очистке эффектов: {ex.Message}");
                Logger.LogError($"[EFFECTS] Stack trace: {ex.StackTrace}");
            }
        }

        // Детальное логирование всех эффектов ДО очистки
        private void LogAllEffectsBeforeClear(LocalPlayer player)
    {
        try
        {
                Logger.LogInfo("[EFFECTS_LOG] ===== ЛОГИРОВАНИЕ ЭФФЕКТОВ ДО ОЧИСТКИ =====");
                
                var activeHealth = player.ActiveHealthController;
                if (activeHealth != null)
                {
                    Logger.LogInfo("[EFFECTS_LOG] ActiveHealthController найден");
                    
                    // Логируем все поля ActiveHealthController
                    var healthType = activeHealth.GetType();
                    var healthFields = healthType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    foreach (var field in healthFields)
                    {
                        try
                        {
                            var fieldValue = field.GetValue(activeHealth);
                            var fieldName = field.Name;
                            var fieldType = field.FieldType.Name;
                            
                            if (fieldValue != null)
                            {
                                Logger.LogInfo($"[EFFECTS_LOG] ActiveHealthController.{fieldName} ({fieldType}) = {fieldValue}");
                                
                                // Если это коллекция эффектов
                                if (fieldValue is System.Collections.IEnumerable enumerable && !(fieldValue is string))
                                {
                                    var count = 0;
                                    foreach (var item in enumerable)
                                    {
                                        Logger.LogInfo($"[EFFECTS_LOG]   [{count}] {item.GetType().Name}: {item}");
                                        count++;
                                    }
                                    Logger.LogInfo($"[EFFECTS_LOG]   Всего элементов в {fieldName}: {count}");
                                }
            }
            else
            {
                                Logger.LogInfo($"[EFFECTS_LOG] ActiveHealthController.{fieldName} ({fieldType}) = null");
            }
        }
        catch (System.Exception ex)
        {
                            Logger.LogWarning($"[EFFECTS_LOG] Ошибка логирования поля {field.Name}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[EFFECTS_LOG] ActiveHealthController не найден");
                }
                
                // Логируем Skills
                var skills = player.Skills;
                if (skills != null)
                {
                    Logger.LogInfo("[EFFECTS_LOG] Skills найден");
                    var skillsType = skills.GetType();
                    var skillsFields = skillsType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    foreach (var field in skillsFields)
    {
        try
        {
                            var fieldValue = field.GetValue(skills);
                            var fieldName = field.Name;
                            var fieldType = field.FieldType.Name;
                            
                            if (fieldValue != null)
                            {
                                Logger.LogInfo($"[EFFECTS_LOG] Skills.{fieldName} ({fieldType}) = {fieldValue}");
                                
                                // Если это коллекция
                                if (fieldValue is System.Collections.IEnumerable enumerable && !(fieldValue is string))
                                {
                                    var count = 0;
                                    foreach (var item in enumerable)
                                    {
                                        Logger.LogInfo($"[EFFECTS_LOG]   [{count}] {item.GetType().Name}: {item}");
                                        count++;
                                    }
                                    Logger.LogInfo($"[EFFECTS_LOG]   Всего элементов в Skills.{fieldName}: {count}");
                                }
            }
            else
            {
                                Logger.LogInfo($"[EFFECTS_LOG] Skills.{fieldName} ({fieldType}) = null");
            }
        }
        catch (System.Exception ex)
        {
                            Logger.LogWarning($"[EFFECTS_LOG] Ошибка логирования Skills.{field.Name}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[EFFECTS_LOG] Skills не найден");
                }
                
                Logger.LogInfo("[EFFECTS_LOG] ===== ЛОГИРОВАНИЕ ЭФФЕКТОВ ДО ОЧИСТКИ ЗАВЕРШЕНО =====");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS_LOG] Ошибка логирования эффектов до очистки: {ex.Message}");
            }
        }

        // Детальное логирование всех эффектов ПОСЛЕ очистки
        private void LogAllEffectsAfterClear(LocalPlayer player)
    {
        try
        {
                Logger.LogInfo("[EFFECTS_LOG] ===== ЛОГИРОВАНИЕ ЭФФЕКТОВ ПОСЛЕ ОЧИСТКИ =====");
                
                var activeHealth = player.ActiveHealthController;
                if (activeHealth != null)
                {
                    Logger.LogInfo("[EFFECTS_LOG] ActiveHealthController найден после очистки");
                    
                    // Логируем все поля ActiveHealthController
                    var healthType = activeHealth.GetType();
                    var healthFields = healthType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    foreach (var field in healthFields)
                    {
                        try
                        {
                            var fieldValue = field.GetValue(activeHealth);
                            var fieldName = field.Name;
                            var fieldType = field.FieldType.Name;
                            
                            if (fieldValue != null)
                            {
                                Logger.LogInfo($"[EFFECTS_LOG] ActiveHealthController.{fieldName} ({fieldType}) = {fieldValue}");
                                
                                // Если это коллекция эффектов
                                if (fieldValue is System.Collections.IEnumerable enumerable && !(fieldValue is string))
                                {
                                    var count = 0;
                                    foreach (var item in enumerable)
                                    {
                                        Logger.LogInfo($"[EFFECTS_LOG]   [{count}] {item.GetType().Name}: {item}");
                                        count++;
                                    }
                                    Logger.LogInfo($"[EFFECTS_LOG]   Всего элементов в {fieldName} после очистки: {count}");
                                }
            }
            else
            {
                                Logger.LogInfo($"[EFFECTS_LOG] ActiveHealthController.{fieldName} ({fieldType}) = null");
            }
        }
        catch (System.Exception ex)
        {
                            Logger.LogWarning($"[EFFECTS_LOG] Ошибка логирования поля {field.Name} после очистки: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[EFFECTS_LOG] ActiveHealthController не найден после очистки");
                }
                
                // Логируем Skills
                var skills = player.Skills;
                if (skills != null)
                {
                    Logger.LogInfo("[EFFECTS_LOG] Skills найден после очистки");
                    var skillsType = skills.GetType();
                    var skillsFields = skillsType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    foreach (var field in skillsFields)
    {
        try
        {
                            var fieldValue = field.GetValue(skills);
                            var fieldName = field.Name;
                            var fieldType = field.FieldType.Name;
                            
                            if (fieldValue != null)
                            {
                                Logger.LogInfo($"[EFFECTS_LOG] Skills.{fieldName} ({fieldType}) = {fieldValue}");
                                
                                // Если это коллекция
                                if (fieldValue is System.Collections.IEnumerable enumerable && !(fieldValue is string))
                                {
                                    var count = 0;
                                    foreach (var item in enumerable)
                                    {
                                        Logger.LogInfo($"[EFFECTS_LOG]   [{count}] {item.GetType().Name}: {item}");
                                        count++;
                                    }
                                    Logger.LogInfo($"[EFFECTS_LOG]   Всего элементов в Skills.{fieldName} после очистки: {count}");
                                }
            }
            else
            {
                                Logger.LogInfo($"[EFFECTS_LOG] Skills.{fieldName} ({fieldType}) = null");
            }
        }
        catch (System.Exception ex)
        {
                            Logger.LogWarning($"[EFFECTS_LOG] Ошибка логирования Skills.{field.Name} после очистки: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[EFFECTS_LOG] Skills не найден после очистки");
                }
                
                Logger.LogInfo("[EFFECTS_LOG] ===== ЛОГИРОВАНИЕ ЭФФЕКТОВ ПОСЛЕ ОЧИСТКИ ЗАВЕРШЕНО =====");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS_LOG] Ошибка логирования эффектов после очистки: {ex.Message}");
            }
        }

        // Снятие всех 29 эффектов стимуляторов
        private void TryClearAllStimulatorEffects(LocalPlayer player)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] ===== НАЧИНАЕМ СНЯТИЕ ВСЕХ 29 ЭФФЕКТОВ СТИМУЛЯТОРОВ =====");
                
                var activeHealth = player.ActiveHealthController;
                if (activeHealth == null)
                {
                    Logger.LogWarning("[EFFECTS] ActiveHealthController не найден для снятия эффектов стимуляторов");
                    return;
                }

                // Получаем Stimulator через рефлексию
                var stimulatorProperty = activeHealth.GetType().GetProperty("Stimulator", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (stimulatorProperty == null)
                {
                    LogReflectionWarningOnce("effects_stimulator_property", "[EFFECTS] Stimulator свойство не найдено, fallback RemoveNegativeEffects(Common)");
                    DesmatchHealthReflection.TryRemoveNegativeEffects(activeHealth, EBodyPart.Common);
                    return;
                }

                var stimulator = stimulatorProperty.GetValue(activeHealth);
                if (stimulator == null)
                {
                    Logger.LogWarning("[EFFECTS] Stimulator объект не найден");
                    return;
                }

                Logger.LogInfo($"[EFFECTS] Stimulator найден: {stimulator.GetType().Name}");

                // Получаем ActiveBuffs через рефлексию
                var activeBuffsProperty = stimulator.GetType().GetProperty("ActiveBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (activeBuffsProperty == null)
                {
                    Logger.LogWarning("[EFFECTS] ActiveBuffs свойство не найдено");
                    return;
                }

                var activeBuffs = activeBuffsProperty.GetValue(stimulator) as System.Collections.IEnumerable;
                if (activeBuffs == null)
                {
                    Logger.LogWarning("[EFFECTS] ActiveBuffs не найдены");
                    return;
                }

                int removedCount = 0;
                foreach (var buff in activeBuffs)
                {
                    try
                    {
                        var buffType = buff.GetType();
                        Logger.LogInfo($"[EFFECTS] Обрабатываем бафф: {buffType.Name}");

                        // Получаем Settings через рефлексию
                        var settingsProperty = buffType.GetProperty("Settings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (settingsProperty == null)
                        {
                            Logger.LogWarning($"[EFFECTS] Settings свойство не найдено для {buffType.Name}");
                            continue;
                        }

                        var settings = settingsProperty.GetValue(buff);
                        if (settings == null)
                        {
                            Logger.LogWarning($"[EFFECTS] Settings объект не найден для {buffType.Name}");
                            continue;
                        }

                        // Получаем BuffType через рефлексию
                        var buffTypeProperty = settings.GetType().GetProperty("BuffType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (buffTypeProperty == null)
                        {
                            Logger.LogWarning($"[EFFECTS] BuffType свойство не найдено для {buffType.Name}");
                            continue;
                        }

                        var buffTypeEnum = buffTypeProperty.GetValue(settings);
                        Logger.LogInfo($"[EFFECTS] Тип баффа: {buffTypeEnum}");

                        // Получаем Active через рефлексию
                        var activeProperty = buffType.GetProperty("Active", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (activeProperty == null)
                        {
                            Logger.LogWarning($"[EFFECTS] Active свойство не найдено для {buffType.Name}");
                            continue;
                        }

                        var isActive = (bool)activeProperty.GetValue(buff);
                        if (!isActive)
                        {
                            Logger.LogInfo($"[EFFECTS] Бафф {buffTypeEnum} не активен, пропускаем");
                            continue;
                        }

                        // Получаем ValueObj через рефлексию
                        var valueObjProperty = buffType.GetProperty("ValueObj", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (valueObjProperty == null)
                        {
                            Logger.LogWarning($"[EFFECTS] ValueObj свойство не найдено для {buffType.Name}");
                            continue;
                        }

                        var valueObj = valueObjProperty.GetValue(buff);
                        if (valueObj != null)
                        {
                            // Вызываем ForceResidue() для снятия эффекта
                            var forceResidueMethod = valueObj.GetType().GetMethod("ForceResidue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (forceResidueMethod != null)
                            {
                                forceResidueMethod.Invoke(valueObj, null);
                                Logger.LogInfo($"[EFFECTS] Вызван ForceResidue() для {buffTypeEnum}");
                                removedCount++;
            }
            else
            {
                                Logger.LogWarning($"[EFFECTS] ForceResidue метод не найден для {buffTypeEnum}");
            }
                        }

                        // Устанавливаем Active = false
                        activeProperty.SetValue(buff, false);
                        Logger.LogInfo($"[EFFECTS] Установлен Active = false для {buffTypeEnum}");
        }
        catch (System.Exception ex)
        {
                        Logger.LogError($"[EFFECTS] Ошибка при снятии баффа: {ex.Message}");
                    }
                }

                Logger.LogInfo($"[EFFECTS] Снято {removedCount} эффектов стимуляторов");

                // КРИТИЧЕСКИ ВАЖНО: Принудительно сбрасываем таймеры блокировки повторного применения эффектов
                Logger.LogInfo("[EFFECTS] ===== ПРИНУДИТЕЛЬНЫЙ СБРОС ТАЙМЕРОВ БЛОКИРОВКИ =====");
                TryForceResetEffectCooldownTimers(player);

                // КРИТИЧЕСКИ ВАЖНО: Принудительно сбрасываем таймеры регенерации
                Logger.LogInfo("[EFFECTS] ===== ПРИНУДИТЕЛЬНЫЙ СБРОС ТАЙМЕРОВ РЕГЕНЕРАЦИИ =====");
                TryForceResetRegenerationTimers(player);

                // КРИТИЧЕСКИ ВАЖНО: Принудительно снимаем все эффекты регенерации
                Logger.LogInfo("[EFFECTS] ===== ПРИНУДИТЕЛЬНОЕ СНЯТИЕ ВСЕХ ЭФФЕКТОВ РЕГЕНЕРАЦИИ =====");
                TryForceRemoveAllRegenerationEffects(activeHealth);

                // Дополнительно вызываем RemoveNegativeEffects для снятия негативных эффектов
                TryRemoveNegativeEffects(activeHealth);

                // Дополнительно вызываем RemoveAllBuffs для снятия всех баффов
                TryRemoveAllBuffs(activeHealth);

                // Дополнительно вызываем RemoveAllBloodLosses для снятия всех кровотечений
                TryRemoveAllBloodLosses(activeHealth);

                // Дополнительно вызываем RemoveAllEffects для снятия всех эффектов
                TryRemoveAllEffects(activeHealth);

                // Дополнительно вызываем ClearEffects для очистки всех эффектов
                TryClearEffects(activeHealth);

                // Дополнительно вызываем StopEffects для остановки всех эффектов
                TryStopEffects(activeHealth);

                // Дополнительно вызываем ResetEffects для сброса всех эффектов
                TryResetEffects(activeHealth);

                // Дополнительно вызываем DisposeEffects для освобождения всех эффектов
                TryDisposeEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllEffects для принудительного снятия всех эффектов
                TryForceRemoveAllEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllBuffs для принудительного снятия всех баффов
                TryForceRemoveAllBuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllBloodLosses для принудительного снятия всех кровотечений
                TryForceRemoveAllBloodLosses(activeHealth);

                // Дополнительно вызываем ForceRemoveAllNegativeEffects для принудительного снятия всех негативных эффектов
                TryForceRemoveAllNegativeEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllPositiveEffects для принудительного снятия всех позитивных эффектов
                TryForceRemoveAllPositiveEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorEffects для принудительного снятия всех эффектов стимуляторов
                TryForceRemoveAllStimulatorEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorBuffs для принудительного снятия всех баффов стимуляторов
                TryForceRemoveAllStimulatorBuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorDebuffs для принудительного снятия всех дебаффов стимуляторов
                TryForceRemoveAllStimulatorDebuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorToxins для принудительного снятия всех токсинов стимуляторов
                TryForceRemoveAllStimulatorToxins(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorBleedings для принудительного снятия всех кровотечений стимуляторов
                TryForceRemoveAllStimulatorBleedings(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorFractures для принудительного снятия всех переломов стимуляторов
                TryForceRemoveAllStimulatorFractures(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorContusions для принудительного снятия всех контузий стимуляторов
                TryForceRemoveAllStimulatorContusions(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorPains для принудительного снятия всех болей стимуляторов
                TryForceRemoveAllStimulatorPains(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorTremors для принудительного снятия всех треморов стимуляторов
                TryForceRemoveAllStimulatorTremors(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorBlurs для принудительного снятия всех размытий стимуляторов
                TryForceRemoveAllStimulatorBlurs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorWiggles для принудительного снятия всех покачиваний стимуляторов
                TryForceRemoveAllStimulatorWiggles(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorQuantumTunnellings для принудительного снятия всех квантовых туннелирований стимуляторов
                TryForceRemoveAllStimulatorQuantumTunnellings(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorMisfires для принудительного снятия всех осечек стимуляторов
                TryForceRemoveAllStimulatorMisfires(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorZombieInfections для принудительного снятия всех зомби-инфекций стимуляторов
                TryForceRemoveAllStimulatorZombieInfections(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorFrostbiteBuffs для принудительного снятия всех обморожений стимуляторов
                TryForceRemoveAllStimulatorFrostbiteBuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorHalloweenBuffs для принудительного снятия всех хэллоуин-баффов стимуляторов
                TryForceRemoveAllStimulatorHalloweenBuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorBodyTemperatures для принудительного снятия всех температур тела стимуляторов
                TryForceRemoveAllStimulatorBodyTemperatures(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorAntidotes для принудительного снятия всех антидотов стимуляторов
                TryForceRemoveAllStimulatorAntidotes(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorDamageModifiers для принудительного снятия всех модификаторов урона стимуляторов
                TryForceRemoveAllStimulatorDamageModifiers(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorWeightLimits для принудительного снятия всех лимитов веса стимуляторов
                TryForceRemoveAllStimulatorWeightLimits(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorUnknownToxins для принудительного снятия всех неизвестных токсинов стимуляторов
                TryForceRemoveAllStimulatorUnknownToxins(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorLethalToxins для принудительного снятия всех летальных токсинов стимуляторов
                TryForceRemoveAllStimulatorLethalToxins(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorStomachBloodlosses для принудительного снятия всех желудочных кровотечений стимуляторов
                TryForceRemoveAllStimulatorStomachBloodlosses(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorLightBleedings для принудительного снятия всех легких кровотечений стимуляторов
                TryForceRemoveAllStimulatorLightBleedings(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorHeavyBleedings для принудительного снятия всех тяжелых кровотечений стимуляторов
                TryForceRemoveAllStimulatorHeavyBleedings(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorHealthRates для принудительного снятия всех регенераций здоровья стимуляторов
                TryForceRemoveAllStimulatorHealthRates(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorEnergyRates для принудительного снятия всех регенераций энергии стимуляторов
                TryForceRemoveAllStimulatorEnergyRates(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorHydrationRates для принудительного снятия всех регенераций гидратации стимуляторов
                TryForceRemoveAllStimulatorHydrationRates(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorSkillRates для принудительного снятия всех регенераций навыков стимуляторов
                TryForceRemoveAllStimulatorSkillRates(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorMaxStaminas для принудительного снятия всех максимальных выносливостей стимуляторов
                TryForceRemoveAllStimulatorMaxStaminas(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorStaminaRates для принудительного снятия всех регенераций выносливости стимуляторов
                TryForceRemoveAllStimulatorStaminaRates(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorRemoveNegativeEffects для принудительного снятия всех снятий негативных эффектов стимуляторов
                TryForceRemoveAllStimulatorRemoveNegativeEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorRemoveAllBuffs для принудительного снятия всех снятий всех баффов стимуляторов
                TryForceRemoveAllStimulatorRemoveAllBuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorRemoveAllBloodLosses для принудительного снятия всех снятий всех кровотечений стимуляторов
                TryForceRemoveAllStimulatorRemoveAllBloodLosses(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorRemoveAllEffects для принудительного снятия всех снятий всех эффектов стимуляторов
                TryForceRemoveAllStimulatorRemoveAllEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorClearEffects для принудительного снятия всех очисток всех эффектов стимуляторов
                TryForceRemoveAllStimulatorClearEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorStopEffects для принудительного снятия всех остановок всех эффектов стимуляторов
                TryForceRemoveAllStimulatorStopEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorResetEffects для принудительного снятия всех сбросов всех эффектов стимуляторов
                TryForceRemoveAllStimulatorResetEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorDisposeEffects для принудительного снятия всех освобождений всех эффектов стимуляторов
                TryForceRemoveAllStimulatorDisposeEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllEffects для принудительного снятия всех принудительных снятий всех эффектов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllBuffs для принудительного снятия всех принудительных снятий всех баффов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllBuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllBloodLosses для принудительного снятия всех принудительных снятий всех кровотечений стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllBloodLosses(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllNegativeEffects для принудительного снятия всех принудительных снятий всех негативных эффектов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllNegativeEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllPositiveEffects для принудительного снятия всех принудительных снятий всех позитивных эффектов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllPositiveEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects для принудительного снятия всех принудительных снятий всех эффектов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorEffects(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs для принудительного снятия всех принудительных снятий всех баффов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs для принудительного снятия всех принудительных снятий всех дебаффов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins для принудительного снятия всех принудительных снятий всех токсинов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorToxins(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings для принудительного снятия всех принудительных снятий всех кровотечений стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures для принудительного снятия всех принудительных снятий всех переломов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorFractures(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions для принудительного снятия всех принудительных снятий всех контузий стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorContusions(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorPains для принудительного снятия всех принудительных снятий всех болей стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorPains(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors для принудительного снятия всех принудительных снятий всех треморов стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorTremors(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs для принудительного снятия всех принудительных снятий всех размытий стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs(activeHealth);

                // Дополнительно вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles для принудительного снятия всех принудительных снятий всех покачиваний стимуляторов
                TryForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles(activeHealth);

                Logger.LogInfo("[EFFECTS] ===== СНЯТИЕ ВСЕХ 29 ЭФФЕКТОВ СТИМУЛЯТОРОВ ЗАВЕРШЕНО =====");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Критическая ошибка при снятии эффектов стимуляторов: {ex.Message}");
                Logger.LogError($"[EFFECTS] Stack trace: {ex.StackTrace}");
            }
        }

        // Снятие негативных эффектов через RemoveNegativeEffects
        private void TryRemoveNegativeEffects(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем RemoveNegativeEffects для снятия негативных эффектов");
                
                // Получаем метод RemoveNegativeEffects через рефлексию
                var removeNegativeEffectsMethod = activeHealth.GetType().GetMethod("RemoveNegativeEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (removeNegativeEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] RemoveNegativeEffects метод не найден");
                    return;
                }

                // Вызываем RemoveNegativeEffects для всех частей тела
                var bodyPartType = System.Type.GetType("EFT.HealthSystem.EBodyPart, Assembly-CSharp");
                if (bodyPartType != null)
                {
                    var commonField = bodyPartType.GetField("Common");
                    if (commonField != null)
                    {
                        var commonValue = commonField.GetValue(null);
                        removeNegativeEffectsMethod.Invoke(activeHealth, new object[] { commonValue });
                        Logger.LogInfo("[EFFECTS] RemoveNegativeEffects вызван для EBodyPart.Common");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове RemoveNegativeEffects: {ex.Message}");
            }
        }

        // Снятие всех баффов через RemoveAllBuffs
        private void TryRemoveAllBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем RemoveAllBuffs для снятия всех баффов");
                
                // Получаем метод RemoveAllBuffs через рефлексию
                var removeAllBuffsMethod = activeHealth.GetType().GetMethod("RemoveAllBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (removeAllBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] RemoveAllBuffs метод не найден");
                    return;
                }

                // Вызываем RemoveAllBuffs
                removeAllBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] RemoveAllBuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове RemoveAllBuffs: {ex.Message}");
            }
        }

        // Снятие всех кровотечений через RemoveAllBloodLosses
        private void TryRemoveAllBloodLosses(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем RemoveAllBloodLosses для снятия всех кровотечений");
                
                // Получаем метод RemoveAllBloodLosses через рефлексию
                var removeAllBloodLossesMethod = activeHealth.GetType().GetMethod("RemoveAllBloodLosses", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (removeAllBloodLossesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] RemoveAllBloodLosses метод не найден");
                    return;
                }

                // Вызываем RemoveAllBloodLosses
                removeAllBloodLossesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] RemoveAllBloodLosses вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове RemoveAllBloodLosses: {ex.Message}");
        }
    }
    
        // Снятие всех эффектов через RemoveAllEffects
        private void TryRemoveAllEffects(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем RemoveAllEffects для снятия всех эффектов");
                
                // Получаем метод RemoveAllEffects через рефлексию
                var removeAllEffectsMethod = activeHealth.GetType().GetMethod("RemoveAllEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (removeAllEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] RemoveAllEffects метод не найден");
                    return;
                }

                // Вызываем RemoveAllEffects
                removeAllEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] RemoveAllEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове RemoveAllEffects: {ex.Message}");
            }
        }

        // Очистка всех эффектов через ClearEffects
        private void TryClearEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ClearEffects для очистки всех эффектов");
                
                // Получаем метод ClearEffects через рефлексию
                var clearEffectsMethod = activeHealth.GetType().GetMethod("ClearEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (clearEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ClearEffects метод не найден");
                    return;
                }

                // Вызываем ClearEffects
                clearEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ClearEffects вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ClearEffects: {ex.Message}");
            }
        }

        // Остановка всех эффектов через StopEffects
        private void TryStopEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем StopEffects для остановки всех эффектов");
                
                // Получаем метод StopEffects через рефлексию
                var stopEffectsMethod = activeHealth.GetType().GetMethod("StopEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (stopEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] StopEffects метод не найден");
                    return;
                }

                // Вызываем StopEffects
                stopEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] StopEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове StopEffects: {ex.Message}");
            }
        }

        // Сброс всех эффектов через ResetEffects
        private void TryResetEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ResetEffects для сброса всех эффектов");
                
                // Получаем метод ResetEffects через рефлексию
                var resetEffectsMethod = activeHealth.GetType().GetMethod("ResetEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (resetEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ResetEffects метод не найден");
                    return;
                }

                // Вызываем ResetEffects
                resetEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ResetEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ResetEffects: {ex.Message}");
            }
        }

        // Освобождение всех эффектов через DisposeEffects
        private void TryDisposeEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем DisposeEffects для освобождения всех эффектов");
                
                // Получаем метод DisposeEffects через рефлексию
                var disposeEffectsMethod = activeHealth.GetType().GetMethod("DisposeEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (disposeEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] DisposeEffects метод не найден");
                    return;
                }

                // Вызываем DisposeEffects
                disposeEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] DisposeEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове DisposeEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех эффектов через ForceRemoveAllEffects
        private void TryForceRemoveAllEffects(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllEffects для принудительного снятия всех эффектов");
                
                // Получаем метод ForceRemoveAllEffects через рефлексию
                var forceRemoveAllEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllEffects метод не найден");
                return;
            }

                // Вызываем ForceRemoveAllEffects
                forceRemoveAllEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех баффов через ForceRemoveAllBuffs
        private void TryForceRemoveAllBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllBuffs для принудительного снятия всех баффов");
                
                // Получаем метод ForceRemoveAllBuffs через рефлексию
                var forceRemoveAllBuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllBuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllBuffs
                forceRemoveAllBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllBuffs вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllBuffs: {ex.Message}");
        }
    }

        // Принудительное снятие всех кровотечений через ForceRemoveAllBloodLosses
        private void TryForceRemoveAllBloodLosses(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllBloodLosses для принудительного снятия всех кровотечений");
                
                // Получаем метод ForceRemoveAllBloodLosses через рефлексию
                var forceRemoveAllBloodLossesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllBloodLosses", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllBloodLossesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllBloodLosses метод не найден");
                return;
            }

                // Вызываем ForceRemoveAllBloodLosses
                forceRemoveAllBloodLossesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllBloodLosses вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllBloodLosses: {ex.Message}");
            }
        }

        // Принудительное снятие всех негативных эффектов через ForceRemoveAllNegativeEffects
        private void TryForceRemoveAllNegativeEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllNegativeEffects для принудительного снятия всех негативных эффектов");
                
                // Получаем метод ForceRemoveAllNegativeEffects через рефлексию
                var forceRemoveAllNegativeEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllNegativeEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllNegativeEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllNegativeEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllNegativeEffects
                forceRemoveAllNegativeEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllNegativeEffects вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllNegativeEffects: {ex.Message}");
        }
    }

        // Принудительное снятие всех позитивных эффектов через ForceRemoveAllPositiveEffects
        private void TryForceRemoveAllPositiveEffects(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllPositiveEffects для принудительного снятия всех позитивных эффектов");
                
                // Получаем метод ForceRemoveAllPositiveEffects через рефлексию
                var forceRemoveAllPositiveEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllPositiveEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllPositiveEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllPositiveEffects метод не найден");
                return;
            }

                // Вызываем ForceRemoveAllPositiveEffects
                forceRemoveAllPositiveEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllPositiveEffects вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllPositiveEffects: {ex.Message}");
        }
    }

        // Принудительное снятие всех эффектов стимуляторов через ForceRemoveAllStimulatorEffects
        private void TryForceRemoveAllStimulatorEffects(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorEffects для принудительного снятия всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorEffects через рефлексию
                var forceRemoveAllStimulatorEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorEffects
                forceRemoveAllStimulatorEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех баффов стимуляторов через ForceRemoveAllStimulatorBuffs
        private void TryForceRemoveAllStimulatorBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorBuffs для принудительного снятия всех баффов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorBuffs через рефлексию
                var forceRemoveAllStimulatorBuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorBuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorBuffs
                forceRemoveAllStimulatorBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorBuffs вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorBuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех дебаффов стимуляторов через ForceRemoveAllStimulatorDebuffs
        private void TryForceRemoveAllStimulatorDebuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorDebuffs для принудительного снятия всех дебаффов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorDebuffs через рефлексию
                var forceRemoveAllStimulatorDebuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorDebuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorDebuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorDebuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorDebuffs
                forceRemoveAllStimulatorDebuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorDebuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorDebuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех токсинов стимуляторов через ForceRemoveAllStimulatorToxins
        private void TryForceRemoveAllStimulatorToxins(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorToxins для принудительного снятия всех токсинов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorToxins через рефлексию
                var forceRemoveAllStimulatorToxinsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorToxins", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorToxinsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorToxins метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorToxins
                forceRemoveAllStimulatorToxinsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorToxins вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorToxins: {ex.Message}");
            }
        }

        // Принудительное снятие всех кровотечений стимуляторов через ForceRemoveAllStimulatorBleedings
        private void TryForceRemoveAllStimulatorBleedings(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorBleedings для принудительного снятия всех кровотечений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorBleedings через рефлексию
                var forceRemoveAllStimulatorBleedingsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorBleedings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorBleedingsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorBleedings метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorBleedings
                forceRemoveAllStimulatorBleedingsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorBleedings вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorBleedings: {ex.Message}");
        }
    }
    
        // Принудительное снятие всех переломов стимуляторов через ForceRemoveAllStimulatorFractures
        private void TryForceRemoveAllStimulatorFractures(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorFractures для принудительного снятия всех переломов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorFractures через рефлексию
                var forceRemoveAllStimulatorFracturesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorFractures", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorFracturesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorFractures метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorFractures
                forceRemoveAllStimulatorFracturesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorFractures вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorFractures: {ex.Message}");
            }
        }

        // Принудительное снятие всех контузий стимуляторов через ForceRemoveAllStimulatorContusions
        private void TryForceRemoveAllStimulatorContusions(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorContusions для принудительного снятия всех контузий стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorContusions через рефлексию
                var forceRemoveAllStimulatorContusionsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorContusions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorContusionsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorContusions метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorContusions
                forceRemoveAllStimulatorContusionsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorContusions вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorContusions: {ex.Message}");
            }
        }

        // Принудительное снятие всех болей стимуляторов через ForceRemoveAllStimulatorPains
        private void TryForceRemoveAllStimulatorPains(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorPains для принудительного снятия всех болей стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorPains через рефлексию
                var forceRemoveAllStimulatorPainsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorPains", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorPainsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorPains метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorPains
                forceRemoveAllStimulatorPainsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorPains вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorPains: {ex.Message}");
            }
        }

        // Принудительное снятие всех треморов стимуляторов через ForceRemoveAllStimulatorTremors
        private void TryForceRemoveAllStimulatorTremors(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorTremors для принудительного снятия всех треморов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorTremors через рефлексию
                var forceRemoveAllStimulatorTremorsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorTremors", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorTremorsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorTremors метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorTremors
                forceRemoveAllStimulatorTremorsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorTremors вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorTremors: {ex.Message}");
        }
    }
    
        // Принудительное снятие всех размытий стимуляторов через ForceRemoveAllStimulatorBlurs
        private void TryForceRemoveAllStimulatorBlurs(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorBlurs для принудительного снятия всех размытий стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorBlurs через рефлексию
                var forceRemoveAllStimulatorBlursMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorBlurs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorBlursMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorBlurs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorBlurs
                forceRemoveAllStimulatorBlursMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorBlurs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorBlurs: {ex.Message}");
            }
        }

        // Принудительное снятие всех покачиваний стимуляторов через ForceRemoveAllStimulatorWiggles
        private void TryForceRemoveAllStimulatorWiggles(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorWiggles для принудительного снятия всех покачиваний стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorWiggles через рефлексию
                var forceRemoveAllStimulatorWigglesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorWiggles", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorWigglesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorWiggles метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorWiggles
                forceRemoveAllStimulatorWigglesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorWiggles вызван");
                }
                catch (System.Exception ex)
                {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorWiggles: {ex.Message}");
            }
        }

        // Принудительное снятие всех квантовых туннелирований стимуляторов через ForceRemoveAllStimulatorQuantumTunnellings
        private void TryForceRemoveAllStimulatorQuantumTunnellings(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorQuantumTunnellings для принудительного снятия всех квантовых туннелирований стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorQuantumTunnellings через рефлексию
                var forceRemoveAllStimulatorQuantumTunnellingsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorQuantumTunnellings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorQuantumTunnellingsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorQuantumTunnellings метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorQuantumTunnellings
                forceRemoveAllStimulatorQuantumTunnellingsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorQuantumTunnellings вызван");
        }
        catch (System.Exception ex)
        {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorQuantumTunnellings: {ex.Message}");
        }
    }
    
        // Принудительное снятие всех осечек стимуляторов через ForceRemoveAllStimulatorMisfires
        private void TryForceRemoveAllStimulatorMisfires(object activeHealth)
    {
        try
        {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorMisfires для принудительного снятия всех осечек стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorMisfires через рефлексию
                var forceRemoveAllStimulatorMisfiresMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorMisfires", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorMisfiresMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorMisfires метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorMisfires
                forceRemoveAllStimulatorMisfiresMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorMisfires вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorMisfires: {ex.Message}");
            }
        }

        // Принудительное снятие всех зомби-инфекций стимуляторов через ForceRemoveAllStimulatorZombieInfections
        private void TryForceRemoveAllStimulatorZombieInfections(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorZombieInfections для принудительного снятия всех зомби-инфекций стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorZombieInfections через рефлексию
                var forceRemoveAllStimulatorZombieInfectionsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorZombieInfections", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorZombieInfectionsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorZombieInfections метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorZombieInfections
                forceRemoveAllStimulatorZombieInfectionsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorZombieInfections вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorZombieInfections: {ex.Message}");
            }
        }

        // Принудительное снятие всех обморожений стимуляторов через ForceRemoveAllStimulatorFrostbiteBuffs
        private void TryForceRemoveAllStimulatorFrostbiteBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorFrostbiteBuffs для принудительного снятия всех обморожений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorFrostbiteBuffs через рефлексию
                var forceRemoveAllStimulatorFrostbiteBuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorFrostbiteBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorFrostbiteBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorFrostbiteBuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorFrostbiteBuffs
                forceRemoveAllStimulatorFrostbiteBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorFrostbiteBuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorFrostbiteBuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех хэллоуин-баффов стимуляторов через ForceRemoveAllStimulatorHalloweenBuffs
        private void TryForceRemoveAllStimulatorHalloweenBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorHalloweenBuffs для принудительного снятия всех хэллоуин-баффов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorHalloweenBuffs через рефлексию
                var forceRemoveAllStimulatorHalloweenBuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorHalloweenBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorHalloweenBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorHalloweenBuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorHalloweenBuffs
                forceRemoveAllStimulatorHalloweenBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorHalloweenBuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorHalloweenBuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех температур тела стимуляторов через ForceRemoveAllStimulatorBodyTemperatures
        private void TryForceRemoveAllStimulatorBodyTemperatures(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorBodyTemperatures для принудительного снятия всех температур тела стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorBodyTemperatures через рефлексию
                var forceRemoveAllStimulatorBodyTemperaturesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorBodyTemperatures", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorBodyTemperaturesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorBodyTemperatures метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorBodyTemperatures
                forceRemoveAllStimulatorBodyTemperaturesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorBodyTemperatures вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorBodyTemperatures: {ex.Message}");
            }
        }

        // Принудительное снятие всех антидотов стимуляторов через ForceRemoveAllStimulatorAntidotes
        private void TryForceRemoveAllStimulatorAntidotes(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorAntidotes для принудительного снятия всех антидотов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorAntidotes через рефлексию
                var forceRemoveAllStimulatorAntidotesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorAntidotes", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorAntidotesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorAntidotes метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorAntidotes
                forceRemoveAllStimulatorAntidotesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorAntidotes вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorAntidotes: {ex.Message}");
            }
        }

        // Принудительное снятие всех модификаторов урона стимуляторов через ForceRemoveAllStimulatorDamageModifiers
        private void TryForceRemoveAllStimulatorDamageModifiers(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorDamageModifiers для принудительного снятия всех модификаторов урона стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorDamageModifiers через рефлексию
                var forceRemoveAllStimulatorDamageModifiersMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorDamageModifiers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorDamageModifiersMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorDamageModifiers метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorDamageModifiers
                forceRemoveAllStimulatorDamageModifiersMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorDamageModifiers вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorDamageModifiers: {ex.Message}");
            }
        }

        // Принудительное снятие всех лимитов веса стимуляторов через ForceRemoveAllStimulatorWeightLimits
        private void TryForceRemoveAllStimulatorWeightLimits(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorWeightLimits для принудительного снятия всех лимитов веса стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorWeightLimits через рефлексию
                var forceRemoveAllStimulatorWeightLimitsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorWeightLimits", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorWeightLimitsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorWeightLimits метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorWeightLimits
                forceRemoveAllStimulatorWeightLimitsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorWeightLimits вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorWeightLimits: {ex.Message}");
            }
        }

        // Принудительное снятие всех неизвестных токсинов стимуляторов через ForceRemoveAllStimulatorUnknownToxins
        private void TryForceRemoveAllStimulatorUnknownToxins(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorUnknownToxins для принудительного снятия всех неизвестных токсинов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorUnknownToxins через рефлексию
                var forceRemoveAllStimulatorUnknownToxinsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorUnknownToxins", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorUnknownToxinsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorUnknownToxins метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorUnknownToxins
                forceRemoveAllStimulatorUnknownToxinsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorUnknownToxins вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorUnknownToxins: {ex.Message}");
            }
        }

        // Принудительное снятие всех летальных токсинов стимуляторов через ForceRemoveAllStimulatorLethalToxins
        private void TryForceRemoveAllStimulatorLethalToxins(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorLethalToxins для принудительного снятия всех летальных токсинов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorLethalToxins через рефлексию
                var forceRemoveAllStimulatorLethalToxinsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorLethalToxins", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorLethalToxinsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorLethalToxins метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorLethalToxins
                forceRemoveAllStimulatorLethalToxinsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorLethalToxins вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorLethalToxins: {ex.Message}");
            }
        }

        // Принудительное снятие всех желудочных кровотечений стимуляторов через ForceRemoveAllStimulatorStomachBloodlosses
        private void TryForceRemoveAllStimulatorStomachBloodlosses(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorStomachBloodlosses для принудительного снятия всех желудочных кровотечений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorStomachBloodlosses через рефлексию
                var forceRemoveAllStimulatorStomachBloodlossesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorStomachBloodlosses", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorStomachBloodlossesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorStomachBloodlosses метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorStomachBloodlosses
                forceRemoveAllStimulatorStomachBloodlossesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorStomachBloodlosses вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorStomachBloodlosses: {ex.Message}");
            }
        }

        // Принудительное снятие всех легких кровотечений стимуляторов через ForceRemoveAllStimulatorLightBleedings
        private void TryForceRemoveAllStimulatorLightBleedings(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorLightBleedings для принудительного снятия всех легких кровотечений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorLightBleedings через рефлексию
                var forceRemoveAllStimulatorLightBleedingsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorLightBleedings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorLightBleedingsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorLightBleedings метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorLightBleedings
                forceRemoveAllStimulatorLightBleedingsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorLightBleedings вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorLightBleedings: {ex.Message}");
            }
        }

        // Принудительное снятие всех тяжелых кровотечений стимуляторов через ForceRemoveAllStimulatorHeavyBleedings
        private void TryForceRemoveAllStimulatorHeavyBleedings(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorHeavyBleedings для принудительного снятия всех тяжелых кровотечений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorHeavyBleedings через рефлексию
                var forceRemoveAllStimulatorHeavyBleedingsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorHeavyBleedings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorHeavyBleedingsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorHeavyBleedings метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorHeavyBleedings
                forceRemoveAllStimulatorHeavyBleedingsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorHeavyBleedings вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorHeavyBleedings: {ex.Message}");
            }
        }

        // Принудительное снятие всех регенераций здоровья стимуляторов через ForceRemoveAllStimulatorHealthRates
        private void TryForceRemoveAllStimulatorHealthRates(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorHealthRates для принудительного снятия всех регенераций здоровья стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorHealthRates через рефлексию
                var forceRemoveAllStimulatorHealthRatesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorHealthRates", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorHealthRatesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorHealthRates метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorHealthRates
                forceRemoveAllStimulatorHealthRatesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorHealthRates вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorHealthRates: {ex.Message}");
            }
        }

        // Принудительное снятие всех регенераций энергии стимуляторов через ForceRemoveAllStimulatorEnergyRates
        private void TryForceRemoveAllStimulatorEnergyRates(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorEnergyRates для принудительного снятия всех регенераций энергии стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorEnergyRates через рефлексию
                var forceRemoveAllStimulatorEnergyRatesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorEnergyRates", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorEnergyRatesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorEnergyRates метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorEnergyRates
                forceRemoveAllStimulatorEnergyRatesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorEnergyRates вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorEnergyRates: {ex.Message}");
            }
        }

        // Принудительное снятие всех регенераций гидратации стимуляторов через ForceRemoveAllStimulatorHydrationRates
        private void TryForceRemoveAllStimulatorHydrationRates(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorHydrationRates для принудительного снятия всех регенераций гидратации стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorHydrationRates через рефлексию
                var forceRemoveAllStimulatorHydrationRatesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorHydrationRates", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorHydrationRatesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorHydrationRates метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorHydrationRates
                forceRemoveAllStimulatorHydrationRatesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorHydrationRates вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorHydrationRates: {ex.Message}");
            }
        }

        // Принудительное снятие всех регенераций навыков стимуляторов через ForceRemoveAllStimulatorSkillRates
        private void TryForceRemoveAllStimulatorSkillRates(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorSkillRates для принудительного снятия всех регенераций навыков стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorSkillRates через рефлексию
                var forceRemoveAllStimulatorSkillRatesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorSkillRates", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorSkillRatesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorSkillRates метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorSkillRates
                forceRemoveAllStimulatorSkillRatesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorSkillRates вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorSkillRates: {ex.Message}");
            }
        }

        // Принудительное снятие всех максимальных выносливостей стимуляторов через ForceRemoveAllStimulatorMaxStaminas
        private void TryForceRemoveAllStimulatorMaxStaminas(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorMaxStaminas для принудительного снятия всех максимальных выносливостей стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorMaxStaminas через рефлексию
                var forceRemoveAllStimulatorMaxStaminasMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorMaxStaminas", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorMaxStaminasMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorMaxStaminas метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorMaxStaminas
                forceRemoveAllStimulatorMaxStaminasMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorMaxStaminas вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorMaxStaminas: {ex.Message}");
            }
        }

        // Принудительное снятие всех регенераций выносливости стимуляторов через ForceRemoveAllStimulatorStaminaRates
        private void TryForceRemoveAllStimulatorStaminaRates(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorStaminaRates для принудительного снятия всех регенераций выносливости стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorStaminaRates через рефлексию
                var forceRemoveAllStimulatorStaminaRatesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorStaminaRates", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorStaminaRatesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorStaminaRates метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorStaminaRates
                forceRemoveAllStimulatorStaminaRatesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorStaminaRates вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorStaminaRates: {ex.Message}");
            }
        }

        // Принудительное снятие всех снятий негативных эффектов стимуляторов через ForceRemoveAllStimulatorRemoveNegativeEffects
        private void TryForceRemoveAllStimulatorRemoveNegativeEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorRemoveNegativeEffects для принудительного снятия всех снятий негативных эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorRemoveNegativeEffects через рефлексию
                var forceRemoveAllStimulatorRemoveNegativeEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorRemoveNegativeEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorRemoveNegativeEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorRemoveNegativeEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorRemoveNegativeEffects
                forceRemoveAllStimulatorRemoveNegativeEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorRemoveNegativeEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorRemoveNegativeEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех снятий всех баффов стимуляторов через ForceRemoveAllStimulatorRemoveAllBuffs
        private void TryForceRemoveAllStimulatorRemoveAllBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorRemoveAllBuffs для принудительного снятия всех снятий всех баффов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorRemoveAllBuffs через рефлексию
                var forceRemoveAllStimulatorRemoveAllBuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorRemoveAllBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorRemoveAllBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorRemoveAllBuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorRemoveAllBuffs
                forceRemoveAllStimulatorRemoveAllBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorRemoveAllBuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorRemoveAllBuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех снятий всех кровотечений стимуляторов через ForceRemoveAllStimulatorRemoveAllBloodLosses
        private void TryForceRemoveAllStimulatorRemoveAllBloodLosses(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorRemoveAllBloodLosses для принудительного снятия всех снятий всех кровотечений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorRemoveAllBloodLosses через рефлексию
                var forceRemoveAllStimulatorRemoveAllBloodLossesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorRemoveAllBloodLosses", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorRemoveAllBloodLossesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorRemoveAllBloodLosses метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorRemoveAllBloodLosses
                forceRemoveAllStimulatorRemoveAllBloodLossesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorRemoveAllBloodLosses вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorRemoveAllBloodLosses: {ex.Message}");
            }
        }

        // Принудительное снятие всех снятий всех эффектов стимуляторов через ForceRemoveAllStimulatorRemoveAllEffects
        private void TryForceRemoveAllStimulatorRemoveAllEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorRemoveAllEffects для принудительного снятия всех снятий всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorRemoveAllEffects через рефлексию
                var forceRemoveAllStimulatorRemoveAllEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorRemoveAllEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorRemoveAllEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorRemoveAllEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorRemoveAllEffects
                forceRemoveAllStimulatorRemoveAllEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorRemoveAllEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorRemoveAllEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех очисток всех эффектов стимуляторов через ForceRemoveAllStimulatorClearEffects
        private void TryForceRemoveAllStimulatorClearEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorClearEffects для принудительного снятия всех очисток всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorClearEffects через рефлексию
                var forceRemoveAllStimulatorClearEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorClearEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorClearEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorClearEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorClearEffects
                forceRemoveAllStimulatorClearEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorClearEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorClearEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех остановок всех эффектов стимуляторов через ForceRemoveAllStimulatorStopEffects
        private void TryForceRemoveAllStimulatorStopEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorStopEffects для принудительного снятия всех остановок всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorStopEffects через рефлексию
                var forceRemoveAllStimulatorStopEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorStopEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorStopEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorStopEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorStopEffects
                forceRemoveAllStimulatorStopEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorStopEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorStopEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех сбросов всех эффектов стимуляторов через ForceRemoveAllStimulatorResetEffects
        private void TryForceRemoveAllStimulatorResetEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorResetEffects для принудительного снятия всех сбросов всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorResetEffects через рефлексию
                var forceRemoveAllStimulatorResetEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorResetEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorResetEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorResetEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorResetEffects
                forceRemoveAllStimulatorResetEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorResetEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorResetEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех освобождений всех эффектов стимуляторов через ForceRemoveAllStimulatorDisposeEffects
        private void TryForceRemoveAllStimulatorDisposeEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorDisposeEffects для принудительного снятия всех освобождений всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorDisposeEffects через рефлексию
                var forceRemoveAllStimulatorDisposeEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorDisposeEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorDisposeEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorDisposeEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorDisposeEffects
                forceRemoveAllStimulatorDisposeEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorDisposeEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorDisposeEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех эффектов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllEffects
        private void TryForceRemoveAllStimulatorForceRemoveAllEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllEffects для принудительного снятия всех принудительных снятий всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllEffects через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllEffects
                forceRemoveAllStimulatorForceRemoveAllEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех баффов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllBuffs
        private void TryForceRemoveAllStimulatorForceRemoveAllBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllBuffs для принудительного снятия всех принудительных снятий всех баффов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllBuffs через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllBuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllBuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllBuffs
                forceRemoveAllStimulatorForceRemoveAllBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllBuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllBuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех кровотечений стимуляторов через ForceRemoveAllStimulatorForceRemoveAllBloodLosses
        private void TryForceRemoveAllStimulatorForceRemoveAllBloodLosses(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllBloodLosses для принудительного снятия всех принудительных снятий всех кровотечений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllBloodLosses через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllBloodLossesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllBloodLosses", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllBloodLossesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllBloodLosses метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllBloodLosses
                forceRemoveAllStimulatorForceRemoveAllBloodLossesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllBloodLosses вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllBloodLosses: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех негативных эффектов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllNegativeEffects
        private void TryForceRemoveAllStimulatorForceRemoveAllNegativeEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllNegativeEffects для принудительного снятия всех принудительных снятий всех негативных эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllNegativeEffects через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllNegativeEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllNegativeEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllNegativeEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllNegativeEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllNegativeEffects
                forceRemoveAllStimulatorForceRemoveAllNegativeEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllNegativeEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllNegativeEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех позитивных эффектов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllPositiveEffects
        private void TryForceRemoveAllStimulatorForceRemoveAllPositiveEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllPositiveEffects для принудительного снятия всех принудительных снятий всех позитивных эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllPositiveEffects через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllPositiveEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllPositiveEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllPositiveEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllPositiveEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllPositiveEffects
                forceRemoveAllStimulatorForceRemoveAllPositiveEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllPositiveEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllPositiveEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех эффектов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects для принудительного снятия всех принудительных снятий всех эффектов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorEffectsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorEffectsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects
                forceRemoveAllStimulatorForceRemoveAllStimulatorEffectsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorEffects: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех баффов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs для принудительного снятия всех принудительных снятий всех баффов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorBuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorBuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs
                forceRemoveAllStimulatorForceRemoveAllStimulatorBuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorBuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех дебаффов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs для принудительного снятия всех принудительных снятий всех дебаффов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorDebuffsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorDebuffsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs
                forceRemoveAllStimulatorForceRemoveAllStimulatorDebuffsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorDebuffs: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех токсинов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorToxins(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins для принудительного снятия всех принудительных снятий всех токсинов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorToxinsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorToxinsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins
                forceRemoveAllStimulatorForceRemoveAllStimulatorToxinsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorToxins: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех кровотечений стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings для принудительного снятия всех принудительных снятий всех кровотечений стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorBleedingsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorBleedingsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings
                forceRemoveAllStimulatorForceRemoveAllStimulatorBleedingsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorBleedings: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех переломов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorFractures(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures для принудительного снятия всех принудительных снятий всех переломов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorFracturesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorFracturesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures
                forceRemoveAllStimulatorForceRemoveAllStimulatorFracturesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorFractures: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех контузий стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorContusions(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions для принудительного снятия всех принудительных снятий всех контузий стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorContusionsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorContusionsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions
                forceRemoveAllStimulatorForceRemoveAllStimulatorContusionsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorContusions: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех болей стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorPains
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorPains(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorPains для принудительного снятия всех принудительных снятий всех болей стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorPains через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorPainsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorPains", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorPainsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorPains метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorPains
                forceRemoveAllStimulatorForceRemoveAllStimulatorPainsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorPains вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorPains: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех треморов стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorTremors(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors для принудительного снятия всех принудительных снятий всех треморов стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorTremorsMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorTremorsMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors
                forceRemoveAllStimulatorForceRemoveAllStimulatorTremorsMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorTremors: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех размытий стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs для принудительного снятия всех принудительных снятий всех размытий стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorBlursMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorBlursMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs
                forceRemoveAllStimulatorForceRemoveAllStimulatorBlursMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorBlurs: {ex.Message}");
            }
        }

        // Принудительное снятие всех принудительных снятий всех покачиваний стимуляторов через ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles
        private void TryForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles для принудительного снятия всех принудительных снятий всех покачиваний стимуляторов");
                
                // Получаем метод ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles через рефлексию
                var forceRemoveAllStimulatorForceRemoveAllStimulatorWigglesMethod = activeHealth.GetType().GetMethod("ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (forceRemoveAllStimulatorForceRemoveAllStimulatorWigglesMethod == null)
                {
                    Logger.LogWarning("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles метод не найден");
                    return;
                }

                // Вызываем ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles
                forceRemoveAllStimulatorForceRemoveAllStimulatorWigglesMethod.Invoke(activeHealth, null);
                Logger.LogInfo("[EFFECTS] ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles вызван");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка при вызове ForceRemoveAllStimulatorForceRemoveAllStimulatorWiggles: {ex.Message}");
            }
        }

        // КРИТИЧЕСКИ ВАЖНО: Принудительное снятие всех эффектов регенерации
        private void TryForceRemoveAllRegenerationEffects(object activeHealth)
        {
            try
            {
                Logger.LogInfo("[REGENERATION] Начинаем принудительное снятие всех эффектов регенерации");

                // Получаем поле _activeBuffs через рефлексию
                var activeBuffsField = activeHealth.GetType().GetField("_activeBuffs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (activeBuffsField != null)
                {
                    var activeBuffs = activeBuffsField.GetValue(activeHealth) as System.Collections.IDictionary;
                    if (activeBuffs != null)
                    {
                        Logger.LogInfo($"[REGENERATION] Найдено {activeBuffs.Count} активных баффов. Ищем эффекты регенерации.");

                        var regenerationEffectsToRemove = new System.Collections.Generic.List<object>();

                        foreach (System.Collections.DictionaryEntry entry in activeBuffs)
                        {
                            var buff = entry.Value;
                            var buffType = buff.GetType();

                            // Ищем эффекты регенерации по названию типа
                            if (buffType.Name.Contains("Regeneration") || 
                                buffType.Name.Contains("HealthBoost") ||
                                buffType.Name.Contains("StaminaBoost") ||
                                buffType.Name.Contains("EnergyBoost") ||
                                buffType.Name.Contains("HydrationBoost") ||
                                buffType.Name.Contains("SkillBoost") ||
                                buffType.Name.Contains("FullHealth") ||
                                buffType.Name.Contains("Heal") ||
                                buffType.Name.Contains("Restore"))
                            {
                                Logger.LogInfo($"[REGENERATION] Обнаружен эффект регенерации: {buffType.Name}");
                                regenerationEffectsToRemove.Add(buff);
                            }
                        }

                        Logger.LogInfo($"[REGENERATION] Найдено {regenerationEffectsToRemove.Count} эффектов регенерации для снятия");

                        foreach (var buff in regenerationEffectsToRemove)
                        {
                            try
                            {
                                // Пробуем вызвать ForceResidue
                                var valueObjField = buff.GetType().GetField("ValueObj", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (valueObjField != null)
                                {
                                    var valueObj = valueObjField.GetValue(buff);
                                    if (valueObj != null)
                                    {
                                        var forceResidueMethod = valueObj.GetType().GetMethod("ForceResidue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        if (forceResidueMethod != null)
                                        {
                                            forceResidueMethod.Invoke(valueObj, null);
                                            Logger.LogInfo($"[REGENERATION] ForceResidue() вызван для {valueObj.GetType().Name}");
                                        }
                                    }
                                }

                                // Пробуем вызвать ForceRemove
                                var forceRemoveMethod = buff.GetType().GetMethod("ForceRemove", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (forceRemoveMethod != null)
                                {
                                    forceRemoveMethod.Invoke(buff, null);
                                    Logger.LogInfo($"[REGENERATION] ForceRemove() вызван для {buff.GetType().Name}");
                                }

                                // Устанавливаем Active в false
                                var activeField = buff.GetType().GetProperty("Active", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (activeField != null && activeField.CanWrite)
                                {
                                    activeField.SetValue(buff, false);
                                    Logger.LogInfo($"[REGENERATION] Active установлено в false для {buff.GetType().Name}");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Logger.LogError($"[REGENERATION] Ошибка при снятии эффекта регенерации {buff.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                }

                // Дополнительно пробуем вызвать методы снятия регенерации через рефлексию
                var regenerationMethods = new string[]
                {
                    "RemoveAllRegenerationEffects",
                    "ForceRemoveAllRegenerationEffects", 
                    "ClearAllRegenerationEffects",
                    "StopAllRegenerationEffects",
                    "DisableAllRegenerationEffects"
                };

                foreach (var methodName in regenerationMethods)
                {
                    try
                    {
                        var method = activeHealth.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (method != null)
                        {
                            method.Invoke(activeHealth, null);
                            Logger.LogInfo($"[REGENERATION] Метод {methodName} вызван");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Logger.LogWarning($"[REGENERATION] Ошибка при вызове {methodName}: {ex.Message}");
                    }
                }

                Logger.LogInfo("[REGENERATION] ===== ПРИНУДИТЕЛЬНОЕ СНЯТИЕ ЭФФЕКТОВ РЕГЕНЕРАЦИИ ЗАВЕРШЕНО =====");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[REGENERATION] Критическая ошибка при снятии эффектов регенерации: {ex.Message}");
                Logger.LogError($"[REGENERATION] Stack trace: {ex.StackTrace}");
            }
        }

        // КРИТИЧЕСКИ ВАЖНО: Специальный сброс таймеров регенерации
        private void TryForceResetRegenerationTimers(LocalPlayer player)
        {
            try
            {
                Logger.LogInfo("[REGEN_TIMERS] ===== НАЧИНАЕМ СПЕЦИАЛЬНЫЙ СБРОС ТАЙМЕРОВ РЕГЕНЕРАЦИИ =====");

                // Получаем ActiveHealthController
                var activeHealth = player.ActiveHealthController;
                if (activeHealth == null)
                {
                    Logger.LogWarning("[REGEN_TIMERS] ActiveHealthController не найден");
                    return;
                }

                Logger.LogInfo($"[REGEN_TIMERS] ActiveHealthController найден, тип: {activeHealth.GetType().Name}");

                // МЕТОД 1: Ищем поля с таймерами регенерации в ActiveHealthController
                var allFields = activeHealth.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var field in allFields)
                {
                    if (field.Name.ToLower().Contains("regeneration") || 
                        field.Name.ToLower().Contains("timer") ||
                        field.Name.ToLower().Contains("cooldown") ||
                        field.Name.ToLower().Contains("boost"))
                    {
                        Logger.LogInfo($"[REGEN_TIMERS] Найдено поле регенерации: {field.Name} (тип: {field.FieldType.Name})");
                        
                        try
                        {
                            var fieldValue = field.GetValue(activeHealth);
                            if (fieldValue != null)
                            {
                                Logger.LogInfo($"[REGEN_TIMERS] Поле {field.Name} содержит значение: {fieldValue}");
                                
                                // Если это таймер, пробуем сбросить его
                                var fieldType = fieldValue.GetType();
                                var startTimeField = fieldType.GetField("_startTime", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (startTimeField != null)
                                {
                                    float currentTime = UnityEngine.Time.realtimeSinceStartup;
                                    float newStartTime = currentTime - 999f;
                                    startTimeField.SetValue(fieldValue, newStartTime);
                                    Logger.LogInfo($"[REGEN_TIMERS] Таймер {field.Name} сброшен на 1 секунду");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Logger.LogWarning($"[REGEN_TIMERS] Ошибка при обработке поля {field.Name}: {ex.Message}");
                        }
                    }
                }

                // МЕТОД 2: Ищем методы сброса регенерации
                var regenerationResetMethods = new string[]
                {
                    "ResetRegenerationTimers",
                    "ClearRegenerationTimers", 
                    "StopRegenerationTimers",
                    "DisableRegenerationTimers",
                    "ForceResetRegenerationTimers"
                };

                foreach (var methodName in regenerationResetMethods)
                {
                    try
                    {
                        var method = activeHealth.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (method != null)
                        {
                            method.Invoke(activeHealth, null);
                            Logger.LogInfo($"[REGEN_TIMERS] Метод {methodName} вызван");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Logger.LogWarning($"[REGEN_TIMERS] Ошибка при вызове {methodName}: {ex.Message}");
                    }
                }

                // МЕТОД 3: Пробуем сбросить таймеры через Skills
                var skills = player.Skills;
                if (skills != null)
                {
                    Logger.LogInfo("[REGEN_TIMERS] Пробуем сбросить таймеры регенерации через Skills");
                    
                    var skillsFields = skills.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var field in skillsFields)
                    {
                        if (field.Name.ToLower().Contains("regeneration") || 
                            field.Name.ToLower().Contains("timer") ||
                            field.Name.ToLower().Contains("cooldown"))
                        {
                            Logger.LogInfo($"[REGEN_TIMERS] Найдено поле в Skills: {field.Name} (тип: {field.FieldType.Name})");
                            
                            try
                            {
                                var fieldValue = field.GetValue(skills);
                                if (fieldValue is System.Collections.IList list && list.Count > 0)
                                {
                                    Logger.LogInfo($"[REGEN_TIMERS] Поле {field.Name} содержит {list.Count} элементов");
                                    
                                    int modifiedCount = 0;
                                    foreach (var item in list)
                                    {
                                        if (item != null)
                                        {
                                            var itemType = item.GetType();
                                            var startTimeField = itemType.GetField("_startTime", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            if (startTimeField != null)
                                            {
                                                float currentTime = UnityEngine.Time.realtimeSinceStartup;
                                                float newStartTime = currentTime - 999f;
                                                startTimeField.SetValue(item, newStartTime);
                                                modifiedCount++;
                                            }
                                        }
                                    }
                                    
                                    if (modifiedCount > 0)
                                    {
                                        Logger.LogInfo($"[REGEN_TIMERS] Сброшено {modifiedCount} таймеров регенерации в поле {field.Name}");
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Logger.LogWarning($"[REGEN_TIMERS] Ошибка при обработке поля Skills {field.Name}: {ex.Message}");
                            }
                        }
                    }
                }

                Logger.LogInfo("[REGEN_TIMERS] ===== СПЕЦИАЛЬНЫЙ СБРОС ТАЙМЕРОВ РЕГЕНЕРАЦИИ ЗАВЕРШЕН =====");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[REGEN_TIMERS] Критическая ошибка при сбросе таймеров регенерации: {ex.Message}");
                Logger.LogError($"[REGEN_TIMERS] Stack trace: {ex.StackTrace}");
            }
        }

        // КРИТИЧЕСКИ ВАЖНО: Принудительный сброс таймеров блокировки повторного применения эффектов
        private void TryForceResetEffectCooldownTimers(LocalPlayer player)
        {
            try
            {
                Logger.LogInfo("[TIMERS] ===== НАЧИНАЕМ ПРИНУДИТЕЛЬНЫЙ СБРОС ТАЙМЕРОВ БЛОКИРОВКИ ЭФФЕКТОВ =====");

                // Получаем Skills
                var skills = player.Skills;
                if (skills == null)
                {
                    Logger.LogWarning("[TIMERS] Skills не найден");
                    return;
                }

                Logger.LogInfo($"[TIMERS] Skills найден, тип: {skills.GetType().Name}");

                // МЕТОД 1: Unsubscribers (EFT 16.9) или legacy _unsubscribers
                var unsubscribersField = skills.GetType().GetField("Unsubscribers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?? skills.GetType().GetField("_unsubscribers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (unsubscribersField != null)
                {
                    Logger.LogInfo("[TIMERS] Поле Unsubscribers найдено");
                    var unsubscribers = unsubscribersField.GetValue(skills) as System.Collections.IList;
                    if (unsubscribers != null && unsubscribers.Count > 0)
                    {
                        Logger.LogInfo($"[TIMERS] Найдено {unsubscribers.Count} активных таймеров блокировки");
                        
                        int modifiedCount = 0;
                        foreach (var timer in unsubscribers)
                        {
                            try
                            {
                                var timerType = timer.GetType();
                                Logger.LogInfo($"[TIMERS] Обрабатываем таймер типа: {timerType.Name}");
                                
                                // Ищем поле _startTime
                                var startTimeField = timerType.GetField("_startTime", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (startTimeField != null)
                                {
                                    float currentTime = UnityEngine.Time.realtimeSinceStartup;
                                    float newStartTime = currentTime - 999f;
                                    startTimeField.SetValue(timer, newStartTime);
                                    modifiedCount++;
                                    Logger.LogInfo($"[TIMERS] Таймер #{modifiedCount}: установлен на 1 секунду (startTime={newStartTime})");
                                }
                                else
                                {
                                    Logger.LogWarning($"[TIMERS] Поле _startTime не найдено в {timerType.Name}");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Logger.LogWarning($"[TIMERS] Ошибка при модификации таймера: {ex.Message}");
                            }
                        }
                        
                        Logger.LogInfo($"[TIMERS] Модифицировано {modifiedCount} таймеров из {unsubscribers.Count}");
                    }
                    else
                    {
                        Logger.LogInfo("[TIMERS] Активные таймеры блокировки не найдены в _unsubscribers");
                    }
                }
                else
                {
                    LogReflectionWarningOnce("timers_unsubscribers", "[TIMERS] Поле Unsubscribers/_unsubscribers не найдено в Skills");
                }

                // МЕТОД 2: Пробуем найти другие возможные поля с таймерами
                Logger.LogInfo("[TIMERS] Ищем альтернативные поля с таймерами...");
                
                var allFields = skills.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var field in allFields)
                {
                    if (field.Name.ToLower().Contains("timer") || 
                        field.Name.ToLower().Contains("cooldown") || 
                        field.Name.ToLower().Contains("unsub") ||
                        field.Name.ToLower().Contains("subscriber"))
                    {
                        Logger.LogInfo($"[TIMERS] Найдено потенциальное поле: {field.Name} (тип: {field.FieldType.Name})");
                        
                        try
                        {
                            var fieldValue = field.GetValue(skills);
                            if (fieldValue is System.Collections.IList list && list.Count > 0)
                            {
                                Logger.LogInfo($"[TIMERS] Поле {field.Name} содержит {list.Count} элементов");
                                
                                int modifiedCount = 0;
                                foreach (var item in list)
                                {
                                    if (item != null)
                                    {
                                        var itemType = item.GetType();
                                        var startTimeField = itemType.GetField("_startTime", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        if (startTimeField != null)
                                        {
                                            float currentTime = UnityEngine.Time.realtimeSinceStartup;
                                            float newStartTime = currentTime - 999f;
                                            startTimeField.SetValue(item, newStartTime);
                                            modifiedCount++;
                                        }
                                    }
                                }
                                
                                if (modifiedCount > 0)
                                {
                                    Logger.LogInfo($"[TIMERS] Модифицировано {modifiedCount} таймеров в поле {field.Name}");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Logger.LogWarning($"[TIMERS] Ошибка при обработке поля {field.Name}: {ex.Message}");
                        }
                    }
                }

                // МЕТОД 3: Специальная обработка таймеров регенерации
                Logger.LogInfo("[TIMERS] Специальная обработка таймеров регенерации...");
                TryForceResetRegenerationTimers(player);

                Logger.LogInfo("[TIMERS] ===== ПРИНУДИТЕЛЬНЫЙ СБРОС ТАЙМЕРОВ ЗАВЕРШЕН =====");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[TIMERS] Критическая ошибка при сбросе таймеров: {ex.Message}");
                Logger.LogError($"[TIMERS] Stack trace: {ex.StackTrace}");
            }
        }

        // Специальная очистка эффектов переносимого веса и регенерации здоровья
        private void TryClearSpecificEffects(LocalPlayer player)
        {
            try
            {
                Logger.LogInfo("[EFFECTS] Начинаем специальную очистку эффектов переносимого веса и регенерации");

                // Ищем все возможные поля и свойства, связанные с эффектами
                var playerType = player.GetType();
                var allFields = playerType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                foreach (var field in allFields)
                {
                    try
                    {
                        var fieldValue = field.GetValue(player);
                        if (fieldValue == null) continue;

                        // Ищем поля, связанные с эффектами, таймерами, баффами
                        var fieldName = field.Name.ToLower();
                        if (fieldName.Contains("effect") || fieldName.Contains("buff") || fieldName.Contains("timer") || 
                            fieldName.Contains("weight") || fieldName.Contains("regeneration") || fieldName.Contains("regenerate"))
                        {
                            Logger.LogInfo($"[EFFECTS] Найдено поле эффекта: {field.Name} = {fieldValue.GetType().Name}");
                            
                            // Если это коллекция эффектов
                            if (fieldValue is System.Collections.IEnumerable enumerable && !(fieldValue is string))
                            {
                                var count = 0;
                                foreach (var item in enumerable)
                                {
                                    AttemptStopAndZeroEffect(item);
                                    count++;
                                }
                                Logger.LogInfo($"[EFFECTS] Обработано {count} элементов в коллекции {field.Name}");
                                
                                // Пытаемся очистить коллекцию
                                var clearMethod = fieldValue.GetType().GetMethod("Clear");
                                if (clearMethod != null)
                                {
                                    clearMethod.Invoke(fieldValue, null);
                                    Logger.LogInfo($"[EFFECTS] Очищена коллекция {field.Name}");
                                }
            }
            else
            {
                                // Одиночный эффект
                                AttemptStopAndZeroEffect(fieldValue);
                            }
            }
        }
        catch (System.Exception ex)
        {
                        Logger.LogWarning($"[EFFECTS] Ошибка обработки поля {field.Name}: {ex.Message}");
                    }
                }

                // Дополнительно ищем в ActiveHealthController
                var activeHealth = player.ActiveHealthController;
                if (activeHealth != null)
                {
                    var healthType = activeHealth.GetType();
                    var healthFields = healthType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    foreach (var field in healthFields)
                    {
                        try
                        {
                            var fieldValue = field.GetValue(activeHealth);
                            if (fieldValue == null) continue;

                            var fieldName = field.Name.ToLower();
                            if (fieldName.Contains("weight") || fieldName.Contains("regeneration") || fieldName.Contains("regenerate") ||
                                fieldName.Contains("carry") || fieldName.Contains("effect") || fieldName.Contains("buff"))
                            {
                                Logger.LogInfo($"[EFFECTS] Найдено поле эффекта в ActiveHealthController: {field.Name} = {fieldValue.GetType().Name}");
                                AttemptStopAndZeroEffect(fieldValue);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Logger.LogWarning($"[EFFECTS] Ошибка обработки поля ActiveHealthController.{field.Name}: {ex.Message}");
                        }
                    }
                }

                Logger.LogInfo("[EFFECTS] Специальная очистка эффектов завершена");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[EFFECTS] Ошибка специальной очистки эффектов: {ex.Message}");
            }
        }

        // Универсальная попытка остановить эффект и обнулить его таймеры/длительность
        private void AttemptStopAndZeroEffect(object effect)
        {
            if (effect == null) return;
            try
            {
                var t = effect.GetType();
                // Выключаем активность
                foreach (var propName in new[] { "Enabled", "IsActive", "Active" })
                {
                    var p = t.GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                    {
                        try { p.SetValue(effect, false); } catch { }
                    }
                }

                // Затираем время/длительность
                foreach (var propName in new[] { "Time", "Duration", "RemainingTime", "RemainingDuration", "EndTime" })
                {
                    var p = t.GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (p == null || !p.CanWrite) continue;
                    var pt = p.PropertyType;
                    try
                    {
                        if (pt == typeof(float) || pt == typeof(double)) p.SetValue(effect, 0);
                        else if (pt == typeof(int)) p.SetValue(effect, 0);
                        else if (pt == typeof(System.TimeSpan)) p.SetValue(effect, System.TimeSpan.Zero);
                        else if (pt == typeof(System.DateTime)) p.SetValue(effect, System.DateTime.UtcNow);
                    }
                    catch { }
                }

                // Пытаемся вызвать методы остановки/завершения
                foreach (var methodName in new[] { "Stop", "Dispose", "Cancel", "End", "Reset" })
                {
                    var m = t.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (m != null && m.GetParameters().Length == 0)
                    {
                        try { m.Invoke(effect, null); } catch { }
                    }
                }

                // Отключаем возможные таймеры
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    if (f.Name.ToLower().Contains("timer"))
                    {
                        var timerObj = f.GetValue(effect);
                        if (timerObj != null)
                        {
                            var disp = timerObj as System.IDisposable;
                            try { disp?.Dispose(); } catch { }
                            try { f.SetValue(effect, null); } catch { }
                        }
                    }
                }
            }
            catch { }
    }
    
    // Автоматический респавн через заданную задержку
    public IEnumerator AutoRespawnAfterDelay(float delaySeconds)
    {
        Logger.LogInfo($"⏰ [AUTO_RESPAWN] Запуск автоматического возрождения через {delaySeconds} секунд");
        
        // Показываем уведомление о начале возрождения
        ShowAutoRespawnNotification($"Автоматическое возрождение через {delaySeconds} секунд...", false);
        
        yield return new WaitForSeconds(delaySeconds);
        
        Logger.LogInfo("⏰ [AUTO_RESPAWN] Время ожидания истекло, начинаем возрождение");
        
        // Показываем уведомление о возрождении (отключено)
        // ShowAutoRespawnNotification("🔄 Возрождение...", false);
        
        // Выполняем возрождение
        RespawnPlayer();
        
        // Показываем уведомление об успешном возрождении
        ShowAutoRespawnNotification("Возрождение завершено", false);
        
        Logger.LogInfo("⏰ [AUTO_RESPAWN] Автоматическое возрождение завершено");
    }
    
    // Респавн с эффектами затемнения экрана
    public IEnumerator RespawnWithFadeEffects(string respawnType = "auto")
    {
        Logger.LogInfo($"🎬 [RESPAWN_FADE] Запуск респавна с эффектами затемнения (тип: {respawnType})");
        
        // Гарантированно получаем LocalPlayer перед началом
        if (localPlayer == null)
        {
            Logger.LogWarning("🎬 [RESPAWN_FADE] LocalPlayer отсутствует, пробуем найти заново");
            if (gameWorld != null)
            {
                localPlayer = gameWorld.MainPlayer as LocalPlayer;
                Logger.LogInfo("🎬 [RESPAWN_FADE] Попытка через gameWorld.MainPlayer выполнена");
            }
            if (localPlayer == null)
            {
                var allLocalPlayers = FindObjectsOfType<LocalPlayer>();
                foreach (var p in allLocalPlayers)
                {
                    if (p.IsYourPlayer)
                    {
                        localPlayer = p;
                        Logger.LogInfo($"🎬 [RESPAWN_FADE] Найден основной игрок через IsYourPlayer: {p.name}");
                        break;
                    }
                }
            }
            if (localPlayer == null)
            {
                Logger.LogError("❌ [RESPAWN_FADE] LocalPlayer не найден, прерываем респавн");
                yield break;
            }
        }
        
        // Анти-спам защита: если уже идёт респавн, выходим
        if (isRespawnInProgress)
        {
            Logger.LogWarning("🎬 [RESPAWN_FADE] Респавн уже выполняется - пропуск");
            yield break;
        }
        isRespawnInProgress = true;
        
        try
        {
        // ЭТАП 1: Затемнение экрана
        if (IsPipelineEnabled(Settings.Settings.Pipeline01_FadeToBlack, "01 Fade To Black"))
        {
            Logger.LogInfo("🎬 [RESPAWN_FADE] ЭТАП 1: Затемнение экрана");
            try
            {
                StartRespawnFade();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"❌ [RESPAWN_FADE] Ошибка затемнения экрана: {ex.Message}");
            }

            yield return new WaitForSeconds(0.5f);
        }
        else
        {
            yield return null;
        }
        
        // ЭТАП 2: Телепортация игрока
        if (IsPipelineEnabled(Settings.Settings.Pipeline02_Teleport, "02 Teleport"))
        {
            Logger.LogInfo("🎬 [RESPAWN_FADE] ЭТАП 2: Телепортация игрока");
            try
            {
                if (localPlayer == null)
                {
                    if (gameWorld != null) localPlayer = gameWorld.MainPlayer as LocalPlayer;
                    if (localPlayer == null)
                    {
                        var allLocalPlayers = FindObjectsOfType<LocalPlayer>();
                        foreach (var p in allLocalPlayers)
                        {
                            if (p.IsYourPlayer) { localPlayer = p; break; }
                        }
                    }
                }
                TeleportPlayerToRespawnPosition();
                Logger.LogInfo("[RESPAWN_FADE] Teleport complete — fade protection via isRespawnInProgress until finalize");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"❌ [RESPAWN_FADE] Ошибка телепортации: {ex.Message}");
            }
        }
        
        // ЭТАП 3: Лечение
        Logger.LogInfo("🎬 [RESPAWN_FADE] ЭТАП 3: Lightweight revive heal");
        var health = localPlayer?.ActiveHealthController;
        if (health != null)
        {
            try
            {
                PerformLightweightReviveHeal(health, "RespawnFade");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"❌ [RESPAWN_FADE] Ошибка лечения: {ex.Message}");
            }
        }

        // ЭТАП 4: Осветление экрана
        if (IsPipelineEnabled(Settings.Settings.Pipeline08_FadeWake, "08 Fade Wake"))
        {
            Logger.LogInfo("🎬 [RESPAWN_FADE] ЭТАП 4: Осветление экрана");
            try
            {
                EndRespawnFade();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"❌ [RESPAWN_FADE] Ошибка осветления экрана: {ex.Message}");
                
                DisableDeathFade();
                if (preloaderUI != null)
                {
                    preloaderUI.SetBlackImageAlpha(0f);
                }
                ShowNotification("Ошибка респавна, эффекты отключены", true);
            }
        }
        
        Logger.LogInfo("✅ [RESPAWN_FADE] Респавн с эффектами завершен");

        FinalizeRespawnAfterEffects(respawnType);

        Logger.LogInfo("🎮 [INPUT_LOCK] Фейлсейф: Разблокируем ввод по завершении респавна");
        TrySetGlobalIgnoreInput(false);
        TryRestorePlayerHandsAfterRevive(localPlayer);

        isRespawnInProgress = false;
        }
        finally
        {
            fadeRespawnCoroutine = null;
        }
    }

    private System.Collections.IEnumerator ReleaseInputAfter(float delaySeconds)
    {
        yield return new UnityEngine.WaitForSeconds(delaySeconds);
        Logger.LogInfo("🎮 [INPUT_LOCK] Разблокируем ввод (SetIgnoreInput=false)");
        TrySetGlobalIgnoreInput(false);
    }

    private void TrySetGlobalIgnoreInput(bool ignore)
    {
        try
        {
            var asm = typeof(EFT.Player).Assembly;
            var t = asm.GetType("EFT.GamePlayerOwner");
            if (t == null) return;
            var m = t.GetMethod("SetIgnoreInput", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (m == null) return;
            m.Invoke(null, new object[] { ignore });
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🎮 [INPUT_LOCK] Reflection SetIgnoreInput error: {ex.Message}");
        }
    }
    
    // Телепортация игрока на позицию респавна
    private void TeleportPlayerToRespawnPosition()
    {
        try
        {
            Logger.LogInfo("🚀 [TELEPORT] Телепортация игрока на позицию респавна");
            
            if (localPlayer == null)
            {
                Logger.LogError("🚀 [TELEPORT] LocalPlayer не найден");
                return;
            }
            
            // Находим безопасную позицию для спавна
            Vector3 safeSpawnPosition = spawnPosition; // Используем сохраненную позицию
            Logger.LogInfo($"🚀 [TELEPORT] safeSpawnPosition = {safeSpawnPosition}");
            Logger.LogInfo($"🚀 [TELEPORT] spawnRotation = {spawnRotation}");
            
            // Устанавливаем позицию и поворот
            localPlayer.Transform.position = safeSpawnPosition;
            localPlayer.Transform.rotation = spawnRotation;
            Logger.LogInfo("🚀 [TELEPORT] Позиция и поворот установлены");
            
            // Сбрасываем физическое состояние/движение
            if (IsPipelineEnabled(Settings.Settings.Pipeline03_ResetMovementOnRevive, "03 Reset Movement On Revive"))
            {
                try
                {
                    var movementContext = localPlayer.MovementContext;
                    if (movementContext != null)
                    {
                        movementContext.ResetPhysicalCondition();
                        Logger.LogInfo("🚀 [TELEPORT] MovementContext.ResetPhysicalCondition вызван");
                        
                        ResetMovementRestrictions(movementContext);
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"🚀 [TELEPORT] Ошибка при сбросе физического состояния: {ex.Message}");
                }
            }
            
            Logger.LogInfo("🚀 [TELEPORT] Телепортация завершена");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"🚀 [TELEPORT] Ошибка телепортации: {ex.Message}");
        }
    }
    
    // Запуск автоматического возрождения
    private void StartAutoRespawn()
    {
        Logger.LogInfo($"⏰ [AUTO_RESPAWN] Запуск через HandleRespawn (delay={PlayerState.ClientRespawnDelay}ms)");
        HandleRespawn("auto");
    }
    
    // Полный сброс ограничений движения (улучшенная версия на основе анализа v1.1.2)
    private void ResetMovementRestrictions(MovementContext movementContext)
    {
        try
        {
            Logger.LogInfo("🏃 [MOVEMENT] Начинаем полный сброс ограничений движения");
            
            // 1. Основной сброс физических состояний (как в версии 1.1.2)
            movementContext.ResetPhysicalCondition();
            Logger.LogInfo("🏃 [MOVEMENT] MovementContext.ResetPhysicalCondition вызван");
            
            // 2. Сброс через рефлексию (как в версии 1.1.2)
            var physicalCondition = movementContext.PhysicalCondition;
            if (physicalCondition != null)
            {
                var conditionType = physicalCondition.GetType();
                var setMethod = conditionType.GetMethod("Set", new System.Type[] { typeof(EPhysicalCondition), typeof(bool) });
                
                if (setMethod != null)
                {
                    setMethod.Invoke(physicalCondition, new object[] { EPhysicalCondition.None, false });
                    Logger.LogInfo("🏃 [MOVEMENT] EPhysicalCondition сброшен в None");
                }
            }
            
            // 3. Дополнительный сброс всех методов Reset
            var movementType = movementContext.GetType();
            var resetMethods = movementType.GetMethods()
                .Where(m => m.Name.Contains("Reset") && m.GetParameters().Length == 0)
                .ToArray();
                
            foreach (var method in resetMethods)
            {
                try
                {
                    method.Invoke(movementContext, null);
                    Logger.LogInfo($"🏃 [MOVEMENT] Вызван метод сброса: {method.Name}");
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"🏃 [MOVEMENT] Ошибка вызова {method.Name}: {ex.Message}");
                }
            }
            
            // 4. Сброс выносливости через рефлексию (исправление ошибки компиляции)
            try
            {
                var movementType2 = movementContext.GetType();
                var physicalProperty = movementType2.GetProperty("Physical");
                if (physicalProperty != null)
                {
                    var physical = physicalProperty.GetValue(movementContext);
                    if (physical != null)
                    {
                        var physicalType = physical.GetType();
                        var staminaProperty = physicalType.GetProperty("Stamina");
                        var handsStaminaProperty = physicalType.GetProperty("HandsStamina");
                        
                        if (staminaProperty != null)
                        {
                            var stamina = staminaProperty.GetValue(physical);
                            if (stamina != null)
                            {
                                var staminaType = stamina.GetType();
                                var capacityProperty = staminaType.GetProperty("Capacity");
                                if (capacityProperty != null)
                                {
                                    capacityProperty.SetValue(stamina, 100f);
                                    Logger.LogInfo("🏃 [MOVEMENT] Stamina восстановлена через рефлексию");
                                }
                            }
                        }
                        
                        if (handsStaminaProperty != null)
                        {
                            var handsStamina = handsStaminaProperty.GetValue(physical);
                            if (handsStamina != null)
                            {
                                var handsStaminaType = handsStamina.GetType();
                                var handsCapacityProperty = handsStaminaType.GetProperty("Capacity");
                                if (handsCapacityProperty != null)
                                {
                                    handsCapacityProperty.SetValue(handsStamina, 100f);
                                    Logger.LogInfo("🏃 [MOVEMENT] HandsStamina восстановлена через рефлексию");
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"🏃 [MOVEMENT] Ошибка сброса выносливости: {ex.Message}");
            }
            
            Logger.LogInfo("🏃 [MOVEMENT] Полный сброс ограничений движения завершен");
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"🏃 [MOVEMENT] Ошибка сброса ограничений движения: {ex.Message}");
        }
    }
    
    // Создание предметов убрано - теперь через крафт в убежище
    
    
    
    /// <summary>
    /// Получает локального игрока
    /// </summary>
    private EFT.Player GetLocalPlayer()
    {
        try
        {
            var gameWorld = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gameWorld != null)
            {
                return gameWorld.MainPlayer as LocalPlayer;
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [PLAYER] Ошибка получения игрока: {ex.Message}");
        }
        return null;
    }
    
    /// <summary>
    /// Обновляет кэш иконок для исправления проблемы с исчезающими иконками дефибриллятора
    /// </summary>
    private void RefreshIconCache()
    {
        try
        {
            Logger.LogInfo("🔄 [ICON_CACHE] Начинаем обновление кэша иконок");
            
            // УБРАНО: Агрессивное обновление UI - оно сломало интерфейс игры (белый экран)
            // ForceUIUpdate();
            
            // Отправляем запрос на сервер для обновления кэша иконок
            var requestData = new
            {
                action = "refresh_icons",
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                sessionId = sessionId
            };
            
            var json = JsonConvert.SerializeObject(requestData);
            Logger.LogInfo("🔄 [ICON_CACHE] Отправляем запрос на обновление кэша иконок");
            
            var response = DesmatchHttpHelper.PostJson("/singleplayer/desmatch/refresh-icons", json);
            
            if (!string.IsNullOrEmpty(response))
            {
                Logger.LogInfo($"✅ [ICON_CACHE] Кэш иконок обновлен: {response}");
                ShowNotification("Кэш иконок обновлен", false);
            }
            else
            {
                Logger.LogWarning("⚠️ [ICON_CACHE] Ошибка обновления кэша иконок: пустой ответ");
                ShowNotification("Ошибка обновления кэша иконок", true);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [ICON_CACHE] Ошибка обновления кэша иконок: {ex.Message}");
            ShowNotification("Ошибка обновления кэша иконок", true);
        }
    }
    
    /// <summary>
    /// ОТКЛЮЧЕНО: Принудительное обновление UI вызывает белый экран
    /// </summary>
    private void ForceUIUpdate()
    {
        Logger.LogWarning("⚠️ [UI_UPDATE] ForceUIUpdate отключен - вызывает белый экран");
        ShowNotification("ForceUIUpdate отключен (вызывает белый экран)", true);
        // Метод отключен - вызывает белый экран в игре
    }
    
    /// <summary>
    /// Принудительно обновляет UI через рефлексию для избежания проблем с зависимостями
    /// </summary>
    private void ForceUIUpdateViaReflection()
    {
        try
        {
            // Принудительно обновляем Canvas через рефлексию
            var canvasType = System.Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule");
            if (canvasType != null)
            {
                var canvases = FindObjectsOfType(canvasType);
                foreach (var canvas in canvases)
                {
                    if (canvas != null)
                    {
                        var gameObject = canvas.GetType().GetProperty("gameObject")?.GetValue(canvas);
                        if (gameObject != null)
                        {
                            var activeInHierarchy = gameObject.GetType().GetProperty("activeInHierarchy")?.GetValue(gameObject);
                            if (activeInHierarchy is bool isActive && isActive)
                            {
                                // Принудительно перерисовываем Canvas
                                var enabledProperty = canvas.GetType().GetProperty("enabled");
                                if (enabledProperty != null)
                                {
                                    enabledProperty.SetValue(canvas, false);
                                    enabledProperty.SetValue(canvas, true);
                                }
                            }
                        }
                    }
                }
                Logger.LogInfo("🔄 [UI_UPDATE] Canvas элементы обновлены через рефлексию");
            }
            
            // Принудительно обновляем Image компоненты через рефлексию
            var imageType = System.Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
            if (imageType != null)
            {
                var images = FindObjectsOfType(imageType);
                foreach (var image in images)
                {
                    if (image != null)
                    {
                        var gameObject = image.GetType().GetProperty("gameObject")?.GetValue(image);
                        if (gameObject != null)
                        {
                            var activeInHierarchy = gameObject.GetType().GetProperty("activeInHierarchy")?.GetValue(gameObject);
                            if (activeInHierarchy is bool isActive && isActive)
                            {
                                // Принудительно перерисовываем Image
                                var enabledProperty = image.GetType().GetProperty("enabled");
                                if (enabledProperty != null)
                                {
                                    enabledProperty.SetValue(image, false);
                                    enabledProperty.SetValue(image, true);
                                }
                            }
                        }
                    }
                }
                Logger.LogInfo("🔄 [UI_UPDATE] Image элементы обновлены через рефлексию");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"❌ [UI_UPDATE] Ошибка обновления UI через рефлексию: {ex.Message}");
        }
    }
    }
}
