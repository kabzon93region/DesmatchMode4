using UnityEngine;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace DesmatchMode4
{
    internal static class DesmatchNetSerialization
    {
        public static void PutVector3(this NetDataWriter writer, Vector3 value)
        {
            writer.Put(value.x);
            writer.Put(value.y);
            writer.Put(value.z);
        }

        public static Vector3 GetVector3(this NetDataReader reader)
        {
            return new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }
    }
}
