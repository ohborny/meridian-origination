using System;
using System.Collections.Generic;

namespace MortgageLOS
{
    // =========================================================================
    // Enums and Constants for the MortgageLOS system
    // 
    // This file has been here since 2005. Do NOT remove any values even if they
    // appear unused - some are used in the database and removing them will break
    // deserialization. (See LOS-451 from 2008.)
    //
    // NOTE: String values must match database column values exactly.
    // =========================================================================

    #region Loan Status

    /// <summary>
    /// Loan pipeline status. Values map to loans.loan_status in los_core.
    /// </summary>
    public static class LoanStatus
    {
        public const string APPLICATION = "APPLICATION";
        public const string PROCESSING = "PROCESSING";
        public const string UNDERWRITING = "UNDERWRITING";
        public const string CONDITIONAL_APPROVAL = "CONDITIONAL_APPROVAL";
        public const string CLEAR_TO_CLOSE = "CLEAR_TO_CLOSE";
        public const string CLOSING = "CLOSING";
        public const string FUNDED = "FUNDED";
        public const string SHIPPED = "SHIPPED";
        public const string PURCHASED = "PURCHASED";
        // Terminal states
        public const string SUSPENDED = "SUSPENDED";
        public const string DENIED = "DENIED";
        public const string WITHDRAWN = "WITHDRAWN";
        public const string CANCELLED = "CANCELLED";
        
        // TODO: Add COUNTEROFFER status (requested by UW team 2019, never implemented)
        // TODO: Add PRE_APPROVAL status (requested by sales 2020, never implemented)
        
        public static readonly string[] ActiveStatuses = {
            APPLICATION, PROCESSING, UNDERWRITING, CONDITIONAL_APPROVAL,
            CLEAR_TO_CLOSE, CLOSING, FUNDED, SHIPPED
        };
        
        public static readonly string[] TerminalStatuses = {
            PURCHASED, SUSPENDED, DENIED, WITHDRAWN, CANCELLED
        };
        
        // Added 2016 - the "correct" order for pipeline progression
        // Some loans skip steps (e.g. streamline refis skip UNDERWRITING)
        public static readonly List<string> PipelineOrder = new List<string> {
            APPLICATION, PROCESSING, UNDERWRITING, CONDITIONAL_APPROVAL,
            CLEAR_TO_CLOSE, CLOSING, FUNDED, SHIPPED, PURCHASED
        };
        
        public static int GetPipelineOrder(string status)
        {
            int idx = PipelineOrder.IndexOf(status);
            return idx >= 0 ? idx : -1;
        }
    }

    #endregion

    #region Loan Purpose

    public static class LoanPurpose
    {
        public const string PURCHASE = "PURCHASE";
        public const string REFINANCE = "REFINANCE";
        public const string CASHOUT_REFI = "CASHOUT_REFI";
        public const string STREAMLINE = "STREAMLINE";
        // Deprecated - use CASHOUT_REFI instead (changed 2014 but old loans still have this)
        public const string CASH_OUT = "CASH_OUT";
        // Deprecated - use STREAMLINE instead
        public const string FHA_STREAMLINE = "FHA_STREAMLINE";
        // Added 2018 for home equity
        public const string HOME_EQUITY = "HOME_EQUITY";
        
        // TODO: Add CONSTRUCTION_TO_PERM (requested 2021, not yet implemented)
    }

    #endregion

    #region Loan Type / Product

    public static class LoanType
    {
        public const string CONVENTIONAL = "CONVENTIONAL";
        public const string FHA = "FHA";
        public const string VA = "VA";
        public const string USDA = "USDA";
        public const string JUMBO = "JUMBO";
        public const string NON_QM = "NON_QM";
        
        // HMDA loan type codes (for reporting)
        public static readonly Dictionary<string, string> HmdaCodes = new Dictionary<string, string>
        {
            { CONVENTIONAL, "1" },
            { FHA, "2" },
            { VA, "3" },
            { USDA, "4" }
        };
    }

    #endregion

    #region Property

    public static class PropertyType
    {
        public const string SFR = "SFR";           // Single Family Residence
        public const string CONDO = "CONDO";
        public const string TOWNHOUSE = "TOWNHOUSE";
        public const string MULTI = "MULTI";       // Multi-family (2-4 units)
        public const string MANUFACTURED = "MANUFACTURED";
        public const string PUD = "PUD";           // Planned Unit Development
        // Deprecated
        public const string SFD = "SFD";           // Old code for SFR, don't use
    }

    public static class OccupancyType
    {
        public const string PRIMARY = "PRIMARY";
        public const string SECONDARY = "SECONDARY";
        public const string INVESTMENT = "INVESTMENT";
    }

    #endregion

    #region Credit

