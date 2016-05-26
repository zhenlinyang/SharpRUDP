using System.IO;

namespace SharpRUDP
{
    public class RUDPInternalPackets
    {
        public class ACKPacket
        {
            public byte[] header;
            public int sequence;

            public byte[] Serialize()
            {
                byte[] rv;
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(header);
                    bw.Write(sequence);
                    rv = ms.ToArray();
                }
                return rv;
            }

            public static ACKPacket Deserialize(byte[] data)
            {
                ACKPacket rv = new ACKPacket();
                using (MemoryStream ms = new MemoryStream(data))
                using (BinaryReader br = new BinaryReader(ms))
                {
                    rv.header = br.ReadBytes(4);
                    rv.sequence = br.ReadInt32();
                }
                return rv;
            }
        }

        public class KeepAlivePacket
        {
            public byte[] header;

            public byte[] Serialize()
            {
                byte[] rv;
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(header);
                    rv = ms.ToArray();
                }
                return rv;
            }

            public static KeepAlivePacket Deserialize(byte[] data)
            {
                KeepAlivePacket rv = new KeepAlivePacket();
                using (MemoryStream ms = new MemoryStream(data))
                using (BinaryReader br = new BinaryReader(ms))
                {
                    rv.header = br.ReadBytes(4);
                }
                return rv;
            }
        }

    }
}
