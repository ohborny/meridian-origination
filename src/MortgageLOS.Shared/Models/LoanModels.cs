using System;
using System.Collections.Generic;

namespace MortgageLOS
{
    // =========================================================================
    // Loan Models - Data transfer objects for loan origination
    //
    // These models are used across all modules. They are intentionally "fat"
    // (many fields) because the original design used DataTables and someone
    // converted them to POCOs in 2014 without normalizing.
    //
    // NOTE: Some fields are duplicated between models (e.g. borrower name
    // appears in both LoanData and BorrowerData). This is because the database
    // is denormalized. Don't try to "fix" this without understanding the sync
    // job that copies data between databases.
    // =========================================================================

    #region LoanData

    /// <summary>
    /// Main loan record. Maps to los_core.loans table.
    /// </summary>
    public class LoanData
    {
        public int LoanId { get; set; }
        public string LoanNumber { get; set; }
        public string LoanGuid { get; set; }

        // Borrower info (denormalized from customer DB)
        public string BorrowerFirstName { get; set; }
        public string BorrowerLastName { get; set; }
        public string BorrowerSsnLast4 { get; set; }
        public int? BorrowerCreditScore { get; set; }

        // Co-borrower
        public string CoborrowerFirstName { get; set; }
        public string CoborrowerLastName { get; set; }
        public int? CoborrowerCreditScore { get; set; }

        // Loan details
        public int? ProductId { get; set; }
        public string LoanPurpose { get; set; }
        public decimal LoanAmount { get; set; }
        public decimal? InterestRate { get; set; }
        public int? TermMonths { get; set; }
        public string AmortizationType { get; set; }

        // Property
        public string PropertyAddr1 { get; set; }
        public string PropertyAddr2 { get; set; }
        public string PropertyCity { get; set; }
        public string PropertyState { get; set; }
        public string PropertyZip { get; set; }
        public string PropertyType { get; set; }
        public string PropertyOccupancy { get; set; }
        public decimal? PropertyValue { get; set; }
        public decimal? AppraisedValue { get; set; }

        // LTV/DTI (cached, may be stale)
        public decimal? Ltv { get; set; }
        public decimal? Cltv { get; set; }
        public decimal? Dti { get; set; }
        public decimal? Htdti { get; set; }

        // Status
        public string LoanStatus { get; set; }
        public string LoanSubstatus { get; set; }

        // People
        public int? LoanOfficerId { get; set; }
        public int? ProcessorId { get; set; }
        public int? UnderwriterId { get; set; }
        public int? BranchId { get; set; }

        // Dates
        public DateTime? ApplicationDt { get; set; }
        public DateTime? ProcessingDt { get; set; }
        public DateTime? UnderwritingDt { get; set; }
        public DateTime? ApprovalDt { get; set; }
        public DateTime? CtcDt { get; set; }
        public DateTime? ClosingDt { get; set; }
        public DateTime? FundedDt { get; set; }
        public DateTime? ShippedDt { get; set; }
        public DateTime? PurchasedDt { get; set; }

        // Lock info
        public string LockId { get; set; }
        public DateTime? LockExpDt { get; set; }
        public decimal? LockRate { get; set; }

        // Pricing
        public decimal? BaseRate { get; set; }
        public decimal? TotalPoints { get; set; }
        public decimal? LenderCredit { get; set; }

        // Misc
        public string LoanOfficerNotes { get; set; }
        public string ExtData { get; set; }
        public string MiscFields { get; set; }

        // Tracking
        public DateTime CreatedDt { get; set; }
        public DateTime UpdatedDt { get; set; }
        public string CreatedBy { get; set; }
        public string UpdatedBy { get; set; }

        // Convenience properties (not in DB)
        public string BorrowerFullName => (BorrowerFirstName + " " + BorrowerLastName).Trim();
        public string CoborrowerFullName => (CoborrowerFirstName + " " + CoborrowerLastName).Trim();
        public bool HasCoborrower => !string.IsNullOrEmpty(CoborrowerFirstName);
        public decimal? EffectivePropertyValue => AppraisedValue ?? PropertyValue;
        public decimal CalculatedLtv => EffectivePropertyValue > 0 ? (LoanAmount / EffectivePropertyValue.Value) * 100m : 0m;
    }

    #endregion

    #region BorrowerData

