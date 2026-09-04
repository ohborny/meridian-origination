using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace MortgageLOS
{
    // =========================================================================
    // AppConfig - Reads configuration from los.config XML file
    //
    // This is a singleton because the original developer thought it was a good
    // pattern in 2005. It's not, but changing it now would require touching
    // every file in the system. So here we are.
    //
    // NOTE: Thread safety was added in 2012 after a race condition in production
    // caused 2 loans to be created with the same loan number. The lock is coarse
    // but it works. Don't remove it.
    // =========================================================================

    public class AppConfig
    {
        private static AppConfig _instance;
        private static readonly object _lock = new object();
        private readonly Dictionary<string, string> _connectionStrings;
        private readonly Dictionary<string, string> _appSettings;
        private readonly string _configPath;

        private AppConfig(string configPath)
        {
            _connectionStrings = new Dictionary<string, string>();
            _appSettings = new Dictionary<string, string>();
            _configPath = configPath;
            LoadConfig();
        }

        /// <summary>
        /// Gets the singleton instance. Uses default config path.
        /// </summary>
        public static AppConfig Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        string configPath = FindConfigFile();
                        _instance = new AppConfig(configPath);
                    }
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Re-initialize with a specific config path (for testing).
        /// </summary>
        public static AppConfig Initialize(string configPath)
        {
            lock (_lock)
            {
                _instance = new AppConfig(configPath);
                return _instance;
            }
        }

        private static string FindConfigFile()
        {
            // Try multiple locations because nobody can agree on where the config lives
            string[] candidates = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "los.config"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "los.config"),
                Path.Combine(Directory.GetCurrentDirectory(), "config", "los.config"),
                Path.Combine(Directory.GetCurrentDirectory(), "los.config"),
                // Added 2016 for when running from the BatchJobs project
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "config", "los.config"),
                // Added 2018 for Docker
                "/app/config/los.config",
            };

            foreach (string path in candidates)
            {
                try
                {
                    if (File.Exists(path))
                        return path;
                }
                catch { } // Swallow, try next
            }

            throw new FileNotFoundException(
                "los.config not found in any expected location. " +
                "Tried: " + string.Join(", ", candidates));
        }

        private void LoadConfig()
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(_configPath);

                // Load connection strings
                XmlNodeList connNodes = doc.SelectNodes("//connectionStrings/add");
                if (connNodes != null)
                {
                    foreach (XmlNode node in connNodes)
                    {
                        string name = node.Attributes["name"]?.Value;
                        string connStr = node.Attributes["connectionString"]?.Value;
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(connStr))
                        {
                            _connectionStrings[name] = connStr;
                        }
                    }
                }

                // Load app settings
                XmlNodeList settingNodes = doc.SelectNodes("//appSettings/add");
                if (settingNodes != null)
                {
                    foreach (XmlNode node in settingNodes)
                    {
                        string key = node.Attributes["key"]?.Value;
                        string val = node.Attributes["value"]?.Value;
                        if (!string.IsNullOrEmpty(key))
                        {
                            _appSettings[key] = val ?? "";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load configuration from " + _configPath + ": " + ex.Message, ex);
            }
        }

        public string GetConnectionString(string name)
        {
            string environmentValue = GetEnvironmentOverride("CONNECTIONSTRING", name);
            if (!string.IsNullOrEmpty(environmentValue))
            {
                return environmentValue;
            }

            if (_connectionStrings.TryGetValue(name, out string connStr))
            {
                return connStr;
            }
            throw new KeyNotFoundException("Connection string '" + name + "' not found in config.");
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            string environmentValue = GetEnvironmentOverride("SETTING", key);
            if (!string.IsNullOrEmpty(environmentValue))
            {
                return environmentValue;
            }

            if (_appSettings.TryGetValue(key, out string val))
            {
                return val;
            }
            return defaultValue;
        }

        private static string GetEnvironmentOverride(string category, string key)
        {
            string environmentKey = "MORTGAGELOS_" + category + "_" + ToEnvironmentKey(key);
            return System.Environment.GetEnvironmentVariable(environmentKey);
        }

        private static string ToEnvironmentKey(string value)
        {
            char[] characters = value.ToUpperInvariant().ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (!char.IsLetterOrDigit(characters[i]))
                {
                    characters[i] = '_';
                }
            }
            return new string(characters);
        }

        public int GetIntSetting(string key, int defaultValue = 0)
        {
            string val = GetSetting(key);
            if (int.TryParse(val, out int result))
            {
                return result;
            }
            return defaultValue;
        }

        public decimal GetDecimalSetting(string key, decimal defaultValue = 0m)
        {
            string val = GetSetting(key);
            if (decimal.TryParse(val, out decimal result))
            {
                return result;
            }
            return defaultValue;
        }

        public bool GetBoolSetting(string key, bool defaultValue = false)
        {
            string val = GetSetting(key);
            if (bool.TryParse(val, out bool result))
            {
                return result;
            }
            // Also accept "1" and "yes" because someone put those in the config once
            if (val == "1" || string.Equals(val, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (val == "0" || string.Equals(val, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return defaultValue;
        }

        // Convenience accessors
        public string DataRootPath => GetSetting("DataRootPath", "./data");
        public string InboundPath => GetSetting("InboundPath", "./data/inbound");
        public string OutboundPath => GetSetting("OutboundPath", "./data/outbound");
        public string BatchPath => GetSetting("BatchPath", "./data/batch");
        public string ArchivePath => GetSetting("ArchivePath", "./data/archive");
        public string Environment => GetSetting("Environment", "DEV");
        public string LogLevel => GetSetting("LogLevel", "INFO");
        public string LogFilePath => GetSetting("LogFilePath", "./logs");
        
        // HMDA settings
        public int HmdaReportingYear => GetIntSetting("HmdaReportingYear", DateTime.Now.Year);
        public string HmdaInstitutionId => GetSetting("HmdaInstitutionId");
        public string HmdaLegalEntityName => GetSetting("HmdaLegalEntityName");
        
        // TRID settings
        public int TridLeDeliveryDays => GetIntSetting("TridLeDeliveryDays", 3);
        public int TridCdWaitingDays => GetIntSetting("TridCdWaitingDays", 3);
    }
}
