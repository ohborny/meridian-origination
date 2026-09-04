using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MortgageLOS
{
    // =========================================================================
    // FileHandoffHelper - Handles file-based handoffs between modules and
    // external systems. This is how the "batch processing" works: files are
    // written to data/batch/ directories, picked up by overnight jobs, and
    // processed. Results are written back as CSV/XML files.
    //
    // This is fragile. If a file is partially written when the batch job picks
    // it up, the job will process a truncated file. There is no file locking.
    // The convention is: write to a .tmp file, then rename to .csv when done.
    // If you don't follow this convention, you WILL get truncated reads.
    // (This has happened multiple times. See INC-2019-044, INC-2020-017.)
    // =========================================================================

    public class FileHandoffHelper
    {
        private readonly AppConfig _config;

        public FileHandoffHelper()
        {
            _config = AppConfig.Instance;
        }

        public FileHandoffHelper(AppConfig config)
        {
            _config = config;
        }

        #region CSV Writing

        /// <summary>
        /// Writes a CSV file to the specified batch subdirectory.
        /// Uses the .tmp -> rename convention to prevent truncated reads.
        /// </summary>
        public string WriteCsv(string subdirectory, string fileName, List<string[]> rows)
        {
            string dir = Path.Combine(_config.BatchPath, subdirectory);
            EnsureDirectory(dir);

            string filePath = Path.Combine(dir, fileName);
            string tmpPath = filePath + ".tmp";

            try
            {
                using (StreamWriter writer = new StreamWriter(tmpPath, false, Encoding.UTF8))
                {
                    foreach (string[] row in rows)
                    {
                        string[] escaped = new string[row.Length];
                        for (int i = 0; i < row.Length; i++)
                        {
                            escaped[i] = EscapeCsvField(row[i] ?? "");
                        }
                        writer.WriteLine(string.Join(",", escaped));
                    }
                }

                // Rename .tmp to final name (atomic on most filesystems)
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tmpPath, filePath);

                FileLogger.Info("FileHandoff", "Wrote CSV: " + filePath + " (" + rows.Count + " rows)");
                return filePath;
            }
            catch (Exception ex)
            {
                FileLogger.Error("FileHandoff", "Failed to write CSV: " + filePath, ex);
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Writes a CSV file with a header row.
        /// </summary>
        public string WriteCsvWithHeader(string subdirectory, string fileName, string[] headers, List<string[]> dataRows)
        {
            List<string[]> allRows = new List<string[]> { headers };
            allRows.AddRange(dataRows);
            return WriteCsv(subdirectory, fileName, allRows);
        }

        #endregion

        #region CSV Reading

        /// <summary>
        /// Reads a CSV file and returns all rows. Assumes no header.
        /// </summary>
        public List<string[]> ReadCsv(string filePath)
        {
            List<string[]> rows = new List<string[]>();
            if (!File.Exists(filePath))
            {
                FileLogger.Warn("FileHandoff", "CSV file not found: " + filePath);
                return rows;
            }

            try
            {
                using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        rows.Add(ParseCsvLine(line));
                    }
                }
                FileLogger.Info("FileHandoff", "Read CSV: " + filePath + " (" + rows.Count + " rows)");
            }
            catch (Exception ex)
            {
                FileLogger.Error("FileHandoff", "Failed to read CSV: " + filePath, ex);
                throw;
            }
            return rows;
        }

        /// <summary>
        /// Reads a CSV file, skipping the first row (header).
        /// </summary>
        public List<string[]> ReadCsvSkipHeader(string filePath)
        {
            List<string[]> all = ReadCsv(filePath);
            if (all.Count > 0)
                all.RemoveAt(0);
            return all;
        }

        /// <summary>
        /// Reads all CSV files in a batch subdirectory.
        /// </summary>
        public Dictionary<string, List<string[]>> ReadAllCsvInDirectory(string subdirectory)
        {
            Dictionary<string, List<string[]>> result = new Dictionary<string, List<string[]>>();
            string dir = Path.Combine(_config.BatchPath, subdirectory);
            if (!Directory.Exists(dir))
                return result;

            foreach (string file in Directory.GetFiles(dir, "*.csv"))
            {
                result[file] = ReadCsv(file);
            }
            return result;
        }

        #endregion

        #region File Management

        /// <summary>
        /// Archives a processed file to the archive directory.
        /// </summary>
        public void ArchiveFile(string filePath, string archiveSubdirectory = "")
        {
            try
            {
                string archiveDir = string.IsNullOrEmpty(archiveSubdirectory)
                    ? _config.ArchivePath
                    : Path.Combine(_config.ArchivePath, archiveSubdirectory);
                EnsureDirectory(archiveDir);

                string fileName = Path.GetFileName(filePath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string archivePath = Path.Combine(archiveDir, timestamp + "_" + fileName);

                File.Move(filePath, archivePath);
                FileLogger.Info("FileHandoff", "Archived: " + filePath + " -> " + archivePath);
            }
            catch (Exception ex)
            {
                FileLogger.Error("FileHandoff", "Failed to archive: " + filePath, ex);
                throw;
            }
        }

        /// <summary>
        /// Gets all pending files in a batch subdirectory.
        /// </summary>
        public string[] GetPendingFiles(string subdirectory, string pattern = "*.csv")
        {
            string dir = Path.Combine(_config.BatchPath, subdirectory);
            if (!Directory.Exists(dir))
                return new string[0];
            return Directory.GetFiles(dir, pattern);
        }

        /// <summary>
        /// Creates a timestamped filename.
        /// </summary>
        public static string CreateTimestampedFileName(string prefix, string extension)
        {
            return string.Format("{0}_{1}.{2}", prefix, DateTime.Now.ToString("yyyyMMdd_HHmmss"), extension.TrimStart('.'));
        }

        #endregion

        #region Private Helpers

        private void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private string EscapeCsvField(string field)
        {
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        private string[] ParseCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }

        #endregion
    }
}