    /// <summary>
    /// Borrower/customer data. Maps to los_customer.customers table.
    /// </summary>
    public class BorrowerData
    {
        public int CustomerId { get; set; }
        public string CustomerGuid { get; set; }

        // Name
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string LastName { get; set; }
        public string Suffix { get; set; }

        // PII
        public string SsnHash { get; set; }
        public string SsnLast4 { get; set; }
        public DateTime? DateOfBirth { get; set; }

        // Contact
        public string EmailAddress { get; set; }
        public string PhoneNumber { get; set; }
        public string CellPhone { get; set; }

        // Demographics (HMDA)
        public string Gender { get; set; }

        // Status
        public string CustomerStatus { get; set; }

        // Tracking
        public DateTime CreatedDt { get; set; }
        public DateTime UpdatedDt { get; set; }

        // Convenience
        public string FullName => (FirstName + " " + (MiddleName ?? "") + " " + LastName + (Suffix ?? "")).Trim();
        public int? Age => DateOfBirth.HasValue ? DateUtils.CalculateAge(DateOfBirth.Value, DateTime.Now) : (int?)null;
    }

    #endregion

    #region BorrowerAddress

    public class BorrowerAddress
    {
        public int AddressId { get; set; }
        public int CustomerId { get; set; }
        public string AddressType { get; set; }
        public string AddressLine1 { get; set; }
        public string AddressLine2 { get; set; }
        public string City { get; set; }
        public string StateCode { get; set; }
        public string ZipCode { get; set; }
        public string County { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string CensusTract { get; set; }
        public string ResidencyStatus { get; set; }
        public decimal? MonthlyHousingPayment { get; set; }
        public decimal? YearsAtAddress { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime? EffectiveDt { get; set; }
        public DateTime? EndDt { get; set; }
    }

    #endregion

    #region BorrowerEmployer

    public class BorrowerEmployer
    {
        public int EmployerId { get; set; }
        public int CustomerId { get; set; }
        public string EmployerName { get; set; }
        public string EmployerType { get; set; }
        public string Position { get; set; }
        public string EmployerAddress1 { get; set; }
        public string EmployerAddress2 { get; set; }
        public string EmployerCity { get; set; }
        public string EmployerState { get; set; }
        public string EmployerZip { get; set; }
        public string EmployerPhone { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsCurrent { get; set; }
        public decimal? AnnualIncome { get; set; }
        public decimal? MonthlyIncome { get; set; }
        public string PayFrequency { get; set; }
        public bool IsVerified { get; set; }
        public string VerificationMethod { get; set; }
        public string BusinessType { get; set; }
        public decimal? BusinessOwnershipPct { get; set; }
        public decimal? YearsInProfession { get; set; }
    }

    #endregion

    #region BorrowerIncome

    public class BorrowerIncome
    {
        public int IncomeId { get; set; }
        public int CustomerId { get; set; }
        public string IncomeType { get; set; }
        public string IncomeDesc { get; set; }
        public decimal MonthlyAmount { get; set; }
        public decimal? AnnualAmount { get; set; }
        public bool IsVerified { get; set; }
        public string VerificationDocId { get; set; }
        public bool IsStable { get; set; }
        public bool Is1099Income { get; set; }
        public DateTime? EffectiveDt { get; set; }
        public DateTime? EndDt { get; set; }
    }

    #endregion

    #region BorrowerAsset

    public class BorrowerAsset
    {
        public int AssetId { get; set; }
        public int CustomerId { get; set; }
        public string AssetType { get; set; }
        public string InstitutionName { get; set; }
        public string AccountNumber { get; set; }
        public string AccountHolderName { get; set; }
        public decimal? CurrentBalance { get; set; }
        public decimal? AvailableBalance { get; set; }
        public bool IsSourceOfFunds { get; set; }
        public decimal? FundsToCloseAmount { get; set; }
        public bool IsVerified { get; set; }
        public string VerificationDocId { get; set; }
        public int? SeasoningMonths { get; set; }
        public bool LargeDepositFlag { get; set; }
        public decimal? LargeDepositAmount { get; set; }
    }

    #endregion

    #region LoanProduct

    public class LoanProduct
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public string ProductType { get; set; }
        public int TermMonths { get; set; }
        public string AmortizationType { get; set; }
        public decimal? MinLoanAmt { get; set; }
        public decimal? MaxLoanAmt { get; set; }
        public int? MinFico { get; set; }
        public decimal? MaxLtv { get; set; }
        public decimal? MaxDti { get; set; }
        public bool IsActive { get; set; }
        public string InvestorCode { get; set; }
    }

    #endregion

    #region LoanOfficer

    public class LoanOfficer
    {
        public int LoId { get; set; }
        public string LoCode { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string NmlsId { get; set; }
        public int? BranchId { get; set; }
        public string EmailAddr { get; set; }
        public string PhoneNum { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? TermDate { get; set; }
        public bool IsActive { get; set; }
        public string FullName => (FirstName + " " + LastName).Trim();
    }

    #endregion

    #region Branch

    public class Branch
    {
        public int BranchId { get; set; }
        public string BranchCode { get; set; }
        public string BranchName { get; set; }
        public string BranchAddr1 { get; set; }
        public string BranchCity { get; set; }
        public string BranchState { get; set; }
        public string BranchZip { get; set; }
        public string BranchPhone { get; set; }
        public string NmlsId { get; set; }
        public string RegionCode { get; set; }
        public bool IsActive { get; set; }
    }

    #endregion

    #region LoanCondition

    public class LoanCondition
    {
        public int ConditionId { get; set; }
        public int LoanId { get; set; }
        public string ConditionType { get; set; }
        public string ConditionCategory { get; set; }
        public string ConditionDesc { get; set; }
        public bool IsSatisfied { get; set; }
        public string SatisfiedBy { get; set; }
        public DateTime? SatisfiedDt { get; set; }
        public string AddedBy { get; set; }
        public DateTime AddedDt { get; set; }
        public string ConditionGroup { get; set; }
        public bool IsWaivable { get; set; }
        public string WaiverApprovedBy { get; set; }
        public DateTime? WaiverApprovedDt { get; set; }
    }

    #endregion

    #region LoanPricingDetail

    public class LoanPricingDetail
    {
        public int PricingId { get; set; }
        public int LoanId { get; set; }
        public string FeeCode { get; set; }
        public string FeeDesc { get; set; }
        public decimal FeeAmount { get; set; }
        public string FeePaidBy { get; set; }
        public bool IsAprFee { get; set; }
        public string Section { get; set; }
    }

    #endregion

    #region StatusHistory

    public class StatusHistory
    {
        public int HistoryId { get; set; }
        public int LoanId { get; set; }
        public string FromStatus { get; set; }
        public string ToStatus { get; set; }
        public string ChangedBy { get; set; }
        public DateTime ChangedDt { get; set; }
        public string ChangeReason { get; set; }
        public string Notes { get; set; }
    }

    #endregion

    #region LoanApplicationRequest

    /// <summary>
    /// Request object for creating a new loan application.
    /// Used by the Web API and Origination module.
    /// </summary>
    public class LoanApplicationRequest
    {
        // Borrower
        public string BorrowerFirstName { get; set; }
        public string BorrowerLastName { get; set; }
        public string BorrowerSsn { get; set; }
        public DateTime? BorrowerDob { get; set; }
        public string BorrowerEmail { get; set; }
        public string BorrowerPhone { get; set; }

        // Co-borrower (optional)
        public string CoborrowerFirstName { get; set; }
        public string CoborrowerLastName { get; set; }
        public string CoborrowerSsn { get; set; }
        public DateTime? CoborrowerDob { get; set; }

        // Loan
        public string LoanPurpose { get; set; }
        public string ProductCode { get; set; }
        public decimal LoanAmount { get; set; }
        public decimal? PropertyValue { get; set; }
        public string PropertyAddr1 { get; set; }
        public string PropertyCity { get; set; }
        public string PropertyState { get; set; }
        public string PropertyZip { get; set; }
        public string PropertyType { get; set; }
        public string PropertyOccupancy { get; set; }

        // Income
        public decimal MonthlyIncome { get; set; }
        public decimal? CoborrowerMonthlyIncome { get; set; }

        // Loan officer
        public string LoanOfficerCode { get; set; }
        public string BranchCode { get; set; }

        // HMDA demographics
        public string HmdaRace { get; set; }
        public string HmdaEthnicity { get; set; }
        public string HmdaSex { get; set; }
    }

    #endregion
}
