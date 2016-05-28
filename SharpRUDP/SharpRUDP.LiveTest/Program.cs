using SharpRUDP.Test;
using System;
using System.Diagnostics;
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

        static void RunAllTests()
        {
            foreach (NUnitTestClass test in ControllingTestOrder.CLITestSource)
            {
                Stopwatch sw = Stopwatch.StartNew();
                sw.Start();
                Console.WriteLine("=================================== TEST START: {0} - {1}", test.TestName, DateTime.Now);
                test.Run();
                sw.Stop();
                Console.WriteLine("=================================== TEST END: {0} - {1}", test.TestName, sw.Elapsed);
            }
        }

        static void Main(string[] args)
        {
            //RunAllTests();
            new ServerDisconnectionTest() { TestName = "Disconnection test" }.Run(); Wait();
            Console.WriteLine("Finished");
            Console.ReadLine();
        }
    }
}
