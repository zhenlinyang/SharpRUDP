namespace SharpRUDP.Serializers
{
    public abstract class RUDPSerializer
    {
        public abstract byte[] Serialize(byte[] header, RUDPPacket p);
        public abstract RUDPPacket Deserialize(byte[] header, byte[] data);
        public abstract string AsString(RUDPPacket p);
    }
}
