using SharpRUDP.Test;
using System.Threading;

namespace SharpRUDP.LiveTest
{
    class Program
    {
        static void Wait()
        {
            // Console.ReadLine();
            Thread.Sleep(1000);
        }

        static void Main(string[] args)
        {
            new Connectivity() { TestName = "Connect & Disconnect" }.Run(); Wait();
            new PacketTest(100, 8, 1) { TestName = "8 bytes" }.Run(); Wait();
            new PacketTest(100, 32, 1) { TestName = "32 bytes" }.Run(); Wait();
            new PacketTest(100, 64, 1) { TestName = "64 bytes" }.Run(); Wait();
            new PacketTest(100, 128, 1) { TestName = "128 bytes" }.Run(); Wait();
            new PacketTest(100, 256, 1) { TestName = "256 bytes" }.Run(); Wait();
            new PacketTest(100, 1) { TestName = "1 Kbytes" }.Run(); Wait();
            new PacketTest(100, 2) { TestName = "2 Kbytes" }.Run(); Wait();
            new PacketTest(100, 4) { TestName = "4 Kbytes" }.Run(); Wait();
            new PacketTest(100, 8) { TestName = "8 Kbytes" }.Run(); Wait();
            new PacketTest(100, 16) { TestName = "16 Kbytes" }.Run(); Wait();
            new PacketTest(100, 32) { TestName = "32 Kbytes" }.Run(); Wait();
            new PacketTest(100, 64) { TestName = "64 Kbytes" }.Run(); Wait();
        }
    }
}
