using System;
using System.Threading;

namespace SharpRUDP.LiveTest
{
    class Program
    {
        static void Wait()
        {
            //Console.ReadLine();
            Thread.Sleep(5000);
        }

        static void Main(string[] args)
        {
            new Test.Connectivity().ConnectAndDisconnect(); Wait();
            new Test.SmallPacketTest().SmallPacket(); Wait();
            new Test.MediumPacketTest().MediumPacket(); Wait();
            new Test.MultiPacketSmallTest().MultiPacketSmall(); Wait();
            new Test.MultiPacketMediumTest().MultiPacketMedium(); Wait();
            new Test.MultiPacketLargeTest().MultiPacketLarge(); Wait();
            new Test.MultiPacketExtraLargeTest().MultiPacketExtraLarge(); Wait();
        }
    }
}
