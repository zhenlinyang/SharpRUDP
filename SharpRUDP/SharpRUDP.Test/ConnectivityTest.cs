using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using System.Threading;

namespace SharpRUDP.Test
{
    [TestClass]
    public class Connectivity
    {
        [TestMethod, Timeout(5000)]
        public void ConnectAndDisconnect()
        {
            RUDPConnection s = new RUDPConnection();
            RUDPConnection c = new RUDPConnection();
            s.Listen("127.0.0.1", 80);
            c.Connect("127.0.0.1", 80);
            while (c.State != ConnectionState.OPEN)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.OPEN, c.State);
            s.Disconnect();
            c.Disconnect();
            while (c.State != ConnectionState.CLOSED && s.State != ConnectionState.CLOSED)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.CLOSED, s.State);
            Assert.AreEqual(ConnectionState.CLOSED, c.State);
        }

        [TestMethod, Timeout(5000)]
        public void AbruptDisconnection()
        {
            RUDPConnection s = new RUDPConnection();
            RUDPConnection c = new RUDPConnection();
            s.Listen("127.0.0.1", 80);
            c.Connect("127.0.0.1", 80);
            while (c.State != ConnectionState.OPEN)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.OPEN, c.State);

            int counter = 0;
            int maxPackets = 100;
            bool finished = false;
            s.OnPacketReceived += (RUDPPacket p) =>
            {
                Assert.AreEqual(counter, int.Parse(Encoding.ASCII.GetString(p.Data)));
                counter++;
                if (counter >= maxPackets)
                    finished = true;
            };

            for (int i = 0; i < maxPackets; i++)
            {
                c.Send(i.ToString());
                if (i > maxPackets / 2)
                    if (s.State == ConnectionState.LISTEN)
                        s.Disconnect();
            }

            Thread.Sleep(5000);
            c.SendKeepAlive();

            while (true)
            {
                Thread.Sleep(1000);
            }

            s.Disconnect();
            c.Disconnect();
            while (c.State != ConnectionState.CLOSED && s.State != ConnectionState.CLOSED)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.CLOSED, s.State);
            Assert.AreEqual(ConnectionState.CLOSED, c.State);
        }

    }
}
