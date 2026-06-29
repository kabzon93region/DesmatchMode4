using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

namespace DesmatchMode4
{
    /// <summary>
    /// Reflection-доступ к health API EFT 16.9 / Fika ClientHealthController.
    /// Обходит Invalid generic instantiation на GClass3009&lt;T&gt; из модов.
    /// </summary>
    internal static class DesmatchHealthReflection
    {
        private static readonly Dictionary<Type, FieldInfo> HealthValue0FieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> HealthValue1FieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> BodyPartDictFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, MethodInfo> GetBodyPartHealthMethodCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> RestoreFullHealthMethodCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> FullRestoreBodyPartMethodCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> RemoveNegativeEffectsMethodCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> IsBodyPartDestroyedMethodCache = new Dictionary<Type, MethodInfo>();

        public static bool TryGetEnergyValues(object healthController, out float current, out float maximum)
        {
            current = DesmatchHealthUtils.DEFAULT_ENERGY_VALUE;
            maximum = DesmatchHealthUtils.DEFAULT_ENERGY_VALUE;
            return TryGetHealthValueField(healthController, "HealthValue_0", HealthValue0FieldCache, out current, out maximum);
        }

        public static bool TryGetHydrationValues(object healthController, out float current, out float maximum)
        {
            current = DesmatchHealthUtils.DEFAULT_HYDRATION_VALUE;
            maximum = DesmatchHealthUtils.DEFAULT_HYDRATION_VALUE;
            return TryGetHealthValueField(healthController, "HealthValue_1", HealthValue1FieldCache, out current, out maximum);
        }

        private static bool TryGetHealthValueField(
            object healthController,
            string fieldName,
            Dictionary<Type, FieldInfo> cache,
            out float current,
            out float maximum)
        {
            current = 0f;
            maximum = 100f;
            if (healthController == null) return false;

            try
            {
                var runtimeType = healthController.GetType();
                if (!cache.TryGetValue(runtimeType, out var fieldInfo))
                {
                    fieldInfo = AccessTools.Field(runtimeType, fieldName);
                    if (fieldInfo == null)
                    {
                        fieldInfo = FindFieldInHierarchy(runtimeType, fieldName);
                    }
                    cache[runtimeType] = fieldInfo;
                }

                if (fieldInfo == null) return false;

                var healthValue = fieldInfo.GetValue(healthController);
                if (healthValue == null) return false;

                return TryReadHealthValueObject(healthValue, out current, out maximum);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] {fieldName}: {ex.Message}");
                return false;
            }
        }

        private static bool TryReadHealthValueObject(object healthValue, out float current, out float maximum)
        {
            current = 0f;
            maximum = 100f;
            var type = healthValue.GetType();
            var currentProp = type.GetProperty("Current", BindingFlags.Instance | BindingFlags.Public);
            var maximumProp = type.GetProperty("Maximum", BindingFlags.Instance | BindingFlags.Public);
            if (currentProp == null || maximumProp == null) return false;

            current = ConvertToFloat(currentProp.GetValue(healthValue));
            maximum = ConvertToFloat(maximumProp.GetValue(healthValue));
            return true;
        }

