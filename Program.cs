using System;

namespace HttpProxy
{
    class Program
    {
        static void Main(string[] args)
        {
            int bufferSize = 1024 * 4;
            StreamMode mode = StreamMode.FrameworkDefault;
            if (args.Length < 1)
            {
                Proxy.WriteLine("\nError in parameters", ConsoleColor.Red);
                Console.WriteLine("usage: HttpProxy {StreamMode} {BufferSize} ");

                Console.WriteLine("\nStreamMode is a number from 0-4: FrameworkDefault, FrameworkBuffer, FrameworkCopy, MultiWrite");
                Console.WriteLine("BufferSize is in Bytes\n");

                Console.WriteLine("Example\n");
                Console.WriteLine("HttpProxy 0");
                Console.WriteLine("HttpProxy 1 4096");
                Console.WriteLine("HttpProxy 3 4096\n");

                return;
            }

            // Get Stream Mode
            int modeInteger;
            if (int.TryParse(args[0], out modeInteger))
            {
                mode = (StreamMode)modeInteger;
            }

            // Get BufferSize
            if (mode != StreamMode.FrameworkDefault)
            {
                if (args.Length > 1)
                {
                    bufferSize = int.Parse(args[1]);
                }
            }
            
            var listener = new Proxy(mode, bufferSize);

            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("Press Enter to exit");
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ReadLine();

            listener.Shutdown();
        }
    }
    
    internal enum StreamMode
    {
        FrameworkDefault,
        FrameworkBuffer,
        MultiWrite
    }
}
