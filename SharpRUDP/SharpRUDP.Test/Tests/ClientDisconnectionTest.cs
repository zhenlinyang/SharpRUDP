﻿using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace SharpRUDP.Test
{
    public class ClientDisconnectionTest : NUnitTestClass
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
                if (counter >= 4)
                    if (c.State == ConnectionState.OPEN)
                        c.Disconnect();
            };
            c.OnSocketError += (IPEndPoint ep, Exception ex) => { Console.WriteLine("CLIENT ERROR {0}: {1}", ep, ex.Message); };
            s.OnSocketError += (IPEndPoint ep, Exception ex) => { Console.WriteLine("SERVER ERROR {0}: {1}", ep, ex.Message); };
            s.OnClientDisconnect += (IPEndPoint ep) =>
            {
                Console.WriteLine("{0} disconnected.", ep);
                finished = true;
            };

            for (int i = 0; i < _packetMax; i++)
            {
                Thread.Sleep(100);
                c.Send(c.RemoteEndPoint, RUDPPacketType.DAT, RUDPPacketFlags.NUL, buf);
            }

            while (!finished)
                Thread.Sleep(10);

            s.Disconnect();
            c.Disconnect();
            while (!(c.State == ConnectionState.CLOSED && s.State == ConnectionState.CLOSED))
                Thread.Sleep(10);

            Assert.AreEqual(ConnectionState.CLOSED, s.State);
            Assert.AreEqual(ConnectionState.CLOSED, c.State);
        }
    }
}
