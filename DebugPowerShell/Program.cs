using System;
using System.IO;
using System.Linq;

namespace DebugPowerShell
{
    public class DebuggerSample
    {
        private static void Main(string[] args)
        {
            Console.Title = "PowerShell Debugger Sample Application";
            Logger.Out("    Windows PowerShell Debugger Application Sample");
            Logger.Out("    ==============================================");
            Logger.Out();
            Logger.Out("This is a simple example of using the PowerShell Debugger API to set");
            Logger.Out("breakpoints in a script file, run the script and handle debugger events.");
            Logger.Out();

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
