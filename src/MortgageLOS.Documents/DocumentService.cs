using System;
using System.Data;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Documents
{
    // =========================================================================
    // DocumentService
    //
    // This is the database-backed document tracker. In 2015 this was intended
    // to be a temporary adapter in front of the DMS. The DMS project was
    // cancelled, so the adapter became the system of record instead.
    // =========================================================================
    public class DocumentService
    {
        private readonly DatabaseHelper _db;

        public DocumentService()
        {
            _db = new DatabaseHelper();
        }

        // This overload is handy for the batch jobs which already have a
        // configured helper. It also keeps all document SQL on los_core.
        public DocumentService(DatabaseHelper databaseHelper)
        {
            _db = databaseHelper ?? new DatabaseHelper();
        }

        /// <summary>
        /// Creates the document tables used by this module.
        /// </summary>
        public void EnsureDocumentTablesExist()
        {
            // TODO (2015): Replace this bootstrap SQL with the DMS schema.
            const string documentsSql = @"
CREATE TABLE IF NOT EXISTS loan_documents (
    document_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    document_type VARCHAR(40) NOT NULL,
    document_name VARCHAR(200),
    document_desc TEXT,
    file_path VARCHAR(500),
    file_extension VARCHAR(10),
    file_size_bytes BIGINT,
    file_hash VARCHAR(64),
    status VARCHAR(20) NOT NULL DEFAULT 'REQUIRED',
    is_required BOOLEAN DEFAULT TRUE,
    is_verified BOOLEAN DEFAULT FALSE,
    required_dt DATE,
    requested_dt TIMESTAMP,
    received_dt TIMESTAMP,
    reviewed_dt TIMESTAMP,
    expiration_dt DATE,
    requested_by VARCHAR(50),
    received_from VARCHAR(100),
    reviewed_by VARCHAR(50),
    rejection_reason TEXT,
    rejected_dt TIMESTAMP,
    rejected_by VARCHAR(50),
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_by VARCHAR(50) DEFAULT 'system'
);";

            const string requirementsSql = @"
CREATE TABLE IF NOT EXISTS document_requirements (
    requirement_id SERIAL PRIMARY KEY,
    loan_type VARCHAR(30) NOT NULL,
    loan_purpose VARCHAR(30),
    document_type VARCHAR(40) NOT NULL,
    document_name VARCHAR(200) NOT NULL,
    is_required BOOLEAN DEFAULT TRUE,
    is_conditional BOOLEAN DEFAULT FALSE,
    condition_desc TEXT,
    sort_order INTEGER DEFAULT 0,
    investor_code VARCHAR(20),
    auto_verifiable BOOLEAN DEFAULT FALSE,
    verification_script VARCHAR(100)
);";

            // These are deliberately separate calls. DatabaseHelper opens and
            // closes a connection for each call, as it did in the old batch code.
            _db.ExecuteCoreNonQuery(documentsSql);
            _db.ExecuteCoreNonQuery(requirementsSql);
            FileLogger.Info("Documents", "Document tables verified.");
        }

        /// <summary>
        /// Adds a received document to the loan.
        /// </summary>
        public DocumentData AddDocument(string loanNumber, string documentType,
            string documentName, string filePath, string uploadedBy)
        {
            try
            {
                string extension = string.IsNullOrEmpty(filePath)
                    ? null
                    : Path.GetExtension(filePath);
                long? fileSize = null;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    fileSize = new FileInfo(filePath).Length;
                }
                string fileHash = string.IsNullOrEmpty(filePath)
                    ? null
                    : new DocumentStorageService(_db).CalculateFileHash(filePath);

                const string sql = @"
INSERT INTO loan_documents
    (loan_number, document_type, document_name, file_path, file_extension,
     file_size_bytes, file_hash, status, is_required, is_verified, received_dt,
     received_from, created_dt, updated_dt, created_by)
VALUES
    (@loan_number, @document_type, @document_name, @file_path, @file_extension,
     @file_size_bytes, @file_hash, @status, TRUE, FALSE, CURRENT_TIMESTAMP,
     @received_from, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, @created_by)
RETURNING *;";

                DataTable table = _db.ExecuteCoreQuery(sql,
                    _db.CreateParam("@loan_number", loanNumber),
                    _db.CreateParam("@document_type", documentType),
                    _db.CreateParam("@document_name", documentName),
                    _db.CreateParam("@file_path", filePath),
                    _db.CreateParam("@file_extension", extension),
                    _db.CreateParam("@file_size_bytes", fileSize),
                    _db.CreateParam("@file_hash", fileHash),
                    _db.CreateParam("@status", DocumentStatus.RECEIVED),
                    _db.CreateParam("@received_from", uploadedBy),
                    _db.CreateParam("@created_by", uploadedBy));

                FileLogger.Info("Documents", "Received document " + documentType
                    + " for loan " + loanNumber);
                return table.Rows.Count == 0 ? null : MapDocument(table.Rows[0]);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Documents", "Unable to add document for loan "
                    + loanNumber, ex);
                throw;
            }
        }

        public List<DocumentData> GetDocuments(string loanNumber)
        {
            const string sql = @"
SELECT *
FROM loan_documents
WHERE loan_number = @loan_number
ORDER BY document_type, created_dt, document_id;";

            DataTable table = _db.ExecuteCoreQuery(sql,
                _db.CreateParam("@loan_number", loanNumber));
            return MapDocuments(table);
        }

        public List<DocumentData> GetDocumentsByType(string loanNumber, string documentType)
        {
            const string sql = @"
SELECT *
FROM loan_documents
WHERE loan_number = @loan_number
  AND document_type = @document_type
ORDER BY created_dt DESC, document_id DESC;";

            DataTable table = _db.ExecuteCoreQuery(sql,
                _db.CreateParam("@loan_number", loanNumber),
                _db.CreateParam("@document_type", documentType));
            return MapDocuments(table);
        }

        public DocumentData GetDocument(int documentId)
        {
            const string sql = @"
SELECT *
FROM loan_documents
WHERE document_id = @document_id;";

            DataTable table = _db.ExecuteCoreQuery(sql,
                _db.CreateParam("@document_id", documentId));
            return table.Rows.Count == 0 ? null : MapDocument(table.Rows[0]);
        }

        /// <summary>
        /// Verification is manual because the automated verification service
        /// was never implemented. The 2019 script column is still in the table.
        /// </summary>
        public bool VerifyDocument(int documentId, string verifiedBy)
        {
            const string sql = @"
UPDATE loan_documents
SET status = @status,
    is_verified = TRUE,
    reviewed_dt = CURRENT_TIMESTAMP,
    reviewed_by = @reviewed_by,
    updated_dt = CURRENT_TIMESTAMP
WHERE document_id = @document_id;";

            int changed = _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", DocumentStatus.REVIEWED),
                _db.CreateParam("@reviewed_by", verifiedBy),
                _db.CreateParam("@document_id", documentId));
            return changed > 0;
        }

        public bool RejectDocument(int documentId, string reason, string rejectedBy)
        {
            try
            {
                const string sql = @"
UPDATE loan_documents
SET status = @status,
    is_verified = FALSE,
    rejection_reason = @rejection_reason,
    rejected_dt = CURRENT_TIMESTAMP,
    rejected_by = @rejected_by,
    updated_dt = CURRENT_TIMESTAMP
WHERE document_id = @document_id;";

                int changed = _db.ExecuteCoreNonQuery(sql,
                    _db.CreateParam("@status", DocumentStatus.REJECTED),
                    _db.CreateParam("@rejection_reason", reason),
                    _db.CreateParam("@rejected_by", rejectedBy),
                    _db.CreateParam("@document_id", documentId));
                return changed > 0;
            }
            catch (Exception ex)
            {
                // Some callers depend on rejection being a best-effort action.
                FileLogger.Error("Documents", "Unable to reject document "
                    + documentId, ex);
                return false;
            }
        }

        public DocumentData RequestDocument(string loanNumber, string documentType,
            string requestedBy)
        {
            const string sql = @"
INSERT INTO loan_documents
    (loan_number, document_type, document_name, status, is_required,
     is_verified, requested_dt, requested_by, created_dt, updated_dt, created_by)
VALUES
    (@loan_number, @document_type, @document_type, @status, TRUE, FALSE,
     CURRENT_TIMESTAMP, @requested_by, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP,
     @created_by)
RETURNING *;";

            DataTable table = _db.ExecuteCoreQuery(sql,
                _db.CreateParam("@loan_number", loanNumber),
                _db.CreateParam("@document_type", documentType),
                _db.CreateParam("@status", DocumentStatus.REQUESTED),
                _db.CreateParam("@requested_by", requestedBy),
                _db.CreateParam("@created_by", requestedBy));
            return table.Rows.Count == 0 ? null : MapDocument(table.Rows[0]);
        }

        public bool WaiveDocument(int documentId, string waivedBy, string reason)
        {
            const string sql = @"
UPDATE loan_documents
SET status = @status,
    rejection_reason = @reason,
    reviewed_by = @waived_by,
    reviewed_dt = CURRENT_TIMESTAMP,
    updated_dt = CURRENT_TIMESTAMP
WHERE document_id = @document_id;";

            int changed = _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", DocumentStatus.WAIVED),
                _db.CreateParam("@reason", reason),
                _db.CreateParam("@waived_by", waivedBy),
                _db.CreateParam("@document_id", documentId));
            return changed > 0;
        }

        public bool UpdateDocument(DocumentData doc, string updatedBy)
        {
            if (doc == null)
            {
                return false;
            }

            try
            {
                const string sql = @"
UPDATE loan_documents
SET loan_number = @loan_number,
    document_type = @document_type,
    document_name = @document_name,
    document_desc = @document_desc,
    file_path = @file_path,
    file_extension = @file_extension,
    file_size_bytes = @file_size_bytes,
    file_hash = @file_hash,
    status = @status,
    is_required = @is_required,
    is_verified = @is_verified,
    required_dt = @required_dt,
    requested_dt = @requested_dt,
    received_dt = @received_dt,
    reviewed_dt = @reviewed_dt,
    expiration_dt = @expiration_dt,
    requested_by = @requested_by,
    received_from = @received_from,
    reviewed_by = @reviewed_by,
    rejection_reason = @rejection_reason,
    rejected_dt = @rejected_dt,
    rejected_by = @rejected_by,
    updated_dt = CURRENT_TIMESTAMP
WHERE document_id = @document_id;";

                int changed = _db.ExecuteCoreNonQuery(sql,
                    _db.CreateParam("@loan_number", doc.LoanNumber),
                    _db.CreateParam("@document_type", doc.DocumentType),
                    _db.CreateParam("@document_name", doc.DocumentName),
                    _db.CreateParam("@document_desc", doc.DocumentDesc),
                    _db.CreateParam("@file_path", doc.FilePath),
                    _db.CreateParam("@file_extension", doc.FileExtension),
                    _db.CreateParam("@file_size_bytes", doc.FileSizeBytes),
                    _db.CreateParam("@file_hash", doc.FileHash),
                    _db.CreateParam("@status", doc.Status),
                    _db.CreateParam("@is_required", doc.IsRequired),
                    _db.CreateParam("@is_verified", doc.IsVerified),
                    _db.CreateParam("@required_dt", doc.RequiredDt),
                    _db.CreateParam("@requested_dt", doc.RequestedDt),
                    _db.CreateParam("@received_dt", doc.ReceivedDt),
                    _db.CreateParam("@reviewed_dt", doc.ReviewedDt),
                    _db.CreateParam("@expiration_dt", doc.ExpirationDt),
                    _db.CreateParam("@requested_by", doc.RequestedBy),
                    _db.CreateParam("@received_from", doc.ReceivedFrom),
                    _db.CreateParam("@reviewed_by", doc.ReviewedBy),
                    _db.CreateParam("@rejection_reason", doc.RejectionReason),
                    _db.CreateParam("@rejected_dt", doc.RejectedDt),
                    _db.CreateParam("@rejected_by", doc.RejectedBy),
                    _db.CreateParam("@document_id", doc.DocumentId));
                return changed > 0;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Documents", "Unable to update document "
                    + doc.DocumentId, ex);
                return false;
            }
        }

        private static List<DocumentData> MapDocuments(DataTable table)
        {
            List<DocumentData> documents = new List<DocumentData>();
            foreach (DataRow row in table.Rows)
            {
                documents.Add(MapDocument(row));
            }
            return documents;
        }

        private static DocumentData MapDocument(DataRow row)
        {
            DocumentData doc = new DocumentData();
            doc.DocumentId = Convert.ToInt32(row["document_id"]);
            doc.LoanNumber = GetString(row, "loan_number");
            doc.DocumentType = GetString(row, "document_type");
            doc.DocumentName = GetString(row, "document_name");
            doc.DocumentDesc = GetString(row, "document_desc");
            doc.FilePath = GetString(row, "file_path");
            doc.FileExtension = GetString(row, "file_extension");
            doc.FileSizeBytes = GetNullableLong(row, "file_size_bytes");
            doc.FileHash = GetString(row, "file_hash");
            doc.Status = GetString(row, "status");
            doc.IsRequired = GetBool(row, "is_required");
            doc.IsVerified = GetBool(row, "is_verified");
            doc.RequiredDt = GetNullableDate(row, "required_dt");
            doc.RequestedDt = GetNullableDate(row, "requested_dt");
            doc.ReceivedDt = GetNullableDate(row, "received_dt");
            doc.ReviewedDt = GetNullableDate(row, "reviewed_dt");
            doc.ExpirationDt = GetNullableDate(row, "expiration_dt");
            doc.RequestedBy = GetString(row, "requested_by");
            doc.ReceivedFrom = GetString(row, "received_from");
            doc.ReviewedBy = GetString(row, "reviewed_by");
            doc.RejectionReason = GetString(row, "rejection_reason");
            doc.RejectedDt = GetNullableDate(row, "rejected_dt");
            doc.RejectedBy = GetString(row, "rejected_by");
            doc.CreatedDt = GetDate(row, "created_dt");
            doc.UpdatedDt = GetDate(row, "updated_dt");
            doc.CreatedBy = GetString(row, "created_by");
            return doc;
        }

        private static string GetString(DataRow row, string column)
        {
            return row.IsNull(column) ? null : Convert.ToString(row[column]);
        }

        private static bool GetBool(DataRow row, string column)
        {
            return !row.IsNull(column) && Convert.ToBoolean(row[column]);
        }

        private static long? GetNullableLong(DataRow row, string column)
        {
            return row.IsNull(column) ? (long?)null : Convert.ToInt64(row[column]);
        }

        private static DateTime? GetNullableDate(DataRow row, string column)
        {
            return row.IsNull(column) ? (DateTime?)null : Convert.ToDateTime(row[column]);
        }

        private static DateTime GetDate(DataRow row, string column)
        {
            return row.IsNull(column) ? DateTime.MinValue : Convert.ToDateTime(row[column]);
        }

        #region DMS INTEGRATION - NEVER COMPLETED

        // TODO (2017): Call the vendor DMS API from this method.
        private bool UploadToDms(DocumentData document)
        {
            // The vendor contract was never signed. Keep the database copy.
            return false;
        }

        // TODO (2019): Add a DMS document identifier to DocumentData.
        private string GetDmsDocumentId(int documentId)
        {
            return null;
        }

        /*
        private bool DownloadFromDms(string dmsDocumentId, string targetPath)
        {
            // This was stubbed during the 2015 proof of concept and never built.
            return false;
        }

        private bool DeleteFromDms(string dmsDocumentId)
        {
            return false;
        }
        */

        #endregion
    }
}
