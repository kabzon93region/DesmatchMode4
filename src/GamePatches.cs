using HarmonyLib;
using EFT;
using EFT.HealthSystem;
using UnityEngine;
using System.Reflection;
using Fika.Core.Main.Players;
using Fika.Core.Networking.Packets.Player.Common.SubPackets;

[HarmonyPatch]
public class GamePatches
{
    private static DesmatchMode4.DesmatchMode4Plugin desmatchMode;

    public static void SetPlugin(DesmatchMode4.DesmatchMode4Plugin plugin)
    {
        desmatchMode = plugin;
        UnityEngine.Debug.Log("[GamePatches] Plugin reference registered from Awake");
    }

    private static DesmatchMode4.DesmatchMode4Plugin ResolvePlugin()
    {
        if (desmatchMode == null)
        {
            desmatchMode = Object.FindObjectOfType<DesmatchMode4.DesmatchMode4Plugin>();
        }

        return desmatchMode;
    }
    
    // Патч для отслеживания входа в рейд (ранний этап - только установка флага)
    [HarmonyPatch(typeof(GameWorld), "Start")]
    [HarmonyPostfix]
    public static void Start_Postfix(GameWorld __instance)
    {
        UnityEngine.Debug.Log("[HARMONY] DesmatchMode: GameWorld.Start вызван");
        
        var plugin = ResolvePlugin();
        if (plugin == null) 
        {
            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: plugin = null");
            return;
        }
        
        var localPlayer = __instance.MainPlayer;
        if (localPlayer != null)
        {
            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: MainPlayer найден, isInRaid = true");
            plugin.SetInRaid(true);
        }
        else
        {
            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: MainPlayer не найден");
        }
    }
    
    // Патч для отслеживания начала игры (когда камера входит в персонажа)
    [HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
    [HarmonyPostfix]
    public static void OnGameStarted_Postfix(GameWorld __instance)
    {
        UnityEngine.Debug.Log("[HARMONY] DesmatchMode: GameWorld.OnGameStarted вызван");
        
        var plugin = ResolvePlugin();
        if (plugin == null) 
        {
            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: plugin = null");
            return;
        }
        
        var localPlayer = __instance.MainPlayer;
        if (localPlayer != null)
        {
            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: SavePlayerData из OnGameStarted");
            plugin.SavePlayerData(localPlayer);
            plugin.ShowPlayerFoundNotification();
        }
        else
        {
            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: MainPlayer не найден в OnGameStarted");
        }
    }
    
    // Патч для предотвращения смерти игрока (на основе RevivalMod)
    [HarmonyPatch(typeof(ActiveHealthController), "Kill")]
    [HarmonyPrefix]
    public static bool Kill_Prefix(ActiveHealthController __instance, EDamageType damageType)
    {
        var plugin = ResolvePlugin();
        if (plugin == null) return true;
        
        try
        {
            FieldInfo playerField = AccessTools.Field(typeof(ActiveHealthController), "Player");
            if (playerField == null) return true;

            Player player = playerField.GetValue(__instance) as Player;
            if (player == null) return true;

            if (!player.IsYourPlayer || player.IsAI) return true;

            UnityEngine.Debug.Log($"[HARMONY] DesmatchMode: перехват смерти {player.ProfileId} от {damageType}");

            if (!plugin.IsDesmatchModeEnabled())
            {
                UnityEngine.Debug.Log("[HARMONY] DesmatchMode: режим отключен, смерть разрешена");
                return true;
            }

            if (plugin.IsLocalPlayerDamageBlocked())
            {
                UnityEngine.Debug.Log("[HARMONY] DesmatchMode: Kill blocked (invuln/respawn)");
                return false;
            }

            if (plugin.ShouldBlockDeathHandling())
            {
                UnityEngine.Debug.Log("[HARMONY] DesmatchMode: смерть уже обрабатывается или респавн в процессе, Kill заблокирован");
                return false;
            }

            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: критическое состояние вместо смерти");
            plugin.SetPlayerCriticalState();
            return false;
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"[HARMONY] DesmatchMode: ошибка Kill patch: {ex.Message}");
            return true;
        }
    }
    
    // Патч для блокировки урона при неуязвимости
    [HarmonyPatch(typeof(ActiveHealthController), "ChangeHealth")]
    [HarmonyPrefix]
    public static bool ChangeHealth_Prefix(ActiveHealthController __instance, EBodyPart bodyPart, float value, DamageInfoStruct damageInfo)
    {
        var plugin = ResolvePlugin();
        if (plugin == null) return true;

        if (plugin.IsTherapeuticDamageInProgress()) return true;
        
        try
        {
            FieldInfo playerField = AccessTools.Field(typeof(ActiveHealthController), "Player");
            if (playerField == null) return true;

            Player player = playerField.GetValue(__instance) as Player;
            if (player == null) return true;

            if (!player.IsYourPlayer || player.IsAI) return true;

            if (value < 0f && plugin.IsLocalPlayerDamageBlocked())
            {
                if (IsNonCombatHealthChange(damageInfo))
                {
                    return true;
                }

                UnityEngine.Debug.Log($"[HARMONY] DesmatchMode: блок урона {value} для {bodyPart} (invuln/respawn)");
                return false;
            }
            
            return true;
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"[HARMONY] DesmatchMode: ошибка ChangeHealth patch: {ex.Message}");
            return true;
        }
    }
    
    [HarmonyPatch(typeof(Player), "ApplyDamageInfo")]
    [HarmonyPrefix]
    public static bool ApplyDamageInfo_Prefix(Player __instance, DamageInfoStruct damageInfo, EBodyPart bodyPartType, EBodyPartColliderType colliderType, float absorbed)
    {
        var plugin = ResolvePlugin();
        if (plugin == null || !plugin.IsDesmatchModeEnabled()) return true;
        if (__instance == null || string.IsNullOrEmpty(__instance.ProfileId)) return true;

        if (plugin.IsTherapeuticDamageInProgress() && __instance.IsYourPlayer) return true;

        if (__instance.IsYourPlayer && plugin.IsLocalPlayerDamageBlocked())
        {
            UnityEngine.Debug.Log($"[HARMONY] DesmatchMode: блок ApplyDamageInfo local ({bodyPartType}) invuln/respawn");
            return false;
        }

        if (plugin.IsProfileInvulnerable(__instance.ProfileId))
        {
            UnityEngine.Debug.Log($"[HARMONY] DesmatchMode: блок ApplyDamageInfo для {__instance.ProfileId} ({bodyPartType})");
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(FikaPlayer), "HandleDamagePacket")]
    [HarmonyPrefix]
    public static bool FikaPlayer_HandleDamagePacket_Prefix(FikaPlayer __instance, DamagePacket packet)
    {
        var plugin = ResolvePlugin();
        if (plugin == null || !plugin.IsDesmatchModeEnabled()) return true;
        if (__instance == null || !__instance.IsYourPlayer) return true;
        if (plugin.IsTherapeuticDamageInProgress()) return true;

        if (plugin.IsLocalPlayerDamageBlocked())
        {
            UnityEngine.Debug.Log("[HARMONY] DesmatchMode: block Fika HandleDamagePacket (invuln/respawn)");
            return false;
        }

        return true;
    }

    private static bool IsNonCombatHealthChange(DamageInfoStruct damageInfo)
    {
        var damageType = damageInfo.DamageType;
        return damageType == EDamageType.Existence
            || damageType == EDamageType.Dehydration
            || damageType == EDamageType.Exhaustion
            || damageType == EDamageType.Stimulator
            || damageType == EDamageType.Medicine;
    }
}
