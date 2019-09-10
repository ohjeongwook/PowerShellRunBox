using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Language;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;

namespace DebugPowerShell
{
    class PowershellRunner
    {
        #region Public Methods
        public Dictionary<string, string> VariableReplaceMap { get; set; } = new Dictionary<string, string>();

        public PowershellRunner(string fileName=null)
        {
            if ( fileName!=null )
            {
                LoadConfig(fileName);
            }
        }

        public void RunCode(string[] args)
        {
            string fileName = string.Empty;

            if (!System.IO.File.Exists(args[0]))
            {
                fileName = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".ps1";
                System.IO.File.WriteAllText(fileName, string.Join(" ", args));
            }
            else
            {
                fileName = args[0];
            }

            RunFile(fileName);
        }

        /// <summary>
        /// Method to run the sample script and handle debugger events.
        /// </summary>
        public void RunFile(string fileName)
        {
            Logger.Out("Starting PowerShell Debugger Sample");
            Logger.Out();

            string filePath = System.IO.Path.Combine(Environment.CurrentDirectory, fileName);

            using (Runspace runspace = RunspaceFactory.CreateRunspace())
            {
                // Open runspace and set debug mode to debug PowerShell scripts and 
                // Workflow scripts.  PowerShell script debugging is enabled by default,
                // Workflow debugging is opt-in.
                runspace.Open();
                runspace.Debugger.SetDebugMode(DebugModes.LocalScript);

                using (PowerShell powerShell = PowerShell.Create())
                {
                    powerShell.Runspace = runspace;

                    runspace.Debugger.BreakpointUpdated += HandlerBreakpointUpdatedEvent;
                    runspace.Debugger.DebuggerStop += HandleDebuggerStopEvent;

                    // Set initial breakpoint on line 10 of script.  This breakpoint
                    // will be in the script workflow function.
                    powerShell.AddCommand("Set-PSBreakpoint").AddParameter("Script", filePath).AddParameter("Line", 1);
                    powerShell.AddCommand("Set-ExecutionPolicy").AddParameter("ExecutionPolicy", "Bypass").AddParameter("Scope", "CurrentUser");
                    powerShell.Invoke();

                    Logger.Out("Starting script file: " + filePath);
                    Logger.Out();

                    // Run script file.
                    powerShell.Commands.Clear();
                    powerShell.AddCommand("Set-ExecutionPolicy").AddParameter("ExecutionPolicy", "Bypass").AddParameter("Scope", "CurrentUser");
                    powerShell.AddScript(filePath).AddCommand("Out-String").AddParameter("Stream", true);
                    var scriptOutput = new PSDataCollection<PSObject>();
                    scriptOutput.DataAdded += (sender, args) =>
                    {
                        // Stream script output to console.
                        foreach (var item in scriptOutput.ReadAll())
                        {
                            Logger.Out("| " + item);
                        }
                    };

                    powerShell.Invoke<PSObject>(null, scriptOutput);

                    if (powerShell.Streams.Error != null)
                    {
                        foreach (ErrorRecord errorRecord in powerShell.Streams.Error.ReadAll())
                        {
                            if (errorRecord != null && errorRecord.ErrorDetails != null)
                            {
                                Logger.Out(errorRecord.ErrorDetails.Message);
                            }
                        }
                    }
                }
            }

            Logger.Out("Press any key to exit.");
            Console.ReadKey(true);
        }
        #endregion

        #region Private Methods
        public void LoadConfig(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return;
            }

            using (StreamReader r = new StreamReader(fileName))
            {
                var jsonString = r.ReadToEnd();
                dynamic dynObj = JsonConvert.DeserializeObject(jsonString);

                foreach (var item in dynObj)
                {
                    VariableReplaceMap[item.Path.ToString()] = item.Value.ToString();
                }
            }
        }

        private Dictionary<string, object> VariableMap = new Dictionary<string, object>();

        private void UpdateVariableMap(Debugger debugger)
        {
            var output = new PSDataCollection<PSObject>();
            output.DataAdded += (dSender, dArgs) =>
            {
                foreach (PSObject item in output.ReadAll())
                {
                    if (item.ImmediateBaseObject is PSVariable)
                    {
                        PSVariable psv = (PSVariable)item.ImmediateBaseObject;

                        if (
                            !VariableMap.ContainsKey(psv.Name) ||
                            (
                                VariableMap[psv.Name]!=null &&
                                VariableMap[psv.Name].ToString() != psv.Value.ToString()
                            )
                        )
                        {
                            Logger.Out(@"* " + psv.Name + ": " + psv.Value);
                        }

                        if(VariableReplaceMap != null && VariableReplaceMap.ContainsKey(psv.Name))
                        {
                            psv.Value = VariableReplaceMap[psv.Name];
                        }

                        VariableMap[psv.Name] = psv.Value;
                    }
                    else if (item.ImmediateBaseObject is ErrorRecord)
                    {
                        ErrorRecord errorRecord = (ErrorRecord)item.ImmediateBaseObject;

                        Logger.Out(@"* item: " + item.ImmediateBaseObject.GetType());
                        
                        if (errorRecord != null && errorRecord.ErrorDetails != null)
                        {
                            Logger.Out(errorRecord.ErrorDetails.Message);
                        }
                    }
                    else
                    {
                        Logger.Out(@"* item: " + item.ImmediateBaseObject.GetType());
                    }
                }
            };

            PSCommand psCommand = new PSCommand();
            psCommand.AddScript("Get-Variable");
            DebuggerCommandResults results = debugger.ProcessCommand(psCommand, output);
        }

