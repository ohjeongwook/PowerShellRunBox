using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerShellRunBox
{
    class ConsoleIO : UserIO
    {
        public ConsoleIO()
        {
        }

        private void ConsoleWrite(string message)
        {
            Console.Write(message);
        }

        private void ConsoleWriteLn(string message)
        {
            Console.Write(message);
            Console.Write("\n");
        }

        private void ConsoleWrite(string message, ConsoleColor fgColor, ConsoleColor bgColor)
        {
            ConsoleColor saveFGColor = Console.ForegroundColor;
            ConsoleColor saveBGColor = Console.BackgroundColor;
            Console.ForegroundColor = fgColor;
            Console.BackgroundColor = bgColor;
            Console.Write(message);
            Console.ForegroundColor = saveFGColor;
            Console.BackgroundColor = saveBGColor;
        }

        private void ConsoleWriteLn(string message, ConsoleColor fgColor, ConsoleColor bgColor)
        {
            ConsoleWrite(message, fgColor, bgColor);
            ConsoleWrite("\n", fgColor, bgColor);
        }

        public void PrintCode(string message, string fgColor = "White", string bgColor = "Black")
        {
            ConsoleWrite(message,
                (ConsoleColor)Enum.Parse(typeof(ConsoleColor), fgColor),
                (ConsoleColor)Enum.Parse(typeof(ConsoleColor), bgColor));
        }

        public void PrintVariable(string message)
        {
            ConsoleWrite(message);
        }

        public void PrintError(string message)
        {
            ConsoleWrite(message);
        }

        public void PrintMessage(string message, string fgColor = "White", string bgColor = "Black")
        {
            ConsoleWrite(message,
                (ConsoleColor)Enum.Parse(typeof(ConsoleColor), fgColor),
                (ConsoleColor)Enum.Parse(typeof(ConsoleColor), bgColor));
            ConsoleWrite("\n");
        }

        public string GetInput(string message)
        {
            ConsoleWrite(message);
            return Console.ReadLine();
        }
    }
}