    public static class CreditBureau
    {
        public const string EXPERIAN = "EXPERIAN";
        public const string EQUIFAX = "EQUIFAX";
        public const string TRANSUNION = "TRANSUNION";
        
        // Score models
        public const string FICO_8 = "FICO_8";
        public const string FICO_9 = "FICO_9";
        public const string VANTAGE_3 = "VANTAGE_3";
        public const string VANTAGE_4 = "VANTAGE_4";
    }

    public static class CreditPullType
    {
        public const string SOFT = "SOFT";
        public const string HARD = "HARD";
        public const string REISSUE = "REISSUE";
    }

    #endregion

    #region Compliance

    public static class ComplianceCheckType
    {
        public const string HMDA = "HMDA";
        public const string TRID = "TRID";
        public const string HOEPA = "HOEPA";
        public const string HPML = "HPML";           // Higher-Priced Mortgage Loan
        public const string QM = "QM";               // Qualified Mortgage
        public const string SCRA = "SCRA";
        public const string FAIR_LENDING = "FAIR_LENDING";
        public const string STATE = "STATE";
    }

    public static class ComplianceStatus
    {
        public const string PASS = "PASS";
        public const string FAIL = "FAIL";
        public const string WARNING = "WARNING";
        public const string MANUAL_REVIEW = "MANUAL_REVIEW";
        public const string NOT_APPLICABLE = "NOT_APPLICABLE";
    }

    public static class DisclosureType
    {
        public const string LOAN_ESTIMATE = "LE";
        public const string CLOSING_DISCLOSURE = "CD";
        public const string AFFILIATED_BUSINESS = "AFFILIATED_BUSINESS";
        public const string SERVICING = "SERVICING";
        public const string APPRAISAL = "APPRAISAL";
        public const string SPECIAL = "SPECIAL";
    }

    // HMDA action taken codes
    public static class HmdaAction
    {
        public const string ORIGINATED = "1";
        public const string APPROVED_NOT_ACCEPTED = "2";
        public const string DENIED = "3";
        public const string WITHDRAWN = "4";
        public const string CLOSED_INCOMPLETE = "5";
        public const string PURCHASED_LOAN = "6";
        public const string PREAPPROVAL_DENIED = "7";
        public const string PREAPPROVAL_APPROVED_NOT_ACCEPTED = "8";
    }

    #endregion

    #region Underwriting

    public static class AusEngine
    {
        public const string DU = "DU";     // Desktop Underwriter (Fannie Mae)
        public const string LP = "LP";     // Loan Product Advisor (Freddie Mac)
        public const string GUS = "GUS";   // Guaranteed Underwriting System (USDA)
        public const string MANUAL = "MANUAL";
    }

    public static class AusResult
    {
        public const string APPROVE_ELIGIBLE = "APPROVE_ELIGIBLE";
        public const string APPROVE_INELIGIBLE = "APPROVE_INELIGIBLE";
        public const string REFER_ELIGIBLE = "REFER_ELIGIBLE";
        public const string REFER_INELIGIBLE = "REFER_INELIGIBLE";
        public const string REFER_WITH_CAUTION = "REFER_WITH_CAUTION";
        public const string OUT_OF_SCOPE = "OUT_OF_SCOPE";
        public const string ERROR = "ERROR";
    }

    public static class UnderwritingDecision
    {
        public const string APPROVED = "APPROVED";
        public const string APPROVED_WITH_CONDITIONS = "APPROVED_WITH_CONDITIONS";
        public const string SUSPENDED = "SUSPENDED";
        public const string DENIED = "DENIED";
        // Added 2019
        public const string COUNTER_OFFER = "COUNTER_OFFER";
    }

    public static class ConditionType
    {
        public const string PRIOR_TO_FINAL = "PRIOR_TO_FINAL";
        public const string PRIOR_TO_DOCS = "PRIOR_TO_DOCS";
        public const string PRIOR_TO_FUNDING = "PRIOR_TO_FUNDING";
        public const string PRIOR_TO_PURCHASE = "PRIOR_TO_PURCHASE";
    }

    #endregion

    #region Documents

    public static class DocumentType
    {
        public const string LOAN_APPLICATION = "1003";
        public const string CREDIT_REPORT = "CREDIT_REPORT";
        public const string APPRAISAL = "APPRAISAL";
        public const string TITLE_COMMITMENT = "TITLE_COMMITMENT";
        public const string INCOME_DOCS = "INCOME_DOCS";       // W2s, paystubs
        public const string ASSET_DOCS = "ASSET_DOCS";          // Bank statements
        public const string TAX_RETURNS = "TAX_RETURNS";
        public const string INSURANCE = "INSURANCE";
        public const string LOAN_ESTIMATE = "LE";
        public const string CLOSING_DISCLOSURE = "CD";
        public const string PURCHASE_AGREEMENT = "PURCHASE_AGREEMENT";
        public const string HOA_DOCS = "HOA_DOCS";
        public const string VERIFICATION_OF_EMPLOYMENT = "VOE";
        public const string VERIFICATION_OF_RENT = "VOR";
        public const string DISCLOSURES = "DISCLOSURES";
        // Added 2017
        public const string BORROWER_AUTHORIZATION = "BORROWER_AUTH";
        // Added 2020
        public const string COVID_HARDSHIP = "COVID_HARDSHIP";
    }