        private DebuggerCommandResults RunCommand(Debugger debugger, string command)
        {
            var output = new PSDataCollection<PSObject>();
            output.DataAdded += (dSender, dArgs) =>
            {
                foreach (var item in output.ReadAll())
                {
                    Logger.Out(item.ToString());
                }
            };

            PSCommand psCommand = new PSCommand();
            psCommand.AddScript(command).AddCommand("Out-String").AddParameter("Stream", true);
            DebuggerCommandResults results = debugger.ProcessCommand(psCommand, output);

            return results;
        }

        /// <summary>
        /// Simple sample script that writes output, sets variables, and
        /// calls a workflow function.
        /// </summary>
        private bool ShowHelpMessage;

        /// <summary>
        /// Collection of all breakpoints on the runspace debugger.
        /// </summary>
        private Dictionary<int, Breakpoint> BreakPoints = new Dictionary<int, Breakpoint>();

        /// <summary>
        /// Helper method to write debugger stop messages.
        /// </summary>
        /// <param name="args">DebuggerStopEventArgs for current debugger stop</param>
        private void PrintDebuggerStopMessage(DebuggerStopEventArgs args)
        {
            // Write debugger stop information in yellow.
            ConsoleColor saveFGColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;

            // Show help message only once.
            if (!ShowHelpMessage)
            {
                Logger.Out("Entering debug mode. Type 'h' to get help.");
                Logger.Out();
                ShowHelpMessage = true;
            }

            // Break point summary message.
            string breakPointMsg = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Breakpoints: Enabled {0}, Disabled {1}",
                (BreakPoints.Values.Where<Breakpoint>((bp) => { return bp.Enabled; })).Count(),
                (BreakPoints.Values.Where<Breakpoint>((bp) => { return !bp.Enabled; })).Count());
            Logger.Out(breakPointMsg);
            Logger.Out();

            if (args.Breakpoints.Count > 0)
            {
                Logger.Out("Debugger hit breakpoint on:");
                foreach (var breakPoint in args.Breakpoints)
                {
                    Logger.Out(breakPoint.ToString());
                }
                Logger.Out();
            }

            if (args.InvocationInfo != null)
            {
                string currentLine = args.InvocationInfo.Line.Substring(
                    args.InvocationInfo.OffsetInLine - 1,
                    args.InvocationInfo.Line.Length - args.InvocationInfo.OffsetInLine + 1);

                Token[] tokens;
                ParseError[] parseErrors;
                ScriptBlockAst scriptBlock = System.Management.Automation.Language.Parser.ParseInput(currentLine, out tokens, out parseErrors);
                Logger.Out("> " + scriptBlock.EndBlock.Statements[0].ToString());
            }

            Console.ForegroundColor = saveFGColor;
        }

        private string Command = string.Empty;
        private int CommandCount = 0;
        private void HandleDebuggerStopEvent(object sender, DebuggerStopEventArgs args)
        {
            Debugger debugger = sender as Debugger;
            DebuggerResumeAction? resumeAction = null;

            PrintDebuggerStopMessage(args);
            UpdateVariableMap(debugger);

            while (resumeAction == null)
            {
                Console.Write("PowerShell Deubugger>> ");

                if (CommandCount > 0)
                {
                    Console.WriteLine("RunCommand: " + Command);
                    resumeAction = RunCommand(debugger, Command)?.ResumeAction;
                    CommandCount--;
                }
                else
                {
                    string commandLine = Console.ReadLine();
                    Logger.Out();
                    string[] commandArgs = commandLine.Split(' ');

                    if (commandArgs.Length<=0)
                    {
                        continue;
                    }

                    Command = commandArgs[0];

                    if (commandArgs.Length > 1)
                    {
                        CommandCount = Convert.ToInt32(commandArgs[1]);
                    }

                    resumeAction = RunCommand(debugger, Command)?.ResumeAction;
                }
            }

            // DebuggerStopEventArgs.ResumeAction:
            //  - Continue      Continue execution until next breakpoint is hit.
            //  - StepInto      Step into function.
            //  - StepOut       Step out of function.
            //  - StepOver      Step over function.
            //  - Stop          Stop debugging.
            args.ResumeAction = resumeAction.Value;
        }

        // Method to handle the Debugger BreakpointUpdated event.
        // This method will display the current breakpoint change and maintain a 
        // collection of all current breakpoints.
        private void HandlerBreakpointUpdatedEvent(object sender, BreakpointUpdatedEventArgs args)
        {
            // Write message to console.
            ConsoleColor saveFGColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Logger.Out();

            switch (args.UpdateType)
            {
                case BreakpointUpdateType.Set:
                    if (!BreakPoints.ContainsKey(args.Breakpoint.Id))
                    {
                        BreakPoints.Add(args.Breakpoint.Id, args.Breakpoint);
                    }
                    Logger.Out("Breakpoint created:");
                    break;

                case BreakpointUpdateType.Removed:
                    BreakPoints.Remove(args.Breakpoint.Id);
                    Logger.Out("Breakpoint removed:");
                    break;

                case BreakpointUpdateType.Enabled:
                    Logger.Out("Breakpoint enabled:");
                    break;

                case BreakpointUpdateType.Disabled:
                    Logger.Out("Breakpoint disabled:");
                    break;
            }

            Logger.Out(args.Breakpoint.ToString());
            Logger.Out();
            Console.ForegroundColor = saveFGColor;
        }
        #endregion
    }
}
