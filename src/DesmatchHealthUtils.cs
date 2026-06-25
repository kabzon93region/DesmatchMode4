using System;

using System.Collections.Generic;

using EFT.HealthSystem;

using UnityEngine;



namespace DesmatchMode4

{

    /// <summary>

    /// Общие утилиты для восстановления здоровья в DesmatchMode.

    /// Чтение health API — через DesmatchHealthReflection (EFT 16.9 / Fika).

    /// </summary>

    public static class DesmatchHealthUtils

    {

        public const float DEFAULT_HEALTH_VALUE = 100f;

        public const float DEFAULT_ENERGY_VALUE = 100f;

        public const float DEFAULT_HYDRATION_VALUE = 100f;



        public static readonly EBodyPart[] BODY_PARTS_ORDER = new EBodyPart[]

        {

            EBodyPart.Head,

            EBodyPart.Chest,

            EBodyPart.Stomach,

            EBodyPart.LeftArm,

            EBodyPart.RightArm,

            EBodyPart.LeftLeg,

            EBodyPart.RightLeg

        };



        public static readonly string[] MEDICAL_ITEMS_PRIORITY = new string[]

        {

            "Surv12",

            "Method15",

            "Method16",

            "IFAK",

            "Car",

            "Salewa",

            "Grizzly"

        };



        public static bool TryGetEnergyValues(ActiveHealthController health, out float current, out float maximum)

        {

            return DesmatchHealthReflection.TryGetEnergyValues(health, out current, out maximum);

        }



        public static bool TryGetHydrationValues(ActiveHealthController health, out float current, out float maximum)

        {

            return DesmatchHealthReflection.TryGetHydrationValues(health, out current, out maximum);

        }



        public static bool TryGetBodyPartValues(ActiveHealthController health, EBodyPart bodyPart, out float current, out float maximum)

        {

            return DesmatchHealthReflection.TryGetBodyPartValues(health, bodyPart, out current, out maximum);

        }



        public static DesmatchHealthPayload CreateHealthPayload(ActiveHealthController health)

        {

            var payload = new DesmatchHealthPayload();

            if (health == null) return payload;



            DesmatchHealthReflection.TryGetEnergyValues(health, out payload.energy, out payload.maxEnergy);

            DesmatchHealthReflection.TryGetHydrationValues(health, out payload.hydration, out payload.maxHydration);

            payload.health = GetTotalHealth(health);

            payload.maxHealth = GetMaxTotalHealth(health);

            if (payload.maxHealth <= 0f)

            {

                payload.maxHealth = DEFAULT_HEALTH_VALUE;

            }

            return payload;

        }



        public static float GetTotalHealth(ActiveHealthController health)

        {

            if (health == null) return 0f;



            float totalHealth = 0f;

            foreach (var bodyPart in BODY_PARTS_ORDER)

            {

                if (TryGetBodyPartValues(health, bodyPart, out float current, out _))

                {

                    totalHealth += current;

                }

            }

            return totalHealth;

        }



        public static float GetMaxTotalHealth(ActiveHealthController health)

        {

            if (health == null) return 0f;



            float maxHealth = 0f;

            foreach (var bodyPart in BODY_PARTS_ORDER)

            {

                if (TryGetBodyPartValues(health, bodyPart, out _, out float maximum))

                {

                    maxHealth += maximum;

                }

            }

            return maxHealth;

        }



        public static bool IsPlayerFullyHealthy(ActiveHealthController health)

        {

            if (health == null) return false;



            foreach (var bodyPart in BODY_PARTS_ORDER)

            {

                if (!TryGetBodyPartValues(health, bodyPart, out float current, out float maximum))

                {

                    return false;

                }



                if (current < maximum)

                {

                    return false;

                }

            }



            TryGetEnergyValues(health, out float energy, out float maxEnergy);

            TryGetHydrationValues(health, out float hydration, out float maxHydration);

            if (energy < maxEnergy || hydration < maxHydration)

            {

                return false;

            }



            return true;

        }



        public static float GetHealthPercentage(ActiveHealthController health)

        {

            if (health == null) return 0f;



            float currentHealth = GetTotalHealth(health);

            float maxHealth = GetMaxTotalHealth(health);

            if (maxHealth <= 0f) return 0f;

            return (currentHealth / maxHealth) * 100f;

        }



        public static float ValidateHealthValue(float value) => Mathf.Clamp(value, 0f, 100f);

        public static float ValidateEnergyValue(float value) => Mathf.Clamp(value, 0f, 100f);

        public static float ValidateHydrationValue(float value) => Mathf.Clamp(value, 0f, 100f);



        public static Dictionary<string, object> GetHealthInfo(ActiveHealthController health)

        {

            var info = new Dictionary<string, object>();

            if (health == null)

            {

                info["error"] = "Health controller is null";

                return info;

            }



            try

            {

                info["total_health"] = GetTotalHealth(health);

                info["max_health"] = GetMaxTotalHealth(health);

                info["health_percentage"] = GetHealthPercentage(health);

                TryGetEnergyValues(health, out float energy, out float maxEnergy);

                TryGetHydrationValues(health, out float hydration, out float maxHydration);

                info["energy"] = energy;

                info["max_energy"] = maxEnergy;

                info["hydration"] = hydration;

                info["max_hydration"] = maxHydration;

                info["is_fully_healthy"] = IsPlayerFullyHealthy(health);



                var bodyPartsInfo = new Dictionary<string, object>();

                foreach (var bodyPart in BODY_PARTS_ORDER)

                {

                    if (!TryGetBodyPartValues(health, bodyPart, out float current, out float maximum))

                    {

                        continue;

                    }



                    bodyPartsInfo[bodyPart.ToString()] = new

                    {

                        current,

                        maximum,

                        percentage = maximum > 0f ? (current / maximum) * 100f : 0f

                    };

                }

                info["body_parts"] = bodyPartsInfo;

            }

            catch (Exception ex)

            {

                info["error"] = ex.Message;

            }



            return info;

        }

    }



    public class DesmatchHealthPayload

    {

        public float energy = DesmatchHealthUtils.DEFAULT_ENERGY_VALUE;

        public float hydration = DesmatchHealthUtils.DEFAULT_HYDRATION_VALUE;

        public float health = DesmatchHealthUtils.DEFAULT_HEALTH_VALUE;

        public float maxEnergy = DesmatchHealthUtils.DEFAULT_ENERGY_VALUE;

        public float maxHydration = DesmatchHealthUtils.DEFAULT_HYDRATION_VALUE;

        public float maxHealth = DesmatchHealthUtils.DEFAULT_HEALTH_VALUE;

    }

}


