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
                MemoryStream ms = new MemoryStream();
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(header);
                    bw.Write(sequence);
                }
                return ms.ToArray();
            }

            public static ACKPacket Deserialize(byte[] data)
            {
                ACKPacket rv = new ACKPacket();
                MemoryStream ms = new MemoryStream(data);
                using (BinaryReader br = new BinaryReader(ms))
                {
                    rv.header = br.ReadBytes(4);
                    rv.sequence = br.ReadInt32();
                }
                return rv;
            }
        }
    }
}
