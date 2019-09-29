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

namespace PowerShellRunBox
{
    class PowershellRunner
    {
        private UserIO UserIOImpl;
        public Dictionary<string, string> VariableReplaceMap { get; set; } = new Dictionary<string, string>();
        public PowershellRunner(string fileName = null, UserIO userIO = null)
        {
            UserIOImpl = userIO;
            if (fileName != null)
            {
                if (File.Exists(fileName))
                {
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
        }

        // Method to handle the Debugger BreakpointUpdated event.
        // This method will display the current breakpoint change and maintain a 
        // collection of all current breakpoints.
        private void HandlerBreakpointUpdatedEvent(object sender, BreakpointUpdatedEventArgs args)
        {
            switch (args.UpdateType)
            {
                case BreakpointUpdateType.Set:
                    if (!BreakPoints.ContainsKey(args.Breakpoint.Id))
                    {
                        BreakPoints.Add(args.Breakpoint.Id, args.Breakpoint);
                    }
                    UserIOImpl.PrintMessage("HandlerBreakpointUpdatedEvent> breakpoint created");
                    break;

                case BreakpointUpdateType.Removed:
                    BreakPoints.Remove(args.Breakpoint.Id);
                    UserIOImpl.PrintMessage("HandlerBreakpointUpdatedEvent> breakpoint removed");
                    break;

                case BreakpointUpdateType.Enabled:
                    UserIOImpl.PrintMessage("HandlerBreakpointUpdatedEvent> breakpoint enabled");
                    break;

                case BreakpointUpdateType.Disabled:
                    UserIOImpl.PrintMessage("HandlerBreakpointUpdatedEvent> breakpoint disabled");
                    break;
            }

            UserIOImpl.PrintMessage(args.Breakpoint.ToString());
        }

        private Dictionary<string, object> VariableMap = new Dictionary<string, object>();

        public static string DumpHex(byte[] bytes, int bytesPerLine = 16, string prefix="")
        {
            if (bytes == null) return "<null>";
            int bytesLength = bytes.Length;

            char[] HexChars = "0123456789ABCDEF".ToCharArray();

            int firstHexColumn =
                  8                   // 8 characters for the address
                + 3;                  // 3 spaces

            int firstCharColumn = firstHexColumn
                + bytesPerLine * 3       // - 2 digit for the hexadecimal value and 1 space
                + (bytesPerLine - 1) / 8 // - 1 extra space every 8 characters from the 9th
                + 2;                  // 2 spaces 

            int lineLength = firstCharColumn
                + bytesPerLine           // - characters to show the ascii value
                + Environment.NewLine.Length; // Carriage return and line feed (should normally be 2)

            char[] line = (new String(' ', lineLength - 2) + Environment.NewLine).ToCharArray();
            int expectedLines = (bytesLength + bytesPerLine - 1) / bytesPerLine;
            StringBuilder result = new StringBuilder(expectedLines * lineLength);

            for (int i = 0; i < bytesLength; i += bytesPerLine)
            {
                line[0] = HexChars[(i >> 28) & 0xF];
                line[1] = HexChars[(i >> 24) & 0xF];
                line[2] = HexChars[(i >> 20) & 0xF];
                line[3] = HexChars[(i >> 16) & 0xF];
                line[4] = HexChars[(i >> 12) & 0xF];
                line[5] = HexChars[(i >> 8) & 0xF];
                line[6] = HexChars[(i >> 4) & 0xF];
                line[7] = HexChars[(i >> 0) & 0xF];

                int hexColumn = firstHexColumn;
                int charColumn = firstCharColumn;

                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (j > 0 && (j & 7) == 0) hexColumn++;
                    if (i + j >= bytesLength)
                    {
                        line[hexColumn] = ' ';
                        line[hexColumn + 1] = ' ';
                        line[charColumn] = ' ';
                    }
                    else
                    {
                        byte b = bytes[i + j];
                        line[hexColumn] = HexChars[(b >> 4) & 0xF];
                        line[hexColumn + 1] = HexChars[b & 0xF];
                        line[charColumn] = asciiSymbol(b);
                    }
                    hexColumn += 3;
                    charColumn++;
                }
                result.Append(prefix + new string(line));
            }
            return result.ToString();
        }

        static char asciiSymbol(byte val)
        {
            if (val < 32) return '.';  // Non-printable ASCII
            if (val < 127) return (char)val;   // Normal ASCII
            // Handle the hole in Latin-1
            if (val == 127) return '.';
            if (val < 0x90) return "€.‚ƒ„…†‡ˆ‰Š‹Œ.Ž."[val & 0xF];
            if (val < 0xA0) return ".‘’“”•–—˜™š›œ.žŸ"[val & 0xF];
            if (val == 0xAD) return '.';   // Soft hyphen: this symbol is zero-width even in monospace fonts
            return (char)val;   // Normal Latin-1
        }

        private void DumpVariable(string name, object value, int level=0)
        {
            string prefix = "";
            for (int i=0; i< level; i++)
            {
                prefix += "   ";
            }

            if (value is Array)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    UserIOImpl.PrintMessage(prefix + @"* " + name, "Red");
                }

                Array arr = (Array)(value);

                if (value is System.Char[])
                {
                    byte[] bytes = Encoding.ASCII.GetBytes((System.Char[])value);
                    UserIOImpl.PrintMessage(DumpHex(bytes, 16, prefix + "   "), "Red");
                }
                else
                {
                    foreach (object element in (Array)(value))
                    {
                        DumpVariable("", element, level + 1);
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(name))
                {
                    UserIOImpl.PrintMessage(prefix + value, "Red");
                }
                else
                {
                    UserIOImpl.PrintMessage(prefix + @"* " + name + ": " + value, "Red");
                }
            }
        }

