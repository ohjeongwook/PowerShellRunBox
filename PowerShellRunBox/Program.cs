using System;
using System.IO;
using System.Linq;

namespace PowerShellRunBox
{
    public class DebuggerSample
    {
        private static void Main(string[] args)
        {
            if (args.Count() < 1)
            {
                return;
            }

            ConsoleIO consoleIO = new ConsoleIO();
            // Create DebuggerSample class instance and begin debugging sample script.
            var powershellRunner = new PowershellRunner("Config.json", consoleIO);
            powershellRunner.RunCode(args);
        }

    }
}
