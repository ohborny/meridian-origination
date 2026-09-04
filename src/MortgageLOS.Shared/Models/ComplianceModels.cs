using System;
using System.Collections.Generic;

namespace MortgageLOS
{
    // =========================================================================
    // Compliance Models - Data transfer objects for regulatory compliance
    //
    // These models map to the los_compliance database. The HMDA model in
    // particular has been patched so many times that it has fields from
    // multiple regulatory vintages. Use the "New" fields for 2018+ data.
    // =========================================================================

    #region ComplianceCheckData

    /// <summary>
    /// Result of a compliance check. Maps to los_compliance.compliance_checks.
    /// </summary>
    public class ComplianceCheckData
    {
        public int ChkId { get; set; }
        public string LoanNumber { get; set; }
        public string ChkType { get; set; }
        public string ChkSubtype { get; set; }
        public string ChkStatus { get; set; }
        public string ChkResult { get; set; }
        public DateTime ChkDt { get; set; }
        public string ChkBy { get; set; }
        public string ChkVersion { get; set; }
        public string RuleSetId { get; set; }
        public bool HasException { get; set; }
        public int? ExceptionId { get; set; }
        public string ReviewedBy { get; set; }
        public DateTime? ReviewedDt { get; set; }
    }

    #endregion

    #region HmdaData

    /// <summary>
    /// HMDA record. Maps to los_compliance.hmda_data.
    /// THE 47-COLUMN TABLE. Use New fields for 2018+ data.
    /// </summary>
    public class HmdaData
    {
        public int HmdaId { get; set; }
        public string LoanNumber { get; set; }
        public int ReportingYear { get; set; }

        // OLD race/ethnicity/sex (pre-2018, kept for historical)
        public string RaceOld { get; set; }
        public string EthnicityOld { get; set; }
        public string SexOld { get; set; }

        // NEW race/ethnicity/sex (2018+, USE THESE for current data)
        public string RaceNew { get; set; }
        public string EthnicityNew { get; set; }
        public string SexNew { get; set; }
        public string RaceObserved { get; set; }
        public string SexObserved { get; set; }

        // Action
        public string ActionTaken { get; set; }
        public DateTime? ActionTakenDt { get; set; }
        public string Preapproval { get; set; }

        // Loan
        public string LoanTypeCode { get; set; }
        public string LoanPurposeCode { get; set; }
        public string LienStatus { get; set; }
        public decimal? LoanAmountHmda { get; set; }

        // Geo
        public string PropertyState { get; set; }
        public string PropertyCounty { get; set; }
        public string PropertyCensusTract { get; set; }

        // Borrower
        public int? IncomeAmount { get; set; }

        // 2018+ additions
        public string DwellingType { get; set; }
        public int TotalUnits { get; set; }
        public string OccupancyType { get; set; }

        // Rate/pricing
        public decimal? RateSpread { get; set; }
        public string HoepaStatus { get; set; }

        // 2020+ additions
        public string AusUsed { get; set; }
        public string AusResult { get; set; }
        public string AusOverrideReason { get; set; }

        // 2023+ additions
        public string ReverseMortgage { get; set; }
        public string OpenEndCredit { get; set; }
        public string BusinessPurpose { get; set; }

        // COVID (supposed to be temporary)
        public string CovidForbearance { get; set; }
        public DateTime? CovidForbearanceDt { get; set; }

        // Tracking
        public DateTime CreatedDt { get; set; }
        public DateTime UpdatedDt { get; set; }
        public string CreatedBy { get; set; }

        // Misc (nobody knows what these are)
        public string MiscFlag1 { get; set; }
        public string MiscFlag2 { get; set; }
        public string MiscRefId { get; set; }

        // Convenience
        public bool UsesNewRaceFormat => !string.IsNullOrEmpty(RaceNew);
        public bool IsOriginated => ActionTaken == HmdaAction.ORIGINATED;
        public bool IsDenied => ActionTaken == HmdaAction.DENIED;
    }

    #endregion

    #region DisclosureData

    /// <summary>
    /// Disclosure tracking record. Maps to los_compliance.disclosures.
    /// </summary>
    public class DisclosureData
    {
        public int DisclosureId { get; set; }
        public string LoanNumber { get; set; }
        public string DisclosureType { get; set; }
        public string DisclosureSubtype { get; set; }

        // Timing
        public DateTime RequiredDt { get; set; }
        public DateTime? SentDt { get; set; }
        public DateTime? ReceivedDt { get; set; }

        // Delivery
        public string DeliveryMethod { get; set; }
        public string DeliveryAddr { get; set; }

        // TRID
        public int? TridBusinessDays { get; set; }
        public bool? TridWaitingPeriodMet { get; set; }
        public bool? TridCdWaitingMet { get; set; }

        // Content
        public string VersionNumber { get; set; }
        public string PdfDocId { get; set; }

        // Tracking
        public string PreparedBy { get; set; }
        public string SentBy { get; set; }
        public DateTime CreatedDt { get; set; }
        public DateTime? EsignConsentDt { get; set; }
        public string EsignConsentIp { get; set; }

        // Convenience
        public bool IsSent => SentDt.HasValue;
        public bool IsReceived => ReceivedDt.HasValue;
        public bool IsLate => IsSent && SentDt.Value > RequiredDt;
    }

