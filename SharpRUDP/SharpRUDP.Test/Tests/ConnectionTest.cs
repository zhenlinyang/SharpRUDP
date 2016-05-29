using System.Threading;
using NUnit.Framework;

namespace SharpRUDP.Test
{
    public class ConnectionTest : NUnitTestClass
    {
        public override void Run()
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
    }
}
