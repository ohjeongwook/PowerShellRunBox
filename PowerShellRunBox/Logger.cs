using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerShellRunBox
{
    public static class Logger
    {
        private static StreamWriter sw = File.AppendText("log.txt");

        public static void Out(string str = "")
        {
            Console.WriteLine(str);
            sw.WriteLine(str);
        }
    }
}