    #endregion

    #region StateDisclosureRule

    /// <summary>
    /// State-specific disclosure rule. Maps to los_compliance.state_disclosure_rules.
    /// </summary>
    public class StateDisclosureRule
    {
        public int RuleId { get; set; }
        public string StateCode { get; set; }
        public string RuleName { get; set; }
        public string RuleType { get; set; }
        public string LoanTypes { get; set; }
        public string LoanPurposes { get; set; }
        public string RuleDesc { get; set; }
        public string RequiredDocument { get; set; }
        public string TimingRequirement { get; set; }
        public int? TimingDays { get; set; }
        public decimal? MaxFeeAmount { get; set; }
        public decimal? MaxFeePct { get; set; }
        public bool IsActive { get; set; }
        public DateTime? EffectiveDt { get; set; }
        public DateTime? ExpiryDt { get; set; }
        public string RegulationCitation { get; set; }
        public string AutomationScript { get; set; }
        public bool ReviewRequired { get; set; }
        public string ReviewFrequency { get; set; }

        // Convenience
        public bool AppliesToAllLoanTypes => string.IsNullOrEmpty(LoanTypes);
        public bool AppliesToAllPurposes => string.IsNullOrEmpty(LoanPurposes);
    }

    #endregion

    #region TridTimelineData

    /// <summary>
    /// TRID timing record. Maps to los_compliance.trid_timeline.
    /// </summary>
    public class TridTimelineData
    {
        public int TridId { get; set; }
        public string LoanNumber { get; set; }
        public DateTime ApplicationDt { get; set; }

        // Loan Estimate
        public DateTime LeRequiredByDt { get; set; }
        public DateTime? LeSentDt { get; set; }
        public DateTime? LeReceivedDt { get; set; }
        public string LeVersion { get; set; }

        // Changed circumstances
        public DateTime? ChangedCircumstanceDt { get; set; }
        public string ChangedCircumstanceDesc { get; set; }
        public DateTime? RevisedLeRequiredDt { get; set; }
        public DateTime? RevisedLeSentDt { get; set; }

        // Closing Disclosure
        public DateTime? CdPreparedDt { get; set; }
        public DateTime? CdSentDt { get; set; }
        public DateTime? CdReceivedDt { get; set; }
        public DateTime? ConsummationDt { get; set; }
        public bool? CdWaitingMet { get; set; }

        // Lock
        public DateTime? RateLockDt { get; set; }
        public DateTime? RateLockExpDt { get; set; }

        // Tracking
        public DateTime CreatedDt { get; set; }
        public DateTime UpdatedDt { get; set; }

        // Tolerance
        public decimal? LeToleranceCure { get; set; }
        public decimal? CdVsLeVariance { get; set; }

        // Convenience
        public bool LeIsLate => LeSentDt.HasValue && LeSentDt.Value > LeRequiredByDt;
        public bool LeIsSent => LeSentDt.HasValue;
        public bool CdIsSent => CdSentDt.HasValue;
        public bool HasChangedCircumstance => ChangedCircumstanceDt.HasValue;
    }

    #endregion

    #region ComplianceExceptionData

    public class ComplianceExceptionData
    {
        public int ExceptionId { get; set; }
        public string LoanNumber { get; set; }
        public int? ChkId { get; set; }
        public string ExceptionType { get; set; }
        public string ExceptionReason { get; set; }
        public string RequestedBy { get; set; }
        public DateTime RequestedDt { get; set; }
        public string ApprovedBy { get; set; }
        public DateTime? ApprovedDt { get; set; }
        public string ApprovalStatus { get; set; }
        public string RiskAssessment { get; set; }
        public string RiskLevel { get; set; }
        public DateTime? ExpiryDt { get; set; }
        public string RegulatoryNotification { get; set; }
        public bool IsReportable { get; set; }
    }

    #endregion

    #region FairLendingReviewData

    public class FairLendingReviewData
    {
        public int ReviewId { get; set; }
        public string LoanNumber { get; set; }
        public DateTime ReviewDt { get; set; }
        public string ReviewType { get; set; }
        public string Reviewer { get; set; }
        public decimal? PricingDisparity { get; set; }
        public decimal? PricingDisparityThreshold { get; set; }
        public bool HasDisparity { get; set; }
        public string OverrideFactors { get; set; }
        public string ReviewStatus { get; set; }
        public string ReviewNotes { get; set; }
        public int? HmdaDataQualityScore { get; set; }
        public string HmdaDataIssues { get; set; }
    }

    #endregion

    #region ScraCheckData

    public class ScraCheckData
    {
        public int ScraId { get; set; }
        public string LoanNumber { get; set; }
        public string BorrowerName { get; set; }
        public string BorrowerSsnHash { get; set; }
        public DateTime CheckDt { get; set; }
        public bool IsActiveMilitary { get; set; }
        public string ServiceBranch { get; set; }
        public DateTime? ActiveDutyStartDt { get; set; }
        public DateTime? ActiveDutyEndDt { get; set; }
        public bool RateCapApplied { get; set; }
        public decimal? RateCapPct { get; set; }
        public bool ForeclosureProtection { get; set; }
        public string VerifiedBy { get; set; }
        public string VerificationMethod { get; set; }
        public string VerificationReference { get; set; }
    }

    #endregion
}
