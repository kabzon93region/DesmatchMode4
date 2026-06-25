using System;
using UnityEngine;
using EFT;
using Fika.Core.Networking;
using Fika.Core.Modding.Events;
using Comfort.Common;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace DesmatchMode4
{
    /// <summary>
    /// FIKA пакеты для DesmatchMode
    /// </summary>
    public static class DesmatchFikaPackets
    {
        // Базовый класс для всех DesmatchMode пакетов
        public abstract class DesmatchNetworkEvent : INetSerializable
        {
            public string PlayerProfileId;
            public string PlayerNickname;
            public long Timestamp;

            public virtual void Serialize(NetDataWriter writer)
            {
                writer.Put(PlayerProfileId ?? "");
                writer.Put(PlayerNickname ?? "");
                writer.Put(Timestamp);
            }

            public virtual void Deserialize(NetDataReader reader)
            {
                PlayerProfileId = reader.GetString();
                PlayerNickname = reader.GetString();
                Timestamp = reader.GetLong();
            }
        }

        /// <summary>
        /// Пакет данных игрока для синхронизации с сервером
        /// </summary>
        public class DesmatchPlayerDataPacket : DesmatchNetworkEvent
        {
            public Vector3 Position;
            public Vector3 Rotation;
            public float Health;
            public float Energy;
            public float Hydration;
            public bool IsInvulnerable;
            public float InvulnTimeRemaining;
            public int RespawnDelay;
            public int InvulnSeconds;
            public bool IsDead;
            public string ManualRespawnKey;

            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.PutVector3(Position);
                writer.PutVector3(Rotation);
                writer.Put(Health);
                writer.Put(Energy);
                writer.Put(Hydration);
                writer.Put(IsInvulnerable);
                writer.Put(InvulnTimeRemaining);
                writer.Put(RespawnDelay);
                writer.Put(InvulnSeconds);
                writer.Put(IsDead);
                writer.Put(ManualRespawnKey ?? "");
            }

            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                Position = reader.GetVector3();
                Rotation = reader.GetVector3();
                Health = reader.GetFloat();
                Energy = reader.GetFloat();
                Hydration = reader.GetFloat();
                IsInvulnerable = reader.GetBool();
                InvulnTimeRemaining = reader.GetFloat();
                RespawnDelay = reader.GetInt();
                InvulnSeconds = reader.GetInt();
                IsDead = reader.GetBool();
                ManualRespawnKey = reader.GetString();
            }
        }

        /// <summary>
        /// Пакет запроса респавна
        /// </summary>
        public class DesmatchRespawnPacket : DesmatchNetworkEvent
        {
            public Vector3 RespawnPosition;
            public Vector3 RespawnRotation;
            public bool IsManualRespawn;
            public int RespawnDelay;
            public int InvulnSeconds;

            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.PutVector3(RespawnPosition);
                writer.PutVector3(RespawnRotation);
                writer.Put(IsManualRespawn);
                writer.Put(RespawnDelay);
                writer.Put(InvulnSeconds);
            }

            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                RespawnPosition = reader.GetVector3();
                RespawnRotation = reader.GetVector3();
                IsManualRespawn = reader.GetBool();
                RespawnDelay = reader.GetInt();
                InvulnSeconds = reader.GetInt();
            }
        }

        /// <summary>
        /// Пакет обновления настроек клиента
        /// </summary>
        public class DesmatchSettingsUpdatePacket : DesmatchNetworkEvent
        {
            public int RespawnDelay;
            public int InvulnSeconds;
            public string ManualRespawnKey;
            public bool SettingsValid;

            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.Put(RespawnDelay);
                writer.Put(InvulnSeconds);
                writer.Put(ManualRespawnKey ?? "");
                writer.Put(SettingsValid);
            }

            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                RespawnDelay = reader.GetInt();
                InvulnSeconds = reader.GetInt();
                ManualRespawnKey = reader.GetString();
                SettingsValid = reader.GetBool();
            }
        }

        /// <summary>
        /// Пакет ответа сервера
        /// </summary>
        public class DesmatchServerResponsePacket : DesmatchNetworkEvent
        {
            public bool Success;
            public string Message;
            public int RespawnDelay;
            public int InvulnSeconds;
            public bool IsInvulnerable;
            public float InvulnTimeRemaining;
            public Vector3 RespawnPosition;
            public Vector3 RespawnRotation;

            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.Put(Success);
                writer.Put(Message ?? "");
                writer.Put(RespawnDelay);
                writer.Put(InvulnSeconds);
                writer.Put(IsInvulnerable);
                writer.Put(InvulnTimeRemaining);
                writer.PutVector3(RespawnPosition);
                writer.PutVector3(RespawnRotation);
            }

            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                Success = reader.GetBool();
                Message = reader.GetString();
                RespawnDelay = reader.GetInt();
                InvulnSeconds = reader.GetInt();
                IsInvulnerable = reader.GetBool();
                InvulnTimeRemaining = reader.GetFloat();
                RespawnPosition = reader.GetVector3();
                RespawnRotation = reader.GetVector3();
            }
        }

        /// <summary>
        /// Пакет уведомления о смерти игрока
        /// </summary>
        public class DesmatchDeathNotificationPacket : DesmatchNetworkEvent
        {
            public Vector3 DeathPosition;
            public Vector3 DeathRotation;
            public string DeathCause;
            public bool IsManualRespawn;

            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.PutVector3(DeathPosition);
                writer.PutVector3(DeathRotation);
                writer.Put(DeathCause ?? "");
                writer.Put(IsManualRespawn);
            }

            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                DeathPosition = reader.GetVector3();
                DeathRotation = reader.GetVector3();
                DeathCause = reader.GetString();
                IsManualRespawn = reader.GetBool();
            }
        }

        /// <summary>
        /// Пакет синхронизации неуязвимости
        /// </summary>
        public class DesmatchInvulnerabilityPacket : DesmatchNetworkEvent
        {
            public bool IsInvulnerable;
            public float InvulnTimeRemaining;
            public int InvulnSeconds;

            public override void Serialize(NetDataWriter writer)
            {
                base.Serialize(writer);
                writer.Put(IsInvulnerable);
                writer.Put(InvulnTimeRemaining);
                writer.Put(InvulnSeconds);
            }

            public override void Deserialize(NetDataReader reader)
            {
                base.Deserialize(reader);
                IsInvulnerable = reader.GetBool();
                InvulnTimeRemaining = reader.GetFloat();
                InvulnSeconds = reader.GetInt();
            }
        }
    }
}

