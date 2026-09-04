using System;
using System.Collections.Generic;

namespace MortgageLOS
{
    // =========================================================================
    // Credit Models - Data transfer objects for credit analysis
    //
    // These models map to the los_credit database. Note that the naming
    // convention here is mixed (PascalCase and camelCase) because the credit
    // team and the core team couldn't agree on standards.
    // =========================================================================

    #region CreditReportData

    /// <summary>
    /// Credit report header. Maps to los_credit.credit_reports.
    /// </summary>
    public class CreditReportData
    {
        public int ReportId { get; set; }
        public string LoanNumber { get; set; }
        public string BorrowerSsnHash { get; set; }
        public string ReportType { get; set; }
        public string PullType { get; set; }
        public string PulledBy { get; set; }
        public DateTime PulledDt { get; set; }

        // Bureau report IDs
        public string ExperianReportId { get; set; }
        public string EquifaxReportId { get; set; }
        public string TransunionReportId { get; set; }

        // Scores
        public int? ExperianScore { get; set; }
        public int? EquifaxScore { get; set; }
        public int? TransunionScore { get; set; }
        public int? RepresentativeScore { get; set; }

        // File status
        public string FileStatus { get; set; }
        public bool FraudAlert { get; set; }
        public int ActiveAlertCount { get; set; }

        // Raw data
        public string RawReportData { get; set; }

        // Vendor
        public string VendorName { get; set; }
        public string VendorReference { get; set; }

        public DateTime CreatedDt { get; set; }

        // Convenience
        public bool HasAllScores => ExperianScore.HasValue && EquifaxScore.HasValue && TransunionScore.HasValue;
        public int LowestScore
        {
            get
            {
                int lowest = int.MaxValue;
                if (ExperianScore.HasValue) lowest = Math.Min(lowest, ExperianScore.Value);
                if (EquifaxScore.HasValue) lowest = Math.Min(lowest, EquifaxScore.Value);
                if (TransunionScore.HasValue) lowest = Math.Min(lowest, TransunionScore.Value);
                return lowest == int.MaxValue ? 0 : lowest;
            }
        }
        public int HighestScore
        {
            get
            {
                int highest = 0;
                if (ExperianScore.HasValue) highest = Math.Max(highest, ExperianScore.Value);
                if (EquifaxScore.HasValue) highest = Math.Max(highest, EquifaxScore.Value);
                if (TransunionScore.HasValue) highest = Math.Max(highest, TransunionScore.Value);
                return highest;
            }
        }
    }

    #endregion

    #region CreditScoreData

    public class CreditScoreData
    {
        public int ScoreId { get; set; }
        public int ReportId { get; set; }
        public string LoanNumber { get; set; }
        public string Bureau { get; set; }
        public string ScoreModel { get; set; }
        public int ScoreValue { get; set; }
        public string ScoreReason1 { get; set; }
        public string ScoreReason2 { get; set; }
        public string ScoreReason3 { get; set; }
        public string ScoreReason4 { get; set; }
        public bool TrendedDataAvailable { get; set; }
        public string TrendedData { get; set; }
    }

    #endregion

    #region CreditLiabilityData

    /// <summary>
    /// Individual credit trade/liability. Maps to los_credit.credit_liabilities.
    /// </summary>
    public class CreditLiabilityData
    {
        public int LiabilityId { get; set; }
        public int ReportId { get; set; }
        public string LoanNumber { get; set; }

        // Account info
        public string CreditorName { get; set; }
        public string AccountNumber { get; set; }
        public string AccountType { get; set; }
        public string AccountStatus { get; set; }

        // Financials
        public decimal? MonthlyPayment { get; set; }
        public decimal? CurrentBalance { get; set; }
        public decimal? HighCredit { get; set; }
        public decimal? PastDueAmount { get; set; }

        // History
        public int? MonthsReviewed { get; set; }
        public int Late30Count { get; set; }
        public int Late60Count { get; set; }
        public int Late90Count { get; set; }

