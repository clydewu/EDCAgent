using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Diagnostics;

using EDCAgentCore;
using EDCLogger;

namespace EDCAgentConsole
{
    class Program
    {
        const string kModuleName = "EDCAgent";
        const string kConfKeyListenPort = "ListenPort";
        const string kConfKeyConnectionString = "DBConnectString";
        const string kConfKeyLog4NetConfFile = "Log4NetConfFile";

        static EDCAgent agent;

        //[DllImport("Kernel32")]
        //public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

        static void Main(string[] args)
        {
            ConsoleKeyInfo ck1;
            Console.CancelKeyPress += new ConsoleCancelEventHandler(stop);
            int port = int.Parse(ConfigurationManager.AppSettings[kConfKeyListenPort]);
            string connectString = ConfigurationManager.AppSettings[kConfKeyConnectionString];
            string logConfFile = ConfigurationManager.AppSettings[kConfKeyLog4NetConfFile];
            System.Diagnostics.EventLog eventLog = new EventLog();

            EDCLogger.EDCLogger logger = new EDCLogger.EDCLogger(kModuleName, logConfFile);
            agent = new EDCAgent(logger, port, connectString);

            agent.Start();
        }

        protected static void stop(object sender, ConsoleCancelEventArgs e)
        {
            agent.Close();
            System.Environment.Exit(0);
        }
    }
}
