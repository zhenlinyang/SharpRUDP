using System;

namespace SharpRUDP.LiveTest
{
    class Program
    {
        static void Main(string[] args)
        {
            //new Test.Connectivity().ConnectAndDisconnect(); Console.ReadLine();
            new Test.Connectivity().AbruptDisconnection(); Console.ReadLine();
            //new Test.SmallPacketTest().SmallPacket(); Console.ReadLine();
            //new Test.MediumPacketTest().MediumPacket(); Console.ReadLine();
            //new Test.MultiPacketSmallTest().MultiPacketSmall(); Console.ReadLine();
            //new Test.MultiPacketMediumTest().MultiPacketMedium(); Console.ReadLine();
            //new Test.MultiPacketLargeTest().MultiPacketLarge(); Console.ReadLine();
            //new Test.MultiPacketExtraLargeTest().MultiPacketExtraLarge(); Console.ReadLine();
        }
    }
}
