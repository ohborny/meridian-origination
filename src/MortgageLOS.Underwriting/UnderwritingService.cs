using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;
using MortgageLOS.Credit;

namespace MortgageLOS.Underwriting
{
    // =========================================================================
    // UnderwritingService - Manual underwriting decisions and conditions
    //
    // Added in 2010 when the underwriting module was created. Originally
    // tried to be cleaner than the origination module but devolved into
    // complexity over time.
    //
    // TODO: Add condition grouping and bulk satisfaction - 2017
    // =========================================================================

    #region Models

    public class EligibilityResult
    {
        public string LoanNumber { get; set; }
        public bool IsEligible { get; set; }
        public List<EligibilityIssue> Issues { get; set; } = new List<EligibilityIssue>();
    }

    public class EligibilityIssue
    {
        public string Check { get; set; }
        public string Value { get; set; }
        public string Threshold { get; set; }
        public bool Passed { get; set; }
        public string Message { get; set; }
    }

    #endregion

    public class UnderwritingService
    {
        private readonly DatabaseHelper _db;

        public UnderwritingService()
        {
            _db = new DatabaseHelper();
        }

        public UnderwritingService(DatabaseHelper db)
        {
            _db = db;
        }

        /// <summary>
        /// Submits a loan for underwriting.
        /// </summary>
        public bool SubmitForUnderwriting(string loanNumber, string underwriterId)
        {
            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                return false;

            int? uwId = null;
            int parsed;
            if (int.TryParse(underwriterId, out parsed))
                uwId = parsed;

            string sql = @"UPDATE loans SET loan_status = @status, underwriting_dt = @now, underwriter_id = @uwId,
                updated_dt = @now, updated_by = @changedBy WHERE loan_id = @loanId";
            int rows = _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", LoanStatus.UNDERWRITING, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@uwId", (object)uwId ?? DBNull.Value, DbType.Int32),
                _db.CreateParam("@changedBy", underwriterId, DbType.String),
                _db.CreateParam("@loanId", loan.LoanId, DbType.Int32));