        // DTI
        public bool IsIncludedInDti { get; set; }
        public string ExcludeReason { get; set; }

        // AUS
        public string AusResponsibleParty { get; set; }

        // Dates
        public DateTime? DateOpened { get; set; }
        public DateTime? DateReported { get; set; }

        public string Remarks { get; set; }
        public DateTime CreatedDt { get; set; }

        // Convenience
        public bool IsDelinquent => PastDueAmount.HasValue && PastDueAmount.Value > 0;
        public bool IsMortgage => AccountType == "MORTGAGE";
        public bool IsRevolving => AccountType == "REVOLVING";
    }

    #endregion

    #region CreditInquiryData

    public class CreditInquiryData
    {
        public int InquiryId { get; set; }
        public int ReportId { get; set; }
        public string LoanNumber { get; set; }
        public string InquiringCompany { get; set; }
        public DateTime InquiryDate { get; set; }
        public string InquiryType { get; set; }
        public bool IsRateShopping { get; set; }
        public int ShoppingWindowDays { get; set; }
    }

    #endregion

    #region CreditPublicRecordData

    public class CreditPublicRecordData
    {
        public int PublicRecordId { get; set; }
        public int ReportId { get; set; }
        public string LoanNumber { get; set; }
        public string RecordType { get; set; }
        public string CourtName { get; set; }
        public string CaseNumber { get; set; }
        public DateTime? FiledDate { get; set; }
        public string Disposition { get; set; }
        public DateTime? DispositionDate { get; set; }
        public decimal? Amount { get; set; }
        public bool? MeetsWaitingPeriod { get; set; }
        public int? WaitingPeriodYears { get; set; }
        public string ExtNotes { get; set; }
    }

    #endregion

    #region DtiResultData

    /// <summary>
    /// DTI calculation result. Maps to los_credit.dti_calculations.
    /// </summary>
    public class DtiResultData
    {
        public int DtiId { get; set; }
        public string LoanNumber { get; set; }
        public int? ReportId { get; set; }

        // Income
        public decimal MonthlyIncome { get; set; }
        public decimal CoborrowerIncome { get; set; }
        public decimal TotalMonthlyIncome { get; set; }

        // Debts
        public decimal TotalMonthlyDebts { get; set; }
        public decimal ProposedHousingPayment { get; set; }

        // Ratios
        public decimal? FrontEndDti { get; set; }
        public decimal? BackEndDti { get; set; }

        // Flags
        public bool ExceedsGuideline { get; set; }
        public decimal DtiGuideline { get; set; }

        // Tracking
        public string CalculatedBy { get; set; }
        public DateTime CalculatedDt { get; set; }

        // Override
        public decimal? ManualOverrideDti { get; set; }
        public string OverrideReason { get; set; }
        public string OverrideApprovedBy { get; set; }

        // Convenience
        public decimal EffectiveBackEndDti => ManualOverrideDti ?? BackEndDti ?? 0m;
        public decimal EffectiveFrontEndDti => FrontEndDti ?? 0m;
    }

    #endregion

    #region CreditPullRequest

    /// <summary>
    /// Request to pull credit (used for batch processing).
    /// </summary>
    public class CreditPullRequest
    {
        public string LoanNumber { get; set; }
        public string BorrowerFirstName { get; set; }
        public string BorrowerLastName { get; set; }
        public string BorrowerSsn { get; set; }
        public string BorrowerAddress { get; set; }
        public string BorrowerCity { get; set; }
        public string BorrowerState { get; set; }
        public string BorrowerZip { get; set; }
        public string PullType { get; set; }
        public string RequestedBy { get; set; }
        public DateTime RequestDt { get; set; }
        
        // Co-borrower (optional)
        public string CoborrowerFirstName { get; set; }
        public string CoborrowerLastName { get; set; }
        public string CoborrowerSsn { get; set; }
        public bool IsJoint { get; set; }
    }

    #endregion
}
