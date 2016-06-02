using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SharpRUDP.Serializers
{
    public class RUDPJSONSerializer : RUDPSerializer
    {
        private static string dataRegexStr = @"""Data"":\[[0-9,]*\]";
        private static Regex dataRegex = new Regex(dataRegexStr, RegexOptions.None);
        private static JavaScriptSerializer _js = new JavaScriptSerializer();

        public override RUDPPacket Deserialize(byte[] header, byte[] data)
        {
            return _js.Deserialize<RUDPPacket>(Encoding.ASCII.GetString(data.Skip(header.Length).ToArray()));
        }

        public override byte[] Serialize(byte[] header, RUDPPacket p)
        {
            return header.Concat(Encoding.ASCII.GetBytes(_js.Serialize(p))).ToArray();
        }

        public override string AsString(RUDPPacket p)
        {
            string js = _js.Serialize(p);
            js = js + string.Format(" RT:{0} ACK:{1} PROC:{2}", p.Retransmit ? 1 : 0, p.Acknowledged ? 1 : 0, p.Processed ? 1 : 0);
            if (p.Data != null && p.Data.Length > 30)
                return dataRegex.Replace(js, "\"Data\":" + (p.Data == null ? 0 : p.Data.Length) + "b");
            else
                return js;
        }
    }
}
