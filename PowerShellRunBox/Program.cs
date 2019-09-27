using System;
using System.IO;
using System.Linq;

namespace DebugPowerShell
{
    public class DebuggerSample
    {
        private static void Main(string[] args)
        {
            if (args.Count() < 1)
            {
                return;
            }

            // Create DebuggerSample class instance and begin debugging sample script.
            var powershellRunner = new PowershellRunner("Config.json");
            powershellRunner.RunCode(args);
        }

    }
}
