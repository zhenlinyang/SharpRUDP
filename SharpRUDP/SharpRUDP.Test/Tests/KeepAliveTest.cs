using NUnit.Framework;
using System;
using System.Net;
using System.Threading;

namespace SharpRUDP.Test
{
    public class KeepAliveTest : NUnitTestClass
    {
        private bool _testServer = false;

        public KeepAliveTest(bool testServer)
        {
            _testServer = testServer;
        }

        public override void Run()
        {
            RUDPConnection s = new RUDPConnection();
            RUDPConnection c = new RUDPConnection();
            s.Listen("127.0.0.1", 80);
            c.Connect("127.0.0.1", 80);
            while (c.State != ConnectionState.OPEN)
                Thread.Sleep(10);
            Assert.AreEqual(ConnectionState.OPEN, c.State);

            c.OnSocketError += (IPEndPoint ep, Exception ex) => { Console.WriteLine("CLIENT ERROR {0}: {1}", ep, ex.Message); };
            s.OnSocketError += (IPEndPoint ep, Exception ex) => { Console.WriteLine("SERVER ERROR {0}: {1}", ep, ex.Message); };

            Console.WriteLine("10 seconds for keepalive START...");
            Thread.Sleep(5000);
            if (_testServer)
                c.Disconnect();
            else
                s.Disconnect();
            Thread.Sleep(5000);
            Console.WriteLine("10 seconds for keepalive END!");
            Thread.Sleep(2500);

            s.Disconnect();
            c.Disconnect();
            while (!(c.State == ConnectionState.CLOSED && s.State == ConnectionState.CLOSED))
                Thread.Sleep(10);

            Assert.AreEqual(ConnectionState.CLOSED, s.State);
            Assert.AreEqual(ConnectionState.CLOSED, c.State);

        }
    }
}
