using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;
using MortgageLOS.Credit;

namespace MortgageLOS.Underwriting
{
    // =========================================================================
    // UnderwritingWorkflowService - Orchestrates the underwriting process
    //
    // This service ties together AUS, eligibility, DTI, and conditions into
    // a single workflow. It was added in 2015 when the manual workflow became
    // too error-prone.
    // =========================================================================

    #region Models

    public class UnderwritingResult
    {
        public string LoanNumber { get; set; }
        public AusResultData AusResult { get; set; }
        public EligibilityResult Eligibility { get; set; }
        public DtiResultData Dti { get; set; }
        public string Decision { get; set; }
        public List<LoanCondition> Conditions { get; set; } = new List<LoanCondition>();
        public Dictionary<string, object> Summary { get; set; } = new Dictionary<string, object>();
    }

    #endregion

    public class UnderwritingWorkflowService
    {
        private readonly DatabaseHelper _db;
        private readonly AusService _ausService;
        private readonly UnderwritingService _uwService;
        private readonly CreditReportService _creditService;
        private readonly DtiCalculator _dtiCalculator;

        public UnderwritingWorkflowService()
        {
            _db = new DatabaseHelper();
            _ausService = new AusService();
            _uwService = new UnderwritingService();
            _creditService = new CreditReportService();
            _dtiCalculator = new DtiCalculator();
        }

        /// <summary>
        /// Processes a loan through the full underwriting workflow.
        /// </summary>
        public UnderwritingResult ProcessLoanForUnderwriting(string loanNumber, string underwriterId)
        {
            LoanData loan = _uwService.GetType().GetMethod("CheckEligibility") != null ? GetLoanData(loanNumber) : null;
            loan = GetLoanData(loanNumber);
            if (loan == null)
                throw new Exception("Loan not found: " + loanNumber);

            // Submit for underwriting if not already
            if (loan.LoanStatus != LoanStatus.UNDERWRITING && loan.LoanStatus != LoanStatus.PROCESSING)
            {
                throw new Exception("Loan must be in PROCESSING or UNDERWRITING status. Current: " + loan.LoanStatus);
            }

            if (loan.LoanStatus == LoanStatus.PROCESSING)
            {
                _uwService.SubmitForUnderwriting(loanNumber, underwriterId);
            }

            // Run AUS
            string engine = _ausService.SelectEngine(GetLoanType(loan));
            AusResultData ausResult = _ausService.RunAus(loanNumber, engine);

            // Check eligibility
            EligibilityResult eligibility = _uwService.CheckEligibility(loanNumber);

            // Get DTI
            DtiResultData dti = _dtiCalculator.GetDtiResult(loanNumber);

            // Get conditions
            List<LoanCondition> conditions = _uwService.GetConditions(loanNumber);

            // Make preliminary decision
            string decision;
            if (ausResult.Result == AusResult.APPROVE_ELIGIBLE && eligibility.IsEligible)
            {
                decision = UnderwritingDecision.APPROVED_WITH_CONDITIONS;
            }
            else if (ausResult.Result == AusResult.REFER_ELIGIBLE && eligibility.IsEligible)
            {
                decision = UnderwritingDecision.APPROVED_WITH_CONDITIONS;
            }
            else if (ausResult.Result == AusResult.REFER_WITH_CAUTION)
            {
                decision = UnderwritingDecision.SUSPENDED;
            }
            else if (ausResult.Result == AusResult.REFER_INELIGIBLE || ausResult.Result == AusResult.OUT_OF_SCOPE)
            {
                decision = eligibility.IsEligible ? UnderwritingDecision.SUSPENDED : UnderwritingDecision.DENIED;
            }
            else
            {
                decision = UnderwritingDecision.SUSPENDED;
            }

            // Build summary
            Dictionary<string, object> summary = new Dictionary<string, object>();
            summary["loanNumber"] = loanNumber;
            summary["loanStatus"] = loan.LoanStatus;
            summary["ausResult"] = ausResult.Result;
            summary["ausEngine"] = ausResult.Engine;
            summary["creditScore"] = ausResult.CreditScore;
            summary["dti"] = dti != null ? dti.EffectiveBackEndDti : 0m;
            summary["ltv"] = loan.Ltv ?? 0m;
            summary["eligible"] = eligibility.IsEligible;
            summary["decision"] = decision;
            summary["conditionCount"] = conditions.Count;
            summary["outstandingConditions"] = _uwService.GetOutstandingConditions(loanNumber).Count;

            FileLogger.Info("Underwriting", "ProcessLoanForUnderwriting: " + loanNumber + " - Decision: " + decision);

            return new UnderwritingResult
            {
                LoanNumber = loanNumber,
                AusResult = ausResult,
                Eligibility = eligibility,
                Dti = dti,
                Decision = decision,
                Conditions = conditions,
                Summary = summary
            };
        }