    public static class DocumentStatus
    {
        public const string REQUIRED = "REQUIRED";
        public const string REQUESTED = "REQUESTED";
        public const string RECEIVED = "RECEIVED";
        public const string REVIEWED = "REVIEWED";
        public const string APPROVED = "APPROVED";
        public const string REJECTED = "REJECTED";
        public const string WAIVED = "WAIVED";
        public const string EXPIRED = "EXPIRED";
    }

    #endregion

    #region Misc Constants

    // DTI limits by loan type (as of 2024 QM rules)
    public static class DtiLimits
    {
        public const decimal QM_MAX_DTI = 43.00m;       // Qualified Mortgage threshold
        public const decimal CONV_MAX_DTI = 50.00m;     // Conventional max (with AUS approval)
        public const decimal FHA_MAX_DTI = 56.99m;      // FHA max with AUS
        public const decimal VA_MAX_DTI = 41.00m;       // VA residual income test
        public const decimal USDA_MAX_DTI = 44.00m;     // USDA GUS
        // Non-QM can go higher, case by case
        public const decimal NON_QM_MAX_DTI = 55.00m;
    }

    // LTV limits by loan type
    public static class LtvLimits
    {
        public const decimal CONV_PURCHASE_MAX_LTV = 97.00m;    // With MI
        public const decimal CONV_REFI_MAX_LTV = 95.00m;
        public const decimal CONV_CASHOUT_MAX_LTV = 80.00m;
        public const decimal FHA_PURCHASE_MAX_LTV = 96.50m;
        public const decimal FHA_REFI_MAX_LTV = 97.75m;
        public const decimal VA_MAX_LTV = 100.00m;       // No LTV limit for VA
        public const decimal USDA_MAX_LTV = 100.00m;
        public const decimal JUMBO_MAX_LTV = 90.00m;
    }

    // Minimum credit scores by loan type
    public static class MinCreditScores
    {
        public const int CONV_PURCHASE = 620;
        public const int CONV_REFI = 620;
        public const int FHA_PURCHASE = 580;  // With 3.5% down. 500-579 needs 10% down
        public const int VA = 580;            // VA doesn't set min, but investors do
        public const int USDA = 640;
        public const int JUMBO = 700;
        public const int NON_QM = 660;
    }

    // US States and territories (for compliance)
    public static class UsStates
    {
        public static readonly string[] AllStates = {
            "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA",
            "KS","KY","LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
            "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT",
            "VA","WA","WV","WI","WY","DC","PR"
        };

        // States with special disclosure requirements (as of 2024)
        // NOTE: This is NOT exhaustive. The state_disclosure_rules table in los_compliance
        // is the source of truth. This array is for quick reference only.
        public static readonly string[] SpecialDisclosureStates = {
            "CA","TX","NY","FL","MN","MD","MA","NC"
        };
    }

    // Business holidays (US federal holidays, used for TRID timing)
    // NOTE: This is hardcoded and needs manual updates every year.
    // TODO: Move to a holiday table in the database (requested 2018, never done)
    public static class Holidays
    {
        public static readonly string[] Holidays2024 = {
            "2024-01-01",  // New Year's Day
            "2024-01-15",  // MLK Day
            "2024-02-19",  // Presidents Day
            "2024-05-27",  // Memorial Day
            "2024-06-19",  // Juneteenth
            "2024-07-04",  // Independence Day
            "2024-09-02",  // Labor Day
            "2024-10-14",  // Columbus Day
            "2024-11-11",  // Veterans Day
            "2024-11-28",  // Thanksgiving
            "2024-12-25",  // Christmas
        };

        public static readonly string[] Holidays2025 = {
            "2025-01-01", "2025-01-20", "2025-02-17", "2025-05-26",
            "2025-06-19", "2025-07-04", "2025-09-01", "2025-10-13",
            "2025-11-11", "2025-11-27", "2025-12-25",
        };

        public static readonly string[] Holidays2026 = {
            "2026-01-01", "2026-01-19", "2026-02-16", "2026-05-25",
            "2026-06-19", "2026-07-04", "2026-09-07", "2026-10-12",
            "2026-11-11", "2026-11-26", "2026-12-25",
        };
    }

    #endregion
}
