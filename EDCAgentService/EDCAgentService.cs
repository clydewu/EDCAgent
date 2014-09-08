using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;

using EDCAgentCore;
using EDCLogger;

namespace EDCAgentService
{
    public partial class EDCAgentService : ServiceBase
    {
        const string kModuleName = "EDCAgent";
        const string kConfKeyListenPort = "ListenPort";
        const string kConfKeyConnectionString = "DBConnectString";
        const string kConfKeyLog4NetConfFile = "Log4NetConfFile";

        EDCLogger.EDCLogger logger;
        EDCAgent agent;

        public EDCAgentService()
        {
            int port = int.Parse(ConfigurationManager.AppSettings[kConfKeyListenPort]);
            string connectString = ConfigurationManager.AppSettings[kConfKeyConnectionString];
            string logConfFile = ConfigurationManager.AppSettings[kConfKeyLog4NetConfFile];

            InitializeComponent();

            logger = new EDCLogger.EDCLogger(kModuleName, logConfFile);
            agent = new EDCAgent(logger, port, connectString);
        }

        protected override void OnStart(string[] args)
        {
            agent.Start();
        }

        protected override void OnStop()
        {
            agent.Close();
        }
    }
}
