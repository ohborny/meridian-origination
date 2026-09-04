using System;
using System.IO;
using System.Threading;

namespace MortgageLOS
{
    // =========================================================================
    // FileLogger - Simple file-based logging
    //
    // This was written in 2005 before we had proper logging frameworks.
    // We looked at replacing it with log4net in 2014 but decided the migration
    // risk was too high. So we're still using this.
    //
    // NOTE: This is NOT thread-safe for file writes. The lock helps but if
    // multiple processes write to the same log file (e.g. batch jobs), you
    // may get interleaved lines. This is a known issue. See LOS-1102.
    // =========================================================================

    public enum LogLevel
    {
        DEBUG = 0,
        INFO = 1,
        WARN = 2,
        ERROR = 3,
        FATAL = 4
    }

    public static class FileLogger
    {
        private static readonly object _lock = new object();
        private static LogLevel _minLevel = LogLevel.INFO;
        private static string _logDirectory = "./logs";

        public static void Initialize(string logDirectory, string level = "INFO")
        {
            _logDirectory = logDirectory;
            if (Enum.TryParse<LogLevel>(level, true, out LogLevel parsed))
            {
                _minLevel = parsed;
            }
            
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch { }
        }

        public static void Debug(string source, string message)
        {
            Log(LogLevel.DEBUG, source, message, null);
        }

        public static void Info(string source, string message)
        {
            Log(LogLevel.INFO, source, message, null);
        }

        public static void Warn(string source, string message)
        {
            Log(LogLevel.WARN, source, message, null);
        }

        public static void Error(string source, string message, Exception ex = null)
        {
            Log(LogLevel.ERROR, source, message, ex);
        }

        public static void Fatal(string source, string message, Exception ex = null)
        {
            Log(LogLevel.FATAL, source, message, ex);
        }

        private static void Log(LogLevel level, string source, string message, Exception ex)
        {
            if ((int)level < (int)_minLevel)
                return;

            try
            {
                lock (_lock)
                {
                    string logFile = Path.Combine(_logDirectory, "mortgage_los_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    string logLine = string.Format("[{0}] [{1}] [{2}] {3}",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        level.ToString(),
                        source,
                        message);

                    if (ex != null)
                    {
                        logLine += Environment.NewLine + "  Exception: " + ex.GetType().Name + ": " + ex.Message;
                        if (ex.InnerException != null)
                        {
                            logLine += Environment.NewLine + "  Inner: " + ex.InnerException.Message;
                        }
                        logLine += Environment.NewLine + "  Stack: " + ex.StackTrace;
                    }

                    File.AppendAllText(logFile, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // If logging fails, we can't really log the failure, can we?
                // This is the kind of thing that keeps people up at night.
            }
        }
    }
}
