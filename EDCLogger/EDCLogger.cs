using System;
using System.IO;
using System.Diagnostics;
using log4net;

namespace EDCLogger
{
    public enum LogLevel
    {
        FATAL,
        ERROR,
        WARN,
        INFO,
        DEBUG
    }

    public class EDCLogger
    {
        EventLog eventLogger;
        ILog netLogger;

        public EDCLogger(string moduleName, string log4NetConfFile)
        {
            //prepare EventLog
            if (!System.Diagnostics.EventLog.SourceExists(moduleName))
            {
                System.Diagnostics.EventLog.CreateEventSource(moduleName, "Log");
            }
            eventLogger = new EventLog();
            eventLogger.Source = moduleName;
            
            //prepare text logger
            log4net.Config.XmlConfigurator.ConfigureAndWatch(new System.IO.FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, log4NetConfFile)));
            netLogger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        public void LogEvent(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.FATAL:
                    eventLogger.WriteEntry(message, EventLogEntryType.FailureAudit);
                    break;
                case LogLevel.ERROR:
                    eventLogger.WriteEntry(message, EventLogEntryType.Error);
                    break;
                case LogLevel.WARN:
                    eventLogger.WriteEntry(message, EventLogEntryType.Warning);
                    break;
                case LogLevel.INFO:
                    eventLogger.WriteEntry(message, EventLogEntryType.Information);
                    break;
                case LogLevel.DEBUG:
                    eventLogger.WriteEntry(string.Format("[DEBUG]{0}", message), EventLogEntryType.Information);
                    break;
                default:
                    break;
            }
        }

        public void FatalEvent(string message)
        {
            eventLogger.WriteEntry(message, EventLogEntryType.FailureAudit);
        }

        public void ErrorEvent(string message)
        {
            eventLogger.WriteEntry(message, EventLogEntryType.Error);
        }

        public void WarnEvent(string message)
        {
            eventLogger.WriteEntry(message, EventLogEntryType.Warning);
        }

        public void InfoEvent(string message)
        {
            eventLogger.WriteEntry(message, EventLogEntryType.Information);
        }

        public void DebugEvent(string message)
        {
            eventLogger.WriteEntry(string.Format("[DEBUG]{0}", message), EventLogEntryType.Information);
        }

        public void LogText(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.FATAL:
                    netLogger.Fatal(message);
                    break;
                case LogLevel.ERROR:
                    netLogger.Error(message);
                    break;
                case LogLevel.WARN:
                    netLogger.Warn(message);
                    break;
                case LogLevel.INFO:
                    netLogger.Info(message);
                    break;
                case LogLevel.DEBUG:
                    netLogger.Debug(message);
                    break;
                default:
                    break;
            }
        }

        public void FatalText(string message)
        {
            netLogger.Fatal(message);
        }

        public void ErrorText(string message)
        {
            netLogger.Error(message);
        }

        public void WarnText(string message)
        {
            netLogger.Warn(message);
        }

        public void InfoText(string message)
        {
            netLogger.Info(message);
        }

        public void DebugText(string message)
        {
            netLogger.Debug(message);
        }

        public void Log(LogLevel level, string message)
        {
            LogEvent(level, message);
            LogText(level, message);
        }

        public void Fatal(string message)
        {
            LogEvent(LogLevel.FATAL, message);
            LogText(LogLevel.FATAL, message);
        }

        public void Warn(string message)
        {
            LogEvent(LogLevel.WARN, message);
            LogText(LogLevel.WARN, message);
        }

        public void Error(string message)
        {
            LogEvent(LogLevel.ERROR, message);
            LogText(LogLevel.ERROR, message);
        }

        public void Info(string message)
        {
            LogEvent(LogLevel.INFO, message);
            LogText(LogLevel.INFO, message);
        }

        public void Debug(string message)
        {
            LogEvent(LogLevel.DEBUG, message);
            LogText(LogLevel.DEBUG, message);
        }
    }
}
