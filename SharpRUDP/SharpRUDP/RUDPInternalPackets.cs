using System.IO;

namespace SharpRUDP
{
    public class RUDPInternalPackets
    {
        public class AckPacket
        {
            public byte[] header;
            public int sequence;

            public byte[] Serialize()
            {
                MemoryStream ms = new MemoryStream();
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(header);
                    bw.Write(sequence);
                }
                return ms.ToArray();
            }

            public static AckPacket Deserialize(byte[] data)
            {
                AckPacket rv = new AckPacket();
                MemoryStream ms = new MemoryStream(data);
                using (BinaryReader br = new BinaryReader(ms))
                {
                    rv.header = br.ReadBytes(2);
                    rv.sequence = br.ReadInt32();
                }
                return rv;
            }
        }

        public class PingPacket
        {
            public byte[] header;

            public byte[] Serialize()
            {
                MemoryStream ms = new MemoryStream();
                using (BinaryWriter bw = new BinaryWriter(ms))
                    bw.Write(header);
                return ms.ToArray();
            }

            public static PingPacket Deserialize(byte[] data)
            {
                PingPacket rv = new PingPacket();
                MemoryStream ms = new MemoryStream(data);
                using (BinaryReader br = new BinaryReader(ms))
                    rv.header = br.ReadBytes(2);
                return rv;
            }
        }
    }
}
