using System;
using System.IO;
using SharpMonoInjector;

namespace HowToFishInjector
{
    internal static class Program
    {
        private const string ProcessName = "How to Fish";

        private static int Main(string[] args)
        {
            Console.WriteLine("How To Fish Injector — injects the mod menu into the running game");
            Console.WriteLine("Make sure the game is running first.\n");

            string coreDll = Path.Combine(AppContext.BaseDirectory, "VladMod.Core.dll");
            if (!File.Exists(coreDll))
            {
                Console.WriteLine("ERROR: VladMod.Core.dll not found next to the injector.");
                return 1;
            }

            // Memory-only: the injector writes NOTHING to the game folder. VladMod.Core.dll
            // is loaded from a byte array, and its Harmony/MonoMod/Cecil dependencies are
            // embedded resources resolved from memory. Restart the game = fully unloaded.
            byte[] assembly = File.ReadAllBytes(coreDll);
            try
            {
                using (var injector = new Injector(ProcessName))
                {
                    IntPtr handle = injector.Inject(assembly, "HowToFishModMenu", "ModCore", "Entry");
                    Console.WriteLine("Injected OK (assembly handle 0x" + handle.ToString("X") + ")");
                }
                Console.WriteLine("\nDone. Check How to Fish_Data/VladModLog.txt to confirm it loaded.");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("INJECTION FAILED: " + e.Message);
                Console.WriteLine("Tips: game running? Run as administrator if access is denied.");
                return 1;
            }
        }
    }
}