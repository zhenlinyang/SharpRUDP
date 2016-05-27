using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpRUDP.Test
{
    public class TestStructure
    {
        public Action Test;
    }

    [TestFixture]
    public class ControllingTestOrder
    {
        public static NUnitTestClass[] testOrder = new NUnitTestClass[] {
            new Connectivity() { TestName = "Connect & Disconnect" },
            new PacketTest(100, 8, 1) { TestName = "8 bytes" },
            new PacketTest(100, 32, 1) { TestName = "32 bytes" },
            new PacketTest(100, 64, 1) { TestName = "64 bytes" },
            new PacketTest(100, 128, 1) { TestName = "128 bytes" },
            new PacketTest(100, 256, 1) { TestName = "256 bytes" },
            new PacketTest(100, 1) { TestName = "1 Kbytes" },
            new PacketTest(100, 2) { TestName = "2 Kbytes" },
            new PacketTest(100, 4) { TestName = "4 Kbytes" },
            new PacketTest(100, 8) { TestName = "8 Kbytes" },
            new PacketTest(100, 16) { TestName = "16 Kbytes" },
            new PacketTest(100, 32) { TestName = "32 Kbytes" },
            new PacketTest(100, 64) { TestName = "64 Kbytes" },
        };

        [TestCaseSource(sourceName: "TestSource")]
        public void MyTest(TestStructure test)
        {
            test.Test();
        }

        // http://codereview.stackexchange.com/questions/90537/converting-to-base-26-in-one-based-mode
        private static string ToBase26(int number)
        {
            var list = new LinkedList<int>();
            list.AddFirst((number - 1) % 26);
            while ((number = --number / 26 - 1) > 0) list.AddFirst(number % 26);
            return new string(list.Select(s => (char)(s + 65)).ToArray());
        }

        public static IEnumerable<TestCaseData> TestSource
        {
            get
            {
                int order = 1;
                foreach (NUnitTestClass c in testOrder)
                {
                    TestCaseData tc = new TestCaseData(new TestStructure { Test = c.Run }).SetName(ToBase26(order).ToUpperInvariant() + ": " + ((NUnitTestClass)c).TestName);
                    order++;
                    yield return tc;
                }
            }
        }
    }
}
