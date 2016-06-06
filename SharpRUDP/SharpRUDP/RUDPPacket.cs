using SharpRUDP.Serializers;
using System;
using System.Net;
using System.Web.Script.Serialization;

namespace SharpRUDP
{
    public class RUDPPacket
    {
        [ScriptIgnore]
        public RUDPSerializer Serializer { get; set; }
        [ScriptIgnore]
        public IPEndPoint Src { get; set; }
        [ScriptIgnore]
        public IPEndPoint Dst { get; set; }
        [ScriptIgnore]
        public DateTime Sent { get; set; }
        [ScriptIgnore]
        public DateTime Received { get; set; }
        [ScriptIgnore]
        public bool Retransmit { get; set; }
        [ScriptIgnore]
        public bool Processed { get; set; }
        [ScriptIgnore]
        public Action<RUDPPacket> OnPacketReceivedByDestination { get; set; }

        public int Seq { get; set; }
        public int Id { get; set; }
        public int Qty { get; set; }
        public RUDPPacketType Type { get; set; }
        public RUDPPacketFlags Flags { get; set; }
        public byte[] Data { get; set; }
        public int[] intData { get; set; }

        public static RUDPPacket Deserialize(RUDPSerializer serializer, byte[] header, byte[] data)
        {
            return serializer.Deserialize(header, data);
        }

        public override string ToString()
        {
            return Serializer.AsString(this);
        }
    }
}
