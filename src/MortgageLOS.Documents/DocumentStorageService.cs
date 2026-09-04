using System;
using System.Data;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Documents
{
    /// <summary>
    /// Handles the old file-share storage convention used by the LOS.
    /// </summary>
    public class DocumentStorageService
    {
        private readonly DatabaseHelper _db;

        public DocumentStorageService()
        {
            _db = new DatabaseHelper();
        }

        public DocumentStorageService(DatabaseHelper databaseHelper)
        {
            _db = databaseHelper ?? new DatabaseHelper();
        }

        public string GetDocumentPath(string loanNumber, string documentType)
        {
            // HACK: Production paths are a hardcoded network share
            // (\\los-fileserver\mortgage_documents). DataRootPath points there
            // in production, but points to ./data on developer machines.
            string root = AppConfig.Instance.GetSetting("DocumentRootPath",
                AppConfig.Instance.DataRootPath);
            return Path.Combine(root, loanNumber ?? string.Empty,
                documentType ?? string.Empty);
        }

        public bool ArchiveDocument(int documentId)
        {
            DocumentData doc = new DocumentService(_db).GetDocument(documentId);
            if (doc == null || string.IsNullOrEmpty(doc.FilePath))
            {
                return false;
            }

            try
            {
                if (!File.Exists(doc.FilePath))
                {
                    FileLogger.Warn("Documents", "Document file was not found: "
                        + doc.FilePath);
                    return false;
                }

                string archiveDirectory = Path.Combine(
                    AppConfig.Instance.GetSetting("DocumentRootPath",
                        AppConfig.Instance.DataRootPath),
                    doc.LoanNumber ?? string.Empty, "archive",
                    doc.DocumentType ?? string.Empty);
                Directory.CreateDirectory(archiveDirectory);
                string archivePath = Path.Combine(archiveDirectory,
                    Path.GetFileName(doc.FilePath));

                // File.Move(..., true) was not available in the original
                // implementation; this is still intentionally overwrite-first.
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
                File.Move(doc.FilePath, archivePath);

                const string sql = @"
UPDATE loan_documents
SET file_path = @file_path,
    updated_dt = CURRENT_TIMESTAMP
WHERE document_id = @document_id;";
                int changed = _db.ExecuteCoreNonQuery(sql,
                    _db.CreateParam("@file_path", archivePath),
                    _db.CreateParam("@document_id", documentId));
                return changed > 0;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Documents", "Unable to archive document "
                    + documentId, ex);
                return false;
            }
        }

        public long GetDocumentFileSize(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return 0;
            }

            try
            {
                return new FileInfo(filePath).Length;
            }
            catch (IOException ex)
            {
                FileLogger.Warn("Documents", "Unable to get file size: "
                    + ex.Message);
                return 0;
            }
        }

        public string CalculateFileHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                using (SHA256 sha256 = SHA256.Create())
                using (FileStream stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", string.Empty)
                        .ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Documents", "Unable to calculate hash for "
                    + filePath, ex);
                return null;
            }
        }

        public bool ValidateFileExtension(string fileName,
            string[] allowedExtensions)
        {
            if (string.IsNullOrEmpty(fileName) || allowedExtensions == null
                || allowedExtensions.Length == 0)
            {
                return false;
            }

            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }
            extension = extension.TrimStart('.');

            foreach (string allowed in allowedExtensions)
            {
                if (!string.IsNullOrEmpty(allowed)
                    && extension.Equals(allowed.TrimStart('.'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        #region DMS INTEGRATION - NEVER COMPLETED

        // TODO (2015): Replace this file move with a DMS check-in operation.
        private bool CheckInToDms(string filePath, string loanNumber)
        {
            return false;
        }

        // TODO (2019): The DMS vendor was supposed to return immutable URLs.
        private string GetDmsUrl(int documentId)
        {
            return null;
        }

        /*
        private bool MoveToDmsArchive(int documentId)
        {
            // The DMS archive endpoint was never delivered.
            return false;
        }
        */

        #endregion
    }
}