            LogStatusHistory(loan.LoanId, loan.LoanStatus, LoanStatus.UNDERWRITING, underwriterId, "Submitted for underwriting");
            FileLogger.Info("Underwriting", "Loan " + loanNumber + " submitted for underwriting by " + underwriterId);
            return rows > 0;
        }

        /// <summary>
        /// Records an underwriting decision.
        /// </summary>
        public bool MakeDecision(string loanNumber, string decision, string underwriterId, string notes)
        {
            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                return false;

            string newStatus;
            if (decision == UnderwritingDecision.APPROVED || decision == UnderwritingDecision.APPROVED_WITH_CONDITIONS)
                newStatus = LoanStatus.CONDITIONAL_APPROVAL;
            else if (decision == UnderwritingDecision.DENIED)
                newStatus = LoanStatus.DENIED;
            else if (decision == UnderwritingDecision.SUSPENDED)
                newStatus = LoanStatus.SUSPENDED;
            else if (decision == UnderwritingDecision.COUNTER_OFFER)
                newStatus = LoanStatus.UNDERWRITING; // Stay in underwriting with substatus
            else
                throw new ArgumentException("Unknown decision: " + decision);

            string dateColumn = newStatus == LoanStatus.CONDITIONAL_APPROVAL ? ", approval_dt = @now" : "";
            string sql = @"UPDATE loans SET loan_status = @status" + dateColumn + @",
                updated_dt = @now, updated_by = @changedBy WHERE loan_id = @loanId";

            _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", newStatus, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@changedBy", underwriterId, DbType.String),
                _db.CreateParam("@loanId", loan.LoanId, DbType.Int32));

            LogStatusHistory(loan.LoanId, loan.LoanStatus, newStatus, underwriterId, decision + ": " + (notes ?? ""));

            // Save decision record
            _db.ExecuteCoreNonQuery(@"CREATE TABLE IF NOT EXISTS underwriting_decisions (
                decision_id SERIAL PRIMARY KEY, loan_id INT, loan_number VARCHAR(20),
                decision VARCHAR(30), underwriter_id VARCHAR(50), notes TEXT, decision_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP)");

            _db.ExecuteCoreNonQuery(@"INSERT INTO underwriting_decisions (loan_id, loan_number, decision, underwriter_id, notes, decision_dt)
                VALUES (@loanId, @loanNumber, @decision, @uwId, @notes, @now)",
                _db.CreateParam("@loanId", loan.LoanId, DbType.Int32),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String),
                _db.CreateParam("@decision", decision, DbType.String),
                _db.CreateParam("@uwId", underwriterId, DbType.String),
                _db.CreateParam("@notes", (object)notes ?? DBNull.Value, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime));

            FileLogger.Info("Underwriting", "Decision for " + loanNumber + ": " + decision + " by " + underwriterId);
            return true;
        }

        /// <summary>
        /// Adds an underwriting condition.
        /// </summary>
        public int AddCondition(int loanId, string conditionType, string category, string desc, string addedBy)
        {
            string sql = @"INSERT INTO loan_conditions (loan_id, condition_type, condition_category, condition_desc, is_satisfied, added_by, added_dt)
                VALUES (@loanId, @type, @category, @desc, false, @addedBy, @now) RETURNING condition_id";
            object result = _db.ExecuteCoreScalar(sql,
                _db.CreateParam("@loanId", loanId, DbType.Int32),
                _db.CreateParam("@type", conditionType, DbType.String),
                _db.CreateParam("@category", category, DbType.String),
                _db.CreateParam("@desc", desc, DbType.String),
                _db.CreateParam("@addedBy", addedBy, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime));
            return result != null ? Convert.ToInt32(result) : 0;
        }

        public List<LoanCondition> GetConditions(string loanNumber)
        {
            List<LoanCondition> conditions = new List<LoanCondition>();
            try
            {
                string sql = @"SELECT c.* FROM loan_conditions c
                    INNER JOIN loans l ON c.loan_id = l.loan_id
                    WHERE l.loan_number = @loanNumber ORDER BY c.added_dt";
                DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
                foreach (DataRow row in dt.Rows)
                    conditions.Add(MapConditionRow(row));
            }
            catch (Exception ex)
            {
                FileLogger.Error("Underwriting", "GetConditions failed for " + loanNumber, ex);
            }
            return conditions;
        }

        public List<LoanCondition> GetOutstandingConditions(string loanNumber)
        {
            List<LoanCondition> conditions = new List<LoanCondition>();
            try
            {
                // BUG: This query doesn't exclude waived conditions (waiver_approved_by IS NOT NULL).
                // A waived condition that was never satisfied still shows as "outstanding", which
                // is misleading in the UI. This has been an issue since 2013. See LOS-1893.
                // Nobody has fixed it because the workaround is to manually satisfy waived
                // conditions before CTC. - D. Patel, 2017
                string sql = @"SELECT c.* FROM loan_conditions c
                    INNER JOIN loans l ON c.loan_id = l.loan_id
                    WHERE l.loan_number = @loanNumber AND c.is_satisfied = false ORDER BY c.added_dt";
                DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
                foreach (DataRow row in dt.Rows)
                    conditions.Add(MapConditionRow(row));
            }
            catch (Exception ex)
            {
                FileLogger.Error("Underwriting", "GetOutstandingConditions failed for " + loanNumber, ex);
            }
            return conditions;
        }

        public bool SatisfyCondition(int conditionId, string satisfiedBy)
        {
            string sql = @"UPDATE loan_conditions SET is_satisfied = true, satisfied_by = @by, satisfied_dt = @now
                WHERE condition_id = @id";
            int rows = _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@by", satisfiedBy, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", conditionId, DbType.Int32));
            return rows > 0;
        }

        public bool WaiveCondition(int conditionId, string waivedBy, string reason)
        {
            string sql = @"UPDATE loan_conditions SET waiver_approved_by = @by, waiver_approved_dt = @now,
                is_satisfied = true, satisfied_by = @by, satisfied_dt = @now
                WHERE condition_id = @id AND is_waivable = true";
            int rows = _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@by", waivedBy, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", conditionId, DbType.Int32));
            return rows > 0;
        }

        public bool CheckAllConditionsSatisfied(string loanNumber)
        {
            // This correctly excludes waived conditions (unlike GetOutstandingConditions)
            string sql = @"SELECT COUNT(*) FROM loan_conditions c
                INNER JOIN loans l ON c.loan_id = l.loan_id
                WHERE l.loan_number = @loanNumber AND c.is_satisfied = false
                AND c.waiver_approved_by IS NULL";
            object count = _db.ExecuteCoreScalar(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
            return count != null && Convert.ToInt32(count) == 0;
        }

        /// <summary>
        /// Checks loan eligibility against product guidelines.
        /// </summary>
        public EligibilityResult CheckEligibility(string loanNumber)
        {
            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                throw new Exception("Loan not found: " + loanNumber);

            LoanProduct product = GetProduct(loan.ProductId ?? 0);
            EligibilityResult result = new EligibilityResult();
            result.LoanNumber = loanNumber;

            // Check FICO
            int creditScore = loan.BorrowerCreditScore ?? 0;
            int minFico = product?.MinFico ?? 620;
            bool ficoPassed = creditScore >= minFico;
            result.Issues.Add(new EligibilityIssue
            {
                Check = "FICO", Value = creditScore.ToString(), Threshold = minFico.ToString(),
                Passed = ficoPassed, Message = ficoPassed ? "Credit score meets minimum" : "Credit score below minimum"
            });

            // Check LTV
            decimal ltv = loan.Ltv ?? loan.CalculatedLtv;
            decimal maxLtv = product?.MaxLtv ?? 97.0m;
            bool ltvPassed = ltv <= maxLtv;
            result.Issues.Add(new EligibilityIssue
            {
                Check = "LTV", Value = ltv.ToString("F2"), Threshold = maxLtv.ToString("F2"),
                Passed = ltvPassed, Message = ltvPassed ? "LTV within guidelines" : "LTV exceeds maximum"
            });

            // Check DTI
            decimal dti = loan.Dti ?? 0m;
            decimal maxDti = product?.MaxDti ?? 50.0m;
            bool dtiPassed = dti <= maxDti;
            result.Issues.Add(new EligibilityIssue
            {
                Check = "DTI", Value = dti.ToString("F2"), Threshold = maxDti.ToString("F2"),
                Passed = dtiPassed, Message = dtiPassed ? "DTI within guidelines" : "DTI exceeds maximum"
            });

            // Check loan amount
            decimal? minAmt = product?.MinLoanAmt;
            decimal? maxAmt = product?.MaxLoanAmt;
            bool amtPassed = true;
            string amtMsg = "Loan amount within product range";
            if (minAmt.HasValue && loan.LoanAmount < minAmt.Value)
            {
                amtPassed = false;
                amtMsg = "Loan amount below product minimum";
            }
            if (maxAmt.HasValue && loan.LoanAmount > maxAmt.Value)
            {
                amtPassed = false;
                amtMsg = "Loan amount exceeds product maximum";
            }
            result.Issues.Add(new EligibilityIssue
            {
                Check = "LOAN_AMOUNT", Value = loan.LoanAmount.ToString("N2"),
                Threshold = (minAmt ?? 0).ToString("N2") + " - " + (maxAmt ?? 0).ToString("N2"),
                Passed = amtPassed, Message = amtMsg
            });

            result.IsEligible = result.Issues.TrueForAll(i => i.Passed);
            return result;
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
                PropertyValue = row["property_value"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["property_value"]),
                Ltv = row["ltv"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["ltv"]),
                Dti = row["dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["dti"]),
                LoanStatus = row["loan_status"]?.ToString()
            };
        }

        private LoanProduct GetProduct(int productId)
        {
            if (productId <= 0)
                return null;
            string sql = "SELECT * FROM loan_products WHERE product_id = @id";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@id", productId, DbType.Int32));
            if (dt.Rows.Count == 0)
                return null;

            DataRow row = dt.Rows[0];
            return new LoanProduct
            {
                ProductId = Convert.ToInt32(row["product_id"]),
                ProductType = row["product_type"]?.ToString(),
                MinFico = row["min_fico"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["min_fico"]),
                MaxLtv = row["max_ltv"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_ltv"]),
                MaxDti = row["max_dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_dti"]),
                MinLoanAmt = row["min_loan_amt"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["min_loan_amt"]),
                MaxLoanAmt = row["max_loan_amt"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_loan_amt"])
            };
        }

        private LoanCondition MapConditionRow(DataRow row)
        {
            return new LoanCondition
            {
                ConditionId = Convert.ToInt32(row["condition_id"]),
                LoanId = Convert.ToInt32(row["loan_id"]),
                ConditionType = row["condition_type"]?.ToString(),
                ConditionCategory = row["condition_category"]?.ToString(),
                ConditionDesc = row["condition_desc"]?.ToString(),
                IsSatisfied = row["is_satisfied"] == DBNull.Value ? false : Convert.ToBoolean(row["is_satisfied"]),
                SatisfiedBy = row["satisfied_by"] == DBNull.Value ? null : row["satisfied_by"].ToString(),
                SatisfiedDt = row["satisfied_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["satisfied_dt"]),
                AddedBy = row["added_by"]?.ToString(),
                AddedDt = Convert.ToDateTime(row["added_dt"]),
                ConditionGroup = row.Table.Columns.Contains("condition_group") && row["condition_group"] != DBNull.Value ? row["condition_group"].ToString() : null,
                IsWaivable = row.Table.Columns.Contains("is_waivable") && row["is_waivable"] != DBNull.Value && Convert.ToBoolean(row["is_waivable"]),
                WaiverApprovedBy = row.Table.Columns.Contains("waiver_approved_by") && row["waiver_approved_by"] != DBNull.Value ? row["waiver_approved_by"].ToString() : null
            };
        }

        private void LogStatusHistory(int loanId, string fromStatus, string toStatus, string changedBy, string reason)
        {
            string sql = @"INSERT INTO loan_status_history (loan_id, from_status, to_status, changed_by, changed_dt, change_reason)
                VALUES (@loanId, @from, @to, @by, @now, @reason)";
            _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@loanId", loanId, DbType.Int32),
                _db.CreateParam("@from", (object)fromStatus ?? DBNull.Value, DbType.String),
                _db.CreateParam("@to", toStatus, DbType.String),
                _db.CreateParam("@by", changedBy, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@reason", (object)reason ?? DBNull.Value, DbType.String));
        }

        #endregion
    }
}
