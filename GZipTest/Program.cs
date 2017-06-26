using System;

namespace GZipTest
{
    class Program
    {
        static BufferedStreamMove job;

        static void Main(string[] args)
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            
            if (args.Length < 3)
            {
                Console.WriteLine("Using: GZipTest <command> <source> <destination>");
                Console.WriteLine("  Compressing: GZipTest compress <source-file> <destination-archive>");
                Console.WriteLine("  Decompressing: GZipTest decompress <source-archive> <destination-file>");
                Environment.ExitCode = 1;
                return;
            }

            switch (args[0])
            {
                case "compress":
                    Environment.ExitCode = Compress(args[1], args[2]);
                    break;
                case "decompress":
                    Environment.ExitCode = Decompress(args[1], args[2]);
                    break;
            }
        }
        
        private static int Compress(string source, string dest)
        {
            job = new GZipCompressor(source, dest).Start();
            return job.Wait() ? 1 : 0;
        }

        private static int Decompress(string source, string dest)
        {
            Console.WriteLine("Decompressing...");
            job = new GZipDecompressor(source, dest).Start();
            return job.Wait() ? 1 : 0;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (job != null)
                job.Stop();
            e.Cancel = true;
        }
    }
}
