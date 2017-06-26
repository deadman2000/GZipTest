using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using GZipTest;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        const string TEST_IN_FILE = "test_in.txt";
        const string TEST_GZ_FILE = "test.gz";
        const string TEST_OUT_FILE = "test_out.txt";
        const string TEST_TEXT = "Example of sample";

        [TestMethod]
        public void CompressingTest()
        {
            Clear();

            File.WriteAllText(TEST_IN_FILE, TEST_TEXT);
            Assert.IsTrue(new GZipCompressor(TEST_IN_FILE, TEST_GZ_FILE).Start().Wait());
            Assert.IsTrue(new GZipDecompressor(TEST_GZ_FILE, TEST_OUT_FILE).Start().Wait());
            var result = File.ReadAllText(TEST_OUT_FILE);
            Assert.AreEqual(TEST_TEXT, result);

            Clear();
        }

        void Clear()
        {
            File.Delete(TEST_IN_FILE);
            File.Delete(TEST_OUT_FILE);
            File.Delete(TEST_GZ_FILE);
        }
    }
}
