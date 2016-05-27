using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SharpRUDP.Test
{
    public class ServerDisconnectionTest : NUnitTestClass
    {
        int _packetMax = 10;
        int _packetSize = 10;
        int _multiplier = 1;

        public override void Run()
        {
            bool finished = false;

            RUDPConnection s = new RUDPConnection();
            RUDPConnection c = new RUDPConnection();
            s.Listen("127.0.0.1", 80);
            c.Connect("127.0.0.1", 80);
            while (c.State != ConnectionState.OPEN)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.OPEN, c.State);

            byte[] buf = new byte[_packetSize * _multiplier];
            Random r = new Random(DateTime.Now.Second);
            r.NextBytes(buf);

            int counter = 0;
            s.OnPacketReceived += (RUDPPacket p) =>
            {
                Assert.IsTrue(p.Data.SequenceEqual(buf));
                counter++;
                if (counter >= 5)
                    if (s.State == ConnectionState.LISTEN)
                        s.Disconnect();
                if(counter >= _packetMax)
                    finished = true;
            };

            for (int i = 0; i < _packetMax; i++)
                c.Send(c.RemoteEndPoint, RUDPPacketType.DAT, RUDPPacketFlags.NUL, buf);

            // TODO: Detect server disconnection through keepalive packet
            // while (!finished) Thread.Sleep(10);

            s.Disconnect();
            c.Disconnect();
            while (c.State != ConnectionState.CLOSED && s.State != ConnectionState.CLOSED)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.CLOSED, s.State);
            Assert.AreEqual(ConnectionState.CLOSED, c.State);
        }
    }
}