        public static bool TryGetBodyPartValues(object healthController, EBodyPart bodyPart, out float current, out float maximum)
        {
            current = DesmatchHealthUtils.DEFAULT_HEALTH_VALUE;
            maximum = DesmatchHealthUtils.DEFAULT_HEALTH_VALUE;
            if (healthController == null) return false;

            try
            {
                if (TryInvokeGetBodyPartHealth(healthController, bodyPart, out var valueStruct))
                {
                    current = valueStruct.Current;
                    maximum = valueStruct.Maximum;
                    return true;
                }

                return TryGetBodyPartFromDictionary(healthController, bodyPart, out current, out maximum);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] BodyPart {bodyPart}: {ex.Message}");
                return false;
            }
        }

        private static bool TryInvokeGetBodyPartHealth(object healthController, EBodyPart bodyPart, out ValueStruct valueStruct)
        {
            valueStruct = default;
            var runtimeType = healthController.GetType();
            if (!GetBodyPartHealthMethodCache.TryGetValue(runtimeType, out var method))
            {
                method = AccessTools.Method(runtimeType, "GetBodyPartHealth", new[] { typeof(EBodyPart), typeof(bool) })
                         ?? FindMethodInHierarchy(runtimeType, "GetBodyPartHealth", typeof(EBodyPart), typeof(bool));
                GetBodyPartHealthMethodCache[runtimeType] = method;
            }

            if (method == null) return false;

            var result = method.Invoke(healthController, new object[] { bodyPart, false });
            if (result is ValueStruct vs)
            {
                valueStruct = vs;
                return true;
            }

            if (result != null)
            {
                var resultType = result.GetType();
                valueStruct.Current = ConvertToFloat(resultType.GetField("Current")?.GetValue(result));
                valueStruct.Maximum = ConvertToFloat(resultType.GetField("Maximum")?.GetValue(result));
                return true;
            }

            return false;
        }

        private static bool TryGetBodyPartFromDictionary(object healthController, EBodyPart bodyPart, out float current, out float maximum)
        {
            current = 0f;
            maximum = 100f;
            var runtimeType = healthController.GetType();
            if (!BodyPartDictFieldCache.TryGetValue(runtimeType, out var dictField))
            {
                dictField = AccessTools.Field(runtimeType, "Dictionary_0_1")
                            ?? AccessTools.Field(runtimeType, "Dictionary_0")
                            ?? FindFieldInHierarchy(runtimeType, "Dictionary_0_1")
                            ?? FindFieldInHierarchy(runtimeType, "Dictionary_0");
                BodyPartDictFieldCache[runtimeType] = dictField;
            }

            if (dictField == null) return false;

            var dictObj = dictField.GetValue(healthController) as IDictionary;
            if (dictObj == null || !dictObj.Contains(bodyPart)) return false;

            var bodyPartState = dictObj[bodyPart];
            if (bodyPartState == null) return false;

            var stateType = bodyPartState.GetType();
            var healthField = stateType.GetField("Health", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (healthField == null) return false;

            var healthValue = healthField.GetValue(bodyPartState);
            if (healthValue == null) return false;

            return TryReadHealthValueObject(healthValue, out current, out maximum);
        }

        public static bool IsBodyPartDestroyed(object healthController, EBodyPart bodyPart)
        {
            if (healthController == null) return false;

            try
            {
                var runtimeType = healthController.GetType();
                if (!IsBodyPartDestroyedMethodCache.TryGetValue(runtimeType, out var method))
                {
                    method = AccessTools.Method(runtimeType, "IsBodyPartDestroyed", new[] { typeof(EBodyPart) })
                             ?? FindMethodInHierarchy(runtimeType, "IsBodyPartDestroyed", typeof(EBodyPart));
                    IsBodyPartDestroyedMethodCache[runtimeType] = method;
                }

                if (method != null)
                {
                    return method.Invoke(healthController, new object[] { bodyPart }) is bool destroyed && destroyed;
                }

                if (TryGetBodyPartValues(healthController, bodyPart, out float current, out _))
                {
                    return current <= 0f;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] IsBodyPartDestroyed {bodyPart}: {ex.Message}");
            }

            return false;
        }

        public static bool TryRestoreFullHealth(object healthController)
        {
            return TryInvokeNoArgs(healthController, "RestoreFullHealth", RestoreFullHealthMethodCache);
        }

        public static bool TryFullRestoreBodyPart(object healthController, EBodyPart bodyPart)
        {
            if (healthController == null) return false;

            try
            {
                var runtimeType = healthController.GetType();
                if (!FullRestoreBodyPartMethodCache.TryGetValue(runtimeType, out var method))
                {
                    method = AccessTools.Method(runtimeType, "FullRestoreBodyPart", new[] { typeof(EBodyPart) })
                             ?? FindMethodInHierarchy(runtimeType, "FullRestoreBodyPart", typeof(EBodyPart));
                    FullRestoreBodyPartMethodCache[runtimeType] = method;
                }

                if (method == null) return false;
                return method.Invoke(healthController, new object[] { bodyPart }) is bool result && result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] FullRestoreBodyPart {bodyPart}: {ex.Message}");
                return false;
            }
        }

        public static bool TryRemoveNegativeEffects(object healthController, EBodyPart bodyPart)
        {
            if (healthController == null) return false;

            try
            {
                var runtimeType = healthController.GetType();
                if (!RemoveNegativeEffectsMethodCache.TryGetValue(runtimeType, out var method))
                {
                    method = AccessTools.Method(runtimeType, "RemoveNegativeEffects", new[] { typeof(EBodyPart) })
                             ?? FindMethodInHierarchy(runtimeType, "RemoveNegativeEffects", typeof(EBodyPart));
                    RemoveNegativeEffectsMethodCache[runtimeType] = method;
                }

                if (method == null) return false;
                method.Invoke(healthController, new object[] { bodyPart });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] RemoveNegativeEffects {bodyPart}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ForceRemove всех активных эффектов через IReadOnlyList_0 (синхронизация RemoveEffect в Fika).
        /// </summary>
        public static int TryForceRemoveAllActiveEffects(object healthController)
        {
            if (healthController == null) return 0;

            int removed = 0;
            try
            {
                var runtimeType = healthController.GetType();
                var effectsList = AccessTools.Property(runtimeType, "IReadOnlyList_0")?.GetValue(healthController)
                                  ?? AccessTools.Field(runtimeType, "IReadOnlyList_0")?.GetValue(healthController);

                if (effectsList is not IEnumerable enumerable)
                {
                    return 0;
                }

                var snapshot = new List<object>();
                foreach (var effect in enumerable)
                {
                    if (effect != null)
                    {
                        snapshot.Add(effect);
                    }
                }

                for (int i = snapshot.Count - 1; i >= 0; i--)
                {
                    var effect = snapshot[i];
                    var forceRemove = effect.GetType().GetMethod("ForceRemove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (forceRemove == null) continue;

                    try
                    {
                        forceRemove.Invoke(effect, null);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[HEALTH_REFLECTION] ForceRemove: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] TryForceRemoveAllActiveEffects: {ex.Message}");
            }

            return removed;
        }

        /// <summary>
        /// Восстанавливает флаги метаболизма после revive (IsAlive, Boolean_0, DamageCoeff, эффекты).
        /// Kill_Prefix блокирует Kill(), поэтому IsAlive может остаться false без явного сброса.
        /// </summary>
        public static bool TryRestorePostReviveMetabolism(object healthController)
        {
            if (healthController == null) return false;

            bool ok = false;
            try
            {
                if (healthController is ActiveHealthController activeHealth)
                {
                    activeHealth.IsAlive = true;
                    activeHealth.SetDamageCoeff(1f);
                    activeHealth.DamageMultiplier = 1f;
                    activeHealth.UnpauseAllEffects();
                    ok = true;
                }
                else
                {
                    var runtimeType = healthController.GetType();
                    var isAliveProp = FindPropertyInHierarchy(runtimeType, "IsAlive");
                    isAliveProp?.SetValue(healthController, true);
                    ok = isAliveProp != null;

                    var unpause = AccessTools.Method(runtimeType, "UnpauseAllEffects", Type.EmptyTypes)
                                  ?? FindMethodInHierarchy(runtimeType, "UnpauseAllEffects");
                    unpause?.Invoke(healthController, null);
                }

                var boolean0Prop = FindPropertyInHierarchy(healthController.GetType(), "Boolean_0");
                if (boolean0Prop != null && boolean0Prop.CanWrite)
                {
                    boolean0Prop.SetValue(healthController, false);
                    ok = true;
                }

                var playerField = AccessTools.Field(healthController.GetType(), "Player")
                                  ?? FindFieldInHierarchy(healthController.GetType(), "Player");
                if (playerField?.GetValue(healthController) is global::EFT.Player player)
                {
                    player.UnpauseAllEffectsOnPlayer();
                    ok = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] TryRestorePostReviveMetabolism: {ex.Message}");
            }

            return ok;
        }

        /// <summary>
        /// Лёгкое лечение для revive: negative effects + full health + metabolism flags.
        /// Без ForceRemove всех эффектов и без сброса таймеров регенерации.
        /// </summary>
        public static bool TryLightweightReviveHeal(object healthController)
        {
            if (healthController == null) return false;

            bool ok = false;
            foreach (var bodyPart in DesmatchHealthUtils.BODY_PARTS_ORDER)
            {
                ok |= TryRemoveNegativeEffects(healthController, bodyPart);
            }

            ok |= TryRemoveNegativeEffects(healthController, EBodyPart.Common);
            ok |= TryRestoreFullHealth(healthController);
            ok |= TryRestorePostReviveMetabolism(healthController);

            try
            {
                var removeMed = AccessTools.Method(healthController.GetType(), "RemoveMedEffect", Type.EmptyTypes);
                removeMed?.Invoke(healthController, null);
                ok = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] RemoveMedEffect: {ex.Message}");
            }

            return ok;
        }

        /// <summary>
        /// Полное лечение с отправкой NetworkHealthSync (Fika coop).
        /// </summary>
        public static bool TryHealWithNetworkSync(object healthController)
        {
            if (healthController == null) return false;

            bool ok = false;
            foreach (var bodyPart in DesmatchHealthUtils.BODY_PARTS_ORDER)
            {
                ok |= TryRemoveNegativeEffects(healthController, bodyPart);
            }

            ok |= TryRemoveNegativeEffects(healthController, EBodyPart.Common);

            int removed = TryForceRemoveAllActiveEffects(healthController);
            ok |= removed > 0;

            ok |= TryRestoreFullHealth(healthController);

            try
            {
                var removeMed = AccessTools.Method(healthController.GetType(), "RemoveMedEffect", Type.EmptyTypes);
                removeMed?.Invoke(healthController, null);
                ok = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] RemoveMedEffect: {ex.Message}");
            }

            return ok;
        }

        private static bool TryInvokeNoArgs(object healthController, string methodName, Dictionary<Type, MethodInfo> cache)
        {
            if (healthController == null) return false;

            try
            {
                var runtimeType = healthController.GetType();
                if (!cache.TryGetValue(runtimeType, out var method))
                {
                    method = AccessTools.Method(runtimeType, methodName, Type.EmptyTypes)
                             ?? FindMethodInHierarchy(runtimeType, methodName);
                    cache[runtimeType] = method;
                }

                if (method == null) return false;
                method.Invoke(healthController, null);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HEALTH_REFLECTION] {methodName}: {ex.Message}");
                return false;
            }
        }

        private static PropertyInfo FindPropertyInHierarchy(Type type, string propertyName)
        {
            while (type != null)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null) return property;
                type = type.BaseType;
            }

            return null;
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        private static MethodInfo FindMethodInHierarchy(Type type, string methodName, params Type[] paramTypes)
        {
            while (type != null)
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, paramTypes, null);
                if (method != null) return method;
                type = type.BaseType;
            }
            return null;
        }

        private static float ConvertToFloat(object value)
        {
            if (value == null) return 0f;
            if (value is float f) return f;
            if (value is double d) return (float)d;
            if (value is int i) return i;
            return Convert.ToSingle(value);
        }
    }
}
