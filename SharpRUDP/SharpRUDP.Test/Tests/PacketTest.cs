using System;
using System.Threading;
using NUnit.Framework;
using System.Linq;
using System.Net;

namespace SharpRUDP.Test
{
    public class PacketTest : NUnitTestClass
    {        
        int _packetMax;
        int _packetSize;
        int _multiplier;
        bool _delay = false;
        AutoResetEvent wait = new AutoResetEvent(false);

        public PacketTest(int max, int size, int multiplier = 1024, bool delay = false)
        {
            _packetMax = max;
            _packetSize = size;
            _multiplier = multiplier;
            _delay = delay;
        }

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
                if (counter >= _packetMax)
                    finished = true;
            };
            c.OnSocketError += (IPEndPoint ep, Exception ex) => { Console.WriteLine("CLIENT ERROR {0}: {1}", ep, ex.Message); };
            s.OnSocketError += (IPEndPoint ep, Exception ex) => { Console.WriteLine("SERVER ERROR {0}: {1}", ep, ex.Message); };
            c.OnPacketConfirmed += (int packetId) =>
            {
                wait.Set();
            };

            if (_delay)
            {
                for (int i = 0; i < _packetMax; i++)
                {
                    Thread.Sleep(1 * r.Next(0, 10));
                    c.Send(c.RemoteEndPoint, RUDPPacketType.DAT, RUDPPacketFlags.NUL, buf);
                    wait.WaitOne();
                }

                while (!finished)
                    Thread.Sleep(10);
            }

            counter = 0;
            finished = false;
            for (int i = 0; i < _packetMax; i++)
            {
                c.Send(c.RemoteEndPoint, RUDPPacketType.DAT, RUDPPacketFlags.NUL, buf);
                wait.WaitOne();
            }

            while (!finished)
                Thread.Sleep(10);

            s.Disconnect();
            c.Disconnect();
            while (c.State != ConnectionState.CLOSED && s.State != ConnectionState.CLOSED)
                Thread.Sleep(10);

            Thread.Sleep(1000);

            Assert.AreEqual(ConnectionState.CLOSED, s.State);
            Assert.AreEqual(ConnectionState.CLOSED, c.State);

        }
    }
}
