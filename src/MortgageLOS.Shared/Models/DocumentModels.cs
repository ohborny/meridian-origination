using System;
using System.Collections.Generic;

namespace MortgageLOS
{
    // =========================================================================
    // Document Models - Data transfer objects for document management
    //
    // The document management module was added in 2015 and was supposed to
    // integrate with a proper DMS (Document Management System). The DMS
    // integration was never completed, so documents are tracked in the
    // los_core database with file paths pointing to a network share.
    // =========================================================================

    #region DocumentData

    /// <summary>
    /// Document tracking record.
    /// </summary>
    public class DocumentData
    {
        public int DocumentId { get; set; }
        public string LoanNumber { get; set; }
        public string DocumentType { get; set; }
        public string DocumentName { get; set; }
        public string DocumentDesc { get; set; }

        // File info
        public string FilePath { get; set; }
        public string FileExtension { get; set; }
        public long? FileSizeBytes { get; set; }
        public string FileHash { get; set; }

        // Status
        public string Status { get; set; }
        public bool IsRequired { get; set; }
        public bool IsVerified { get; set; }

        // Dates
        public DateTime? RequiredDt { get; set; }
        public DateTime? RequestedDt { get; set; }
        public DateTime? ReceivedDt { get; set; }
        public DateTime? ReviewedDt { get; set; }
        public DateTime? ExpirationDt { get; set; }

        // People
        public string RequestedBy { get; set; }
        public string ReceivedFrom { get; set; }
        public string ReviewedBy { get; set; }

        // Rejection
        public string RejectionReason { get; set; }
        public DateTime? RejectedDt { get; set; }
        public string RejectedBy { get; set; }

        // Tracking
        public DateTime CreatedDt { get; set; }
        public DateTime UpdatedDt { get; set; }
        public string CreatedBy { get; set; }

        // Convenience
        public bool IsExpired => ExpirationDt.HasValue && ExpirationDt.Value < DateTime.Now;
        public bool IsPending => Status == DocumentStatus.REQUESTED || Status == DocumentStatus.REQUIRED;
        public bool IsComplete => Status == DocumentStatus.APPROVED || Status == DocumentStatus.REVIEWED;
    }

    #endregion

    #region DocumentRequirement

    /// <summary>
    /// Defines what documents are required for a loan based on loan type and purpose.
    /// This is configured per loan type and may vary by investor.
    /// </summary>
    public class DocumentRequirement
    {
        public int RequirementId { get; set; }
        public string LoanType { get; set; }
        public string LoanPurpose { get; set; }
        public string DocumentType { get; set; }
        public string DocumentName { get; set; }
        public bool IsRequired { get; set; }
        public bool IsConditional { get; set; }
        public string ConditionDesc { get; set; }
        public int SortOrder { get; set; }

        // Added 2017 for investor-specific requirements
        public string InvestorCode { get; set; }

        // Added 2019 for automated verification
        public bool AutoVerifiable { get; set; }
        public string VerificationScript { get; set; }
    }

    #endregion

    #region DocumentChecklistResult

    /// <summary>
    /// Result of checking a loan's document checklist.
    /// </summary>
    public class DocumentChecklistResult
    {
        public string LoanNumber { get; set; }
        public List<DocumentChecklistItem> Items { get; set; } = new List<DocumentChecklistItem>();
        public bool AllRequiredDocsReceived => Items.TrueForAll(i => !i.IsRequired || i.IsReceived);
        public int TotalRequired => Items.FindAll(i => i.IsRequired).Count;
        public int TotalReceived => Items.FindAll(i => i.IsRequired && i.IsReceived).Count;
        public int TotalMissing => TotalRequired - TotalReceived;
        public int TotalExpired => Items.FindAll(i => i.IsReceived && i.IsExpired).Count;
    }

    public class DocumentChecklistItem
    {
        public string DocumentType { get; set; }
        public string DocumentName { get; set; }
        public bool IsRequired { get; set; }
        public bool IsReceived { get; set; }
        public bool IsVerified { get; set; }
        public bool IsExpired { get; set; }
        public DateTime? ReceivedDt { get; set; }
        public DateTime? ExpirationDt { get; set; }
        public string Status { get; set; }
    }

    #endregion
}
