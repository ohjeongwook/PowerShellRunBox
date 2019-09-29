using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerShellRunBox
{
    interface UserIO
    {
        void PrintCode(string message, string fgColor = "White", string bgColor = "Black");
        void PrintVariable(string message);
        void PrintError(string message);
        void PrintMessage(string message, string fgColor = "White", string bgColor = "Black");
        string GetInput(string message);
    }
}
