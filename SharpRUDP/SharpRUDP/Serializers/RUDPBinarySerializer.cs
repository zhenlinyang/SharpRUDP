using System.IO;

namespace SharpRUDP.Serializers
{
    public class RUDPBinarySerializer : RUDPSerializer
    {
        public override RUDPPacket Deserialize(byte[] header, byte[] data)
        {
            RUDPPacket p = new RUDPPacket();
            MemoryStream ms = new MemoryStream(data);
            using (BinaryReader br = new BinaryReader(ms))
            {
                br.ReadBytes(header.Length);
                p.Seq = br.ReadInt32();
                p.Id = br.ReadInt32();
                p.Qty = br.ReadInt32();
                p.Type = (RUDPPacketType)br.ReadByte();
                p.Flags = (RUDPPacketFlags)br.ReadByte();
                int dataLen = br.ReadInt32();
                p.Data = br.ReadBytes(dataLen);
                p.intData = new int[br.ReadInt32()];
                for (int i = 0; i < p.intData.Length; i++)
                    p.intData[i] = br.ReadInt32();
            }
            return p;
        }

        public override byte[] Serialize(byte[] header, RUDPPacket p)
        {
            MemoryStream ms = new MemoryStream();
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(header);
                bw.Write(p.Seq);
                bw.Write(p.Id);
                bw.Write(p.Qty);
                bw.Write((byte)p.Type);
                bw.Write((byte)p.Flags);
                bw.Write(p.Data == null ? 0 : p.Data.Length);
                if (p.Data != null)
                    bw.Write(p.Data);
                bw.Write(p.intData == null ? 0 : p.intData.Length);
                if (p.intData != null)
                    foreach (int i in p.intData)
                        bw.Write(i);
            }
            return ms.ToArray();
        }

        public override string AsString(RUDPPacket p)
        {
            return string.Format("SEQ:{0}|ID:{1}|QTY:{2}|TYPE:{3}|FLAGS:{4}|DATA:{5}|INTDATA:{6}",
                p.Seq,
                p.Id,
                p.Qty,
                p.Type.ToString(),
                p.Flags.ToString(),
                p.Data == null ? "" : (p.Data.Length > 30 ? p.Data.Length.ToString() : string.Join(",", p.Data)),
                p.intData == null ? "" : string.Join(",", p.intData)
            );
        }
    }
}