        /// <summary>
        /// Clears a loan to close if all conditions, compliance, and documents are satisfied.
        /// </summary>
        public bool ClearToClose(string loanNumber, string clearedBy)
        {
            // Check all conditions satisfied
            if (!_uwService.CheckAllConditionsSatisfied(loanNumber))
            {
                FileLogger.Warn("Underwriting", "ClearToClose: Outstanding conditions for " + loanNumber);
                return false;
            }

            // Check compliance passed
            if (!CheckCompliancePassed(loanNumber))
            {
                FileLogger.Warn("Underwriting", "ClearToClose: Compliance not passed for " + loanNumber);
                return false;
            }

            // Check documents received
            if (!CheckDocumentsReceived(loanNumber))
            {
                FileLogger.Warn("Underwriting", "ClearToClose: Missing documents for " + loanNumber);
                return false;
            }

            // Update status to CLEAR_TO_CLOSE
            LoanData loan = GetLoanData(loanNumber);
            string sql = @"UPDATE loans SET loan_status = @status, ctc_dt = @now,
                updated_dt = @now, updated_by = @clearedBy WHERE loan_id = @loanId";
            _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", LoanStatus.CLEAR_TO_CLOSE, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@clearedBy", clearedBy, DbType.String),
                _db.CreateParam("@loanId", loan.LoanId, DbType.Int32));

            FileLogger.Info("Underwriting", "Loan " + loanNumber + " cleared to close by " + clearedBy);
            return true;
        }

        /// <summary>
        /// Gets an underwriting summary for a loan.
        /// </summary>
        public Dictionary<string, object> GetUnderwritingSummary(string loanNumber)
        {
            Dictionary<string, object> summary = new Dictionary<string, object>();
            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                return summary;

            summary["loanNumber"] = loanNumber;
            summary["loanStatus"] = loan.LoanStatus;
            summary["loanAmount"] = loan.LoanAmount;
            summary["ltv"] = loan.Ltv;
            summary["dti"] = loan.Dti;
            summary["creditScore"] = loan.BorrowerCreditScore;

            AusResultData aus = _ausService.GetAusResult(loanNumber);
            if (aus != null)
            {
                summary["ausResult"] = aus.Result;
                summary["ausEngine"] = aus.Engine;
            }

            List<LoanCondition> conditions = _uwService.GetConditions(loanNumber);
            summary["totalConditions"] = conditions.Count;
            summary["outstandingConditions"] = _uwService.GetOutstandingConditions(loanNumber).Count;

            return summary;
        }

        #region Private Helpers

        private LoanData GetLoanData(string loanNumber)
        {
            string sql = "SELECT * FROM loans WHERE loan_number = @loanNumber";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
            if (dt.Rows.Count == 0)
                return null;

            DataRow row = dt.Rows[0];
            return new LoanData
            {
                LoanId = Convert.ToInt32(row["loan_id"]),
                LoanNumber = row["loan_number"]?.ToString(),
                BorrowerCreditScore = row["borrower_credit_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["borrower_credit_score"]),
                ProductId = row["product_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["product_id"]),
                LoanAmount = row["loan_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(row["loan_amount"]),
                Ltv = row["ltv"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["ltv"]),
                Dti = row["dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["dti"]),
                LoanStatus = row["loan_status"]?.ToString()
            };
        }

        private string GetLoanType(LoanData loan)
        {
            if (loan.ProductId == null || loan.ProductId <= 0)
                return LoanType.CONVENTIONAL;

            string sql = "SELECT product_type FROM loan_products WHERE product_id = @id";
            object result = _db.ExecuteCoreScalar(sql, _db.CreateParam("@id", loan.ProductId.Value, DbType.Int32));
            return result?.ToString() ?? LoanType.CONVENTIONAL;
        }

        private bool CheckCompliancePassed(string loanNumber)
        {
            try
            {
                string sql = "SELECT COUNT(*) FROM compliance_checks WHERE loan_number = @loanNumber AND chk_status = 'FAIL'";
                object count = _db.ExecuteComplianceScalar(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
                return count == null || Convert.ToInt32(count) == 0;
            }
            catch
            {
                // If compliance DB is unreachable, assume passed (this is a terrible default)
                FileLogger.Warn("Underwriting", "ClearToClose: Could not check compliance for " + loanNumber + ", assuming passed");
                return true;
            }
        }

        private bool CheckDocumentsReceived(string loanNumber)
        {
            try
            {
                string sql = "SELECT COUNT(*) FROM loan_documents WHERE loan_number = @loanNumber AND is_required = true AND status NOT IN ('RECEIVED', 'REVIEWED', 'APPROVED', 'WAIVED')";
                object count = _db.ExecuteCoreScalar(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
                return count == null || Convert.ToInt32(count) == 0;
            }
            catch
            {
                // If documents table doesn't exist yet, assume passed
                return true;
            }
        }

        #endregion
    }
}
