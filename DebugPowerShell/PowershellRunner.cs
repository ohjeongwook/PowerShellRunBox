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
        public Dictionary<string, string> VariableReplaceMap { get; set; } = new Dictionary<string, string>();
        public PowershellRunner(string fileName = null)
        {
            if (fileName != null)
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
                    Logger.Out("HandlerBreakpointUpdatedEvent> breakpoint created");
                    break;

                case BreakpointUpdateType.Removed:
                    BreakPoints.Remove(args.Breakpoint.Id);
                    Logger.Out("HandlerBreakpointUpdatedEvent> breakpoint removed");
                    break;

                case BreakpointUpdateType.Enabled:
                    Logger.Out("HandlerBreakpointUpdatedEvent> breakpoint enabled");
                    break;

                case BreakpointUpdateType.Disabled:
                    Logger.Out("HandlerBreakpointUpdatedEvent> breakpoint disabled");
                    break;
            }

            Logger.Out(args.Breakpoint.ToString());
            Logger.Out();
            Console.ForegroundColor = saveFGColor;
        }

        private Dictionary<string, object> VariableMap = new Dictionary<string, object>();

        private void UpdateVariableMap(Debugger debugger)
        {
            var processVariables = new PSDataCollection<PSObject>();
            processVariables.DataAdded += (dSender, dArgs) =>
            {
                foreach (PSObject item in processVariables.ReadAll())
                {
                    if (item.ImmediateBaseObject is PSVariable)
                    {
                        PSVariable psv = (PSVariable)item.ImmediateBaseObject;

                        if (
                            !VariableMap.ContainsKey(psv.Name) ||
                            (
                                VariableMap[psv.Name] != null &&
                                VariableMap[psv.Name].ToString() != psv.Value.ToString()
                            )
                        )
                        {
                            Logger.Out(@"* " + psv.Name + ": " + psv.Value);
                        }

                        if (VariableReplaceMap != null && VariableReplaceMap.ContainsKey(psv.Name))
                        {
                            Logger.Out(@"Updated variable " + psv.Name + ": " + psv.Value + " --> " + VariableReplaceMap[psv.Name]);
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
            DebuggerCommandResults results = debugger.ProcessCommand(psCommand, processVariables);
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
            psCommand.AddScript(command);
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
            ConsoleColor saveFGColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;

            if (!ShowHelpMessage)
            {
                Logger.Out("Entering debug mode. Type 'h' to get help.");
                Logger.Out();
                ShowHelpMessage = true;
            }

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

            UpdateVariableMap(debugger);
            PrintDebuggerStopMessage(args);
            
            foreach(Breakpoint breakpoint in args.Breakpoints)
            {
                if (BreakPoints.ContainsKey(breakpoint.Id))
                {
                    Logger.Out("Found breakpoint info");
                }
            }

            while (resumeAction == null)
            {
                if (CommandCount > 0)
                {
                    resumeAction = RunCommand(debugger, Command)?.ResumeAction;
                    CommandCount--;
                }
                else
                {
                    Console.Write("PowerShell Debugger>> ");
                    string commandLine = Console.ReadLine();
                    Logger.Out();
                    string[] commandArgs = commandLine.Split(' ');

                    if (commandArgs.Length <= 0)
                    {
                        continue;
                    }

                    if (commandArgs[0] == "s")
                    {
                        Command = commandArgs[0];

                        if (commandArgs.Length > 1)
                        {
                            CommandCount = Convert.ToInt32(commandArgs[1]);
                        }
                    }
                    else
                    {
                        Command = commandLine;
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

        /// <summary>
        /// Method to run the sample script and handle debugger events.
        /// </summary>
        public void RunFile(string filePath)
        {
            using (Runspace runspace = RunspaceFactory.CreateRunspace())
            {
                runspace.Open();
                runspace.Debugger.SetDebugMode(DebugModes.LocalScript);

                using (PowerShell powerShell = PowerShell.Create())
                {
                    powerShell.Runspace = runspace;

                    runspace.Debugger.BreakpointUpdated += HandlerBreakpointUpdatedEvent;
                    runspace.Debugger.DebuggerStop += HandleDebuggerStopEvent;

                    Logger.Out("Starting script file: " + filePath);
                    powerShell.AddStatement().AddCommand("Set-PSBreakpoint").AddParameter("Script", filePath).AddParameter("Line", 1);
                    foreach (var kv in VariableReplaceMap)
                    {
                        Logger.Out("Setting variable breakpoint on: " + kv.Key);
                        powerShell.AddStatement().AddCommand("Set-PSBreakpoint").AddParameter("Variable", kv.Key);
                    }
                    powerShell.AddStatement().AddCommand("Set-ExecutionPolicy").AddParameter("ExecutionPolicy", "Bypass").AddParameter("Scope", "CurrentUser");
                    powerShell.AddScript(filePath);

                    var scriptOutput = new PSDataCollection<PSObject>();
                    scriptOutput.DataAdded += (sender, args) =>
                    {
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
        }

        public void RunCode(string[] args)
        {
            string filePath = string.Empty;

            if (!System.IO.File.Exists(args[0]))
            {
                filePath = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".ps1";
                System.IO.File.WriteAllText(filePath, string.Join(" ", args));
            }
            else
            {
                filePath = System.IO.Path.Combine(Environment.CurrentDirectory, args[0]);
            }

            RunFile(filePath);
        }
    }
}