        private bool CheckVariables(Debugger debugger)
        {
            bool updatedVariable = false;
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
                            DumpVariable(psv.Name, psv.Value);
                        }

                        if (VariableReplaceMap != null && VariableReplaceMap.ContainsKey(psv.Name))
                        {
                            if (psv.Value.ToString() != VariableReplaceMap[psv.Name])
                            {
                                UserIOImpl.PrintMessage(@"Updated variable " + psv.Name + ": " + psv.Value + " --> " + VariableReplaceMap[psv.Name], "Red");
                                psv.Value = VariableReplaceMap[psv.Name];
                                updatedVariable = true;
                            }
                        }

                        VariableMap[psv.Name] = psv.Value;
                    }
                    else if (item.ImmediateBaseObject is ErrorRecord)
                    {
                        ErrorRecord errorRecord = (ErrorRecord)item.ImmediateBaseObject;

                        UserIOImpl.PrintMessage(@"* item: " + item.ImmediateBaseObject.GetType() + "\n");

                        if (errorRecord != null && errorRecord.ErrorDetails != null)
                        {
                            UserIOImpl.PrintMessage(errorRecord.ErrorDetails.Message + "\n");
                        }
                    }
                    else
                    {
                        UserIOImpl.PrintMessage(@"* item: " + item.ImmediateBaseObject.GetType());
                    }
                }
            };

            PSCommand psCommand = new PSCommand();
            psCommand.AddScript("Get-Variable");
            DebuggerCommandResults results = debugger.ProcessCommand(psCommand, processVariables);
            return updatedVariable;
        }

        private DebuggerCommandResults RunDebuggerCommand(Debugger debugger, string command)
        {
            var output = new PSDataCollection<PSObject>();
            output.DataAdded += (dSender, dArgs) =>
            {
                foreach (var item in output.ReadAll())
                {
                    UserIOImpl.PrintMessage(item.ToString()+"\n");
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

        private bool FindAst(Ast ast)
        {
            return true;
        }

        private void PrintAST(Ast ast, int offsetInLine)
        {
            foreach (Ast oneAst in ast.FindAll(FindAst, true))
            {
                if (oneAst.Extent.StartScriptPosition.ColumnNumber != offsetInLine)
                {
                    continue;
                }

                string line = oneAst.Extent.StartScriptPosition.Line;
                int startOffset = oneAst.Extent.StartScriptPosition.Offset;
                int endOffset = oneAst.Extent.EndScriptPosition.Offset;

                if (!string.IsNullOrEmpty(oneAst.Extent.StartScriptPosition.File))
                {
                    UserIOImpl.PrintCode(String.Format("File {0}: {1}",
                        oneAst.Extent.StartScriptPosition.File,
                        oneAst.Extent.StartScriptPosition.LineNumber));
                }

                UserIOImpl.PrintCode(line.Substring(0, startOffset));
                UserIOImpl.PrintCode(line.Substring(startOffset, endOffset-startOffset), "Yellow", "Red");
                UserIOImpl.PrintCode(line.Substring(endOffset));
                break;
            }
        }

        private Dictionary<string, string[]> fileLines = new Dictionary<string, string[]>();
        private void ReadFile(string filename)
        {
            if (!fileLines.ContainsKey(filename))
            {
                string[] lines = System.IO.File.ReadAllLines(filename);
                fileLines.Add(filename, lines);
            }
        }

        private string GetFileLine(string filename, int lineNumber)
        {
            if (!fileLines.ContainsKey(filename))
            {
                return string.Empty;
            }

            int index = lineNumber - 1;
            if (index >= fileLines[filename].Length || index < 0)
            {
                return string.Empty;
            }

            return fileLines[filename][index];
        }

        /// <summary>
        /// Helper method to write debugger stop messages.
        /// </summary>
        /// <param name="args">DebuggerStopEventArgs for current debugger stop</param>
        private void PrintCurrentCode(DebuggerStopEventArgs args)
        {
            if (args.InvocationInfo == null)
            {
                return;
            }

            Token[] tokens;
            ParseError[] parseErrors;

            ScriptBlockAst sciptBlockAst = Parser.ParseInput(
                args.InvocationInfo.Line,
                out tokens,
                out parseErrors);


            if (!string.IsNullOrEmpty(args.InvocationInfo.ScriptName))
            {
                ReadFile(args.InvocationInfo.ScriptName);
                UserIOImpl.PrintCode(String.Format("File {0}: {1}\n",
                    args.InvocationInfo.ScriptName, args.InvocationInfo.ScriptLineNumber));

                UserIOImpl.PrintCode(GetFileLine(args.InvocationInfo.ScriptName, args.InvocationInfo.ScriptLineNumber - 1) + "\n");
            }

            foreach (StatementAst statementAst in sciptBlockAst.EndBlock.Statements)
            {
                PrintAST(statementAst, args.InvocationInfo.OffsetInLine);
            }

            if (!string.IsNullOrEmpty(args.InvocationInfo.ScriptName))
            {
                UserIOImpl.PrintCode(GetFileLine(args.InvocationInfo.ScriptName, args.InvocationInfo.ScriptLineNumber + 1) + "\n");
            }

            UserIOImpl.PrintCode("\n");
        }

        private string Command = string.Empty;
        private int CommandCount = 0;

        private void HandleDebuggerStopEvent(object sender, DebuggerStopEventArgs args)
        {
            Debugger debugger = sender as Debugger;
            DebuggerResumeAction? resumeAction = null;

            if (!ShowHelpMessage)
            {
                UserIOImpl.PrintMessage("Entering debug mode. Type 'h' to get help.\n");
                ShowHelpMessage = true;
            }

            CheckVariables(debugger);
            PrintCurrentCode(args);

            while (resumeAction == null)
            {
                if (CommandCount > 0)
                {
                    resumeAction = RunDebuggerCommand(debugger, Command)?.ResumeAction;
                    CommandCount--;
                }
                else
                {
                    string commandLine = UserIOImpl.GetInput("PowerShellRunBox>> ");
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

                    resumeAction = RunDebuggerCommand(debugger, Command)?.ResumeAction;
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

                    UserIOImpl.PrintMessage("Starting script file: " + filePath);
                    powerShell.AddStatement().AddCommand("Set-PSBreakpoint").AddParameter("Script", filePath).AddParameter("Line", 1);
                    foreach (var kv in VariableReplaceMap)
                    {
                        UserIOImpl.PrintMessage("Setting variable breakpoint on: " + kv.Key);
                        powerShell.AddStatement().AddCommand("Set-PSBreakpoint").AddParameter("Variable", kv.Key);
                    }
                    powerShell.AddStatement().AddCommand("Set-ExecutionPolicy").AddParameter("ExecutionPolicy", "Bypass").AddParameter("Scope", "CurrentUser");
                    powerShell.AddScript(filePath);

                    var scriptOutput = new PSDataCollection<PSObject>();
                    scriptOutput.DataAdded += (sender, args) =>
                    {
                        foreach (var item in scriptOutput.ReadAll())
                        {
                            UserIOImpl.PrintMessage("| " + item);
                        }
                    };

                    powerShell.Invoke<PSObject>(null, scriptOutput);

                    if (powerShell.Streams.Error != null)
                    {
                        foreach (ErrorRecord errorRecord in powerShell.Streams.Error.ReadAll())
                        {
                            if (errorRecord != null && errorRecord.ErrorDetails != null)
                            {
                                UserIOImpl.PrintMessage(errorRecord.ErrorDetails.Message);
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
