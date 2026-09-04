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
    /// Builds the loan document checklist from configured requirements and
    /// documents already recorded in los_core.
    /// </summary>
    public class DocumentChecklistService
    {
        private readonly DatabaseHelper _db;
        private readonly DocumentService _documents;

        public DocumentChecklistService()
        {
            _db = new DatabaseHelper();
            _documents = new DocumentService(_db);
        }

        public DocumentChecklistService(DatabaseHelper databaseHelper)
        {
            _db = databaseHelper ?? new DatabaseHelper();
            _documents = new DocumentService(_db);
        }

        public DocumentChecklistResult GetDocumentChecklist(string loanNumber,
            string loanType, string loanPurpose)
        {
            _documents.EnsureDocumentTablesExist();
            List<DocumentRequirement> requirements = GetRequirements(loanType, loanPurpose);
            List<DocumentData> documents = _documents.GetDocuments(loanNumber);
            DocumentChecklistResult result = new DocumentChecklistResult();
            result.LoanNumber = loanNumber;

            foreach (DocumentRequirement requirement in requirements)
            {
                DocumentData document = FindBestDocument(documents,
                    requirement.DocumentType);
                DocumentChecklistItem item = new DocumentChecklistItem();
                item.DocumentType = requirement.DocumentType;
                item.DocumentName = requirement.DocumentName;
                item.IsRequired = requirement.IsRequired;
                item.IsReceived = document != null && IsReceived(document.Status);
                item.IsVerified = document != null && document.IsVerified;
                item.IsExpired = document != null
                    && (document.IsExpired || document.Status == DocumentStatus.EXPIRED);
                item.ReceivedDt = document == null ? (DateTime?)null : document.ReceivedDt;
                item.ExpirationDt = document == null
                    ? (DateTime?)null
                    : document.ExpirationDt;
                item.Status = document == null
                    ? (requirement.IsRequired
                        ? DocumentStatus.REQUIRED
                        : DocumentStatus.WAIVED)
                    : document.Status;
                result.Items.Add(item);
            }

            return result;
        }

        public List<DocumentChecklistItem> GetMissingDocuments(string loanNumber,
            string loanType, string loanPurpose)
        {
            DocumentChecklistResult checklist = GetDocumentChecklist(loanNumber,
                loanType, loanPurpose);
            List<DocumentChecklistItem> missing = new List<DocumentChecklistItem>();
            foreach (DocumentChecklistItem item in checklist.Items)
            {
                // An expired received document is not useful for the next
                // closing package, so show it in the missing list as well.
                if (item.IsRequired && (!item.IsReceived || item.IsExpired))
                {
                    missing.Add(item);
                }
            }
            return missing;
        }

        public bool CheckAllRequiredDocumentsReceived(string loanNumber,
            string loanType, string loanPurpose)
        {
            return GetMissingDocuments(loanNumber, loanType, loanPurpose).Count == 0;
        }

        public List<DocumentData> GetExpiredDocuments(string loanNumber)
        {
            // BUG (known since 2017): subtracting a day makes documents that
            // expired yesterday appear one day late in this report.
            const string sql = @"
SELECT *
FROM loan_documents
WHERE loan_number = @loan_number
  AND expiration_dt < CURRENT_DATE - INTERVAL '1 day'
  AND status <> @waived
ORDER BY expiration_dt, document_id;";

            DataTable table = _db.ExecuteCoreQuery(sql,
                _db.CreateParam("@loan_number", loanNumber),
                _db.CreateParam("@waived", DocumentStatus.WAIVED));
            List<DocumentData> expired = new List<DocumentData>();
            foreach (DataRow row in table.Rows)
            {
                expired.Add(MapDocument(row));
            }
            return expired;
        }

        /// <summary>
        /// Seeds the old, broad checklist used by the originating teams. It is
        /// intentionally idempotent because this is called from startup jobs.
        /// </summary>
        public void InitializeDefaultRequirements()
        {
            _documents.EnsureDocumentTablesExist();
            string[] loanTypes = new string[]
            {
                LoanType.CONVENTIONAL,
                LoanType.FHA,
                LoanType.VA,
                LoanType.USDA,
                LoanType.JUMBO,
                LoanType.NON_QM
            };

            foreach (string loanType in loanTypes)
            {
                AddDefault(loanType, null, DocumentType.LOAN_APPLICATION,
                    "Uniform Residential Loan Application", true, false, 10);
                AddDefault(loanType, null, DocumentType.CREDIT_REPORT,
                    "Credit Report", true, false, 20);
                AddDefault(loanType, null, DocumentType.INCOME_DOCS,
                    "Income Documentation", true, false, 30);
                AddDefault(loanType, null, DocumentType.ASSET_DOCS,
                    "Asset Documentation", true, false, 40);
                AddDefault(loanType, null, DocumentType.APPRAISAL,
                    "Appraisal", true, false, 50);
                AddDefault(loanType, null, DocumentType.TITLE_COMMITMENT,
                    "Title Commitment", true, false, 60);
                AddDefault(loanType, null, DocumentType.INSURANCE,
                    "Homeowners Insurance", true, false, 70);
                AddDefault(loanType, null, DocumentType.DISCLOSURES,
                    "Loan Disclosures", true, false, 80);
                AddDefault(loanType, null, DocumentType.BORROWER_AUTHORIZATION,
                    "Borrower Authorization", true, false, 90);
                AddDefault(loanType, null, DocumentType.LOAN_ESTIMATE,
                    "Loan Estimate", true, false, 100);
                AddDefault(loanType, null, DocumentType.CLOSING_DISCLOSURE,
                    "Closing Disclosure", true, false, 110);

                // The old team treated these as a conditional line item. The
                // condition was never evaluated; the text remains for users.
                AddDefault(loanType, LoanPurpose.PURCHASE,
                    DocumentType.PURCHASE_AGREEMENT, "Purchase Agreement",
                    true, true, 120);
                AddDefault(loanType, null, DocumentType.HOA_DOCS,
                    "HOA Documents", false, true, 130);
            }

            // Jumbo and non-QM files historically asked for tax returns.
            AddDefault(LoanType.JUMBO, null, DocumentType.TAX_RETURNS,
                "Tax Returns", true, false, 45);
            AddDefault(LoanType.NON_QM, null, DocumentType.TAX_RETURNS,
                "Tax Returns", true, false, 45);
            FileLogger.Info("Documents", "Default document requirements initialized.");
        }

        public List<DocumentRequirement> GetRequirements(string loanType,
            string loanPurpose)
        {
            const string sql = @"
SELECT *
FROM document_requirements
WHERE loan_type = @loan_type
  AND (loan_purpose = @loan_purpose
       OR loan_purpose IS NULL
       OR loan_purpose = '')
ORDER BY sort_order, requirement_id;";

            DataTable table = _db.ExecuteCoreQuery(sql,
                _db.CreateParam("@loan_type", loanType),
                _db.CreateParam("@loan_purpose", loanPurpose));
            List<DocumentRequirement> requirements = new List<DocumentRequirement>();
            foreach (DataRow row in table.Rows)
            {
                DocumentRequirement requirement = new DocumentRequirement();
                requirement.RequirementId = Convert.ToInt32(row["requirement_id"]);
                requirement.LoanType = GetString(row, "loan_type");
                requirement.LoanPurpose = GetString(row, "loan_purpose");
                requirement.DocumentType = GetString(row, "document_type");
                requirement.DocumentName = GetString(row, "document_name");
                requirement.IsRequired = GetBool(row, "is_required");
                requirement.IsConditional = GetBool(row, "is_conditional");
                requirement.ConditionDesc = GetString(row, "condition_desc");
                requirement.SortOrder = GetInt(row, "sort_order");
                requirement.InvestorCode = GetString(row, "investor_code");
                requirement.AutoVerifiable = GetBool(row, "auto_verifiable");
                requirement.VerificationScript = GetString(row, "verification_script");
                requirements.Add(requirement);
            }
            return requirements;
        }

        public int AddRequirement(DocumentRequirement req)
        {
            if (req == null)
            {
                return 0;
            }

            const string sql = @"
INSERT INTO document_requirements
    (loan_type, loan_purpose, document_type, document_name, is_required,
     is_conditional, condition_desc, sort_order, investor_code,
     auto_verifiable, verification_script)
VALUES
    (@loan_type, @loan_purpose, @document_type, @document_name, @is_required,
     @is_conditional, @condition_desc, @sort_order, @investor_code,
     @auto_verifiable, @verification_script)
RETURNING requirement_id;";

            object id = _db.ExecuteCoreScalar(sql,
                _db.CreateParam("@loan_type", req.LoanType),
                _db.CreateParam("@loan_purpose", req.LoanPurpose),
                _db.CreateParam("@document_type", req.DocumentType),
                _db.CreateParam("@document_name", req.DocumentName),
                _db.CreateParam("@is_required", req.IsRequired),
                _db.CreateParam("@is_conditional", req.IsConditional),
                _db.CreateParam("@condition_desc", req.ConditionDesc),
                _db.CreateParam("@sort_order", req.SortOrder),
                _db.CreateParam("@investor_code", req.InvestorCode),
                _db.CreateParam("@auto_verifiable", req.AutoVerifiable),
                _db.CreateParam("@verification_script", req.VerificationScript));
            return id == null || id == DBNull.Value ? 0 : Convert.ToInt32(id);
        }

        private void AddDefault(string loanType, string loanPurpose,
            string documentType, string documentName, bool required,
            bool conditional, int sortOrder)
        {
            // This is deliberately similar to AddRequirement. It was copied
            // into the startup job in 2019 instead of being refactored.
            const string sql = @"
INSERT INTO document_requirements
    (loan_type, loan_purpose, document_type, document_name, is_required,
     is_conditional, sort_order)
SELECT @loan_type, @loan_purpose, @document_type, @document_name,
       @is_required, @is_conditional, @sort_order
WHERE NOT EXISTS
    (SELECT 1
       FROM document_requirements
      WHERE loan_type = @loan_type
        AND COALESCE(loan_purpose, '') = COALESCE(@loan_purpose, '')
        AND document_type = @document_type
        AND document_name = @document_name);";

            _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@loan_type", loanType),
                _db.CreateParam("@loan_purpose", loanPurpose),
                _db.CreateParam("@document_type", documentType),
                _db.CreateParam("@document_name", documentName),
                _db.CreateParam("@is_required", required),
                _db.CreateParam("@is_conditional", conditional),
                _db.CreateParam("@sort_order", sortOrder));
        }

        private static DocumentData FindBestDocument(List<DocumentData> documents,
            string documentType)
        {
            DocumentData best = null;
            foreach (DocumentData candidate in documents)
            {
                if (candidate.DocumentType != documentType)
                {
                    continue;
                }

                if (best == null || candidate.CreatedDt > best.CreatedDt
                    || (candidate.CreatedDt == best.CreatedDt
                        && candidate.DocumentId > best.DocumentId))
                {
                    best = candidate;
                }
            }
            return best;
        }

        private static bool IsReceived(string status)
        {
            return status == DocumentStatus.RECEIVED
                || status == DocumentStatus.REVIEWED
                || status == DocumentStatus.APPROVED;
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
            doc.FileSizeBytes = row.IsNull("file_size_bytes")
                ? (long?)null : Convert.ToInt64(row["file_size_bytes"]);
            doc.FileHash = GetString(row, "file_hash");
            doc.Status = GetString(row, "status");
            doc.IsRequired = GetBool(row, "is_required");
            doc.IsVerified = GetBool(row, "is_verified");
            doc.RequiredDt = GetDate(row, "required_dt");
            doc.RequestedDt = GetDate(row, "requested_dt");
            doc.ReceivedDt = GetDate(row, "received_dt");
            doc.ReviewedDt = GetDate(row, "reviewed_dt");
            doc.ExpirationDt = GetDate(row, "expiration_dt");
            doc.RequestedBy = GetString(row, "requested_by");
            doc.ReceivedFrom = GetString(row, "received_from");
            doc.ReviewedBy = GetString(row, "reviewed_by");
            doc.RejectionReason = GetString(row, "rejection_reason");
            doc.RejectedDt = GetDate(row, "rejected_dt");
            doc.RejectedBy = GetString(row, "rejected_by");
            doc.CreatedDt = GetDateValue(row, "created_dt");
            doc.UpdatedDt = GetDateValue(row, "updated_dt");
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

        private static int GetInt(DataRow row, string column)
        {
            return row.IsNull(column) ? 0 : Convert.ToInt32(row[column]);
        }

        private static DateTime? GetDate(DataRow row, string column)
        {
            return row.IsNull(column) ? (DateTime?)null
                : Convert.ToDateTime(row[column]);
        }

        private static DateTime GetDateValue(DataRow row, string column)
        {
            return row.IsNull(column) ? DateTime.MinValue
                : Convert.ToDateTime(row[column]);
        }
    }
}
