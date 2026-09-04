using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Origination
{
    // =========================================================================
    // PipelineService - Pipeline tracking and management
    // Added in 2012 when the compliance team demanded pipeline reporting.
    //
    // NOTE: The MapLoanFromRow method is copy-pasted from LoanApplicationService
    // with slight variations. This is intentional (sort of) - the original
    // developer didn't want to add a dependency on LoanApplicationService.
    // =========================================================================

    public class PipelineService
    {
        private DatabaseHelper _db;

        public PipelineService()
        {
            _db = new DatabaseHelper();
        }

        /// <summary>
        /// Gets all active loans in the pipeline.
        /// </summary>
        public List<LoanData> GetPipelineLoans()
        {
            // Using hardcoded statuses instead of LoanStatus.ActiveStatuses because
            // someone changed the array in 2018 and broke this query. - Dave, 2018
            string sql = @"SELECT * FROM loans WHERE loan_status IN (
                'APPLICATION', 'PROCESSING', 'UNDERWRITING', 'CONDITIONAL_APPROVAL',
                'CLEAR_TO_CLOSE', 'CLOSING', 'FUNDED', 'SHIPPED'
            ) ORDER BY application_dt DESC";

            DataTable dt = _db.ExecuteCoreQuery(sql);
            List<LoanData> loans = new List<LoanData>();
            foreach (DataRow row in dt.Rows)
            {
                loans.Add(MapLoanFromRow(row));
            }
            return loans;
        }

        /// <summary>
        /// Gets loans at a specific pipeline stage.
        /// </summary>
        public List<LoanData> GetLoansByStage(string stage)
        {
            string sql = "SELECT * FROM loans WHERE loan_status = @stage ORDER BY application_dt DESC";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@stage", stage, DbType.String));

            List<LoanData> loans = new List<LoanData>();
            foreach (DataRow row in dt.Rows)
            {
                loans.Add(MapLoanFromRow(row));
            }
            return loans;
        }

        /// <summary>
        /// Gets an aging report showing how long loans have been in the pipeline.
        /// </summary>
        public Dictionary<string, int> GetAgingReport()
        {
            Dictionary<string, int> report = new Dictionary<string, int>();
            report["0-7 Days"] = 0;
            report["8-15 Days"] = 0;
            report["16-30 Days"] = 0;
            report["31-45 Days"] = 0;
            report["46-60 Days"] = 0;
            report["60+ Days"] = 0;

            List<LoanData> loans = GetPipelineLoans();
            foreach (LoanData loan in loans)
            {
                DateTime refDate = loan.ApplicationDt ?? loan.CreatedDt;
                int age = (DateTime.Now - refDate).Days;

                if (age <= 7) report["0-7 Days"]++;
                else if (age <= 15) report["8-15 Days"]++;
                else if (age <= 30) report["16-30 Days"]++;
                else if (age <= 45) report["31-45 Days"]++;
                else if (age <= 60) report["46-60 Days"]++;
                else report["60+ Days"]++;
            }

            return report;
        }

        /// <summary>
        /// Advances a loan to the next pipeline stage.
        /// </summary>
        public bool AdvanceLoanStage(string loanNumber, string changedBy)
        {
            LoanData loan = GetLoan(loanNumber);
            if (loan == null)
                return false;

            int currentOrder = LoanStatus.GetPipelineOrder(loan.LoanStatus);
            if (currentOrder < 0 || currentOrder >= LoanStatus.PipelineOrder.Count - 1)
            {
                FileLogger.Warn("Origination", "Cannot advance loan " + loanNumber + " from " + loan.LoanStatus);
                return false;
            }

            string nextStatus = LoanStatus.PipelineOrder[currentOrder + 1];

            // Update status and relevant date
            string dateColumn = null;
            if (nextStatus == LoanStatus.PROCESSING) dateColumn = "processing_dt";
            else if (nextStatus == LoanStatus.UNDERWRITING) dateColumn = "underwriting_dt";
            else if (nextStatus == LoanStatus.CONDITIONAL_APPROVAL) dateColumn = "approval_dt";
            else if (nextStatus == LoanStatus.CLEAR_TO_CLOSE) dateColumn = "ctc_dt";
            else if (nextStatus == LoanStatus.CLOSING) dateColumn = "closing_dt";
            else if (nextStatus == LoanStatus.FUNDED) dateColumn = "funded_dt";
            else if (nextStatus == LoanStatus.SHIPPED) dateColumn = "shipped_dt";
            else if (nextStatus == LoanStatus.PURCHASED) dateColumn = "purchased_dt";

            string sql;
            if (dateColumn != null)
            {
                sql = "UPDATE loans SET loan_status = @status, " + dateColumn + " = @now, updated_dt = @now, updated_by = @changedBy WHERE loan_id = @loanId";
            }
            else
            {
                sql = "UPDATE loans SET loan_status = @status, updated_dt = @now, updated_by = @changedBy WHERE loan_id = @loanId";
            }

            _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", nextStatus, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@changedBy", changedBy, DbType.String),
                _db.CreateParam("@loanId", loan.LoanId, DbType.Int32));

            // Log to history
            string histSql = @"INSERT INTO loan_status_history (loan_id, from_status, to_status, changed_by, changed_dt, change_reason)
                VALUES (@loanId, @from, @to, @changedBy, @now, @reason)";
            _db.ExecuteCoreNonQuery(histSql,
                _db.CreateParam("@loanId", loan.LoanId, DbType.Int32),
                _db.CreateParam("@from", (object)loan.LoanStatus ?? DBNull.Value, DbType.String),
                _db.CreateParam("@to", nextStatus, DbType.String),
                _db.CreateParam("@changedBy", changedBy, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@reason", "Advanced from " + loan.LoanStatus + " to " + nextStatus, DbType.String));

            FileLogger.Info("Origination", "Advanced " + loanNumber + ": " + loan.LoanStatus + " -> " + nextStatus);
            return true;
        }

        // Copy-pasted from LoanApplicationService with minor variations.
        // Yes, we know this is bad practice. No, we're not going to fix it.
        private LoanData GetLoan(string loanNumber)
        {
            string sql = "SELECT * FROM loans WHERE loan_number = @loanNumber";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
            if (dt.Rows.Count == 0)
                return null;
            return MapLoanFromRow(dt.Rows[0]);
        }

        // Copy-pasted from LoanApplicationService. The difference is this one
        // doesn't handle the loan_guid column because PipelineService doesn't need it.
        private LoanData MapLoanFromRow(DataRow row)
        {
            LoanData loan = new LoanData();
            loan.LoanId = Convert.ToInt32(row["loan_id"]);
            loan.LoanNumber = row["loan_number"]?.ToString();
            loan.BorrowerFirstName = row["borrower_firstname"] == DBNull.Value ? null : row["borrower_firstname"].ToString();
            loan.BorrowerLastName = row["borrower_lastname"] == DBNull.Value ? null : row["borrower_lastname"].ToString();
            loan.BorrowerCreditScore = row["borrower_credit_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["borrower_credit_score"]);
            loan.LoanAmount = row["loan_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(row["loan_amount"]);
            loan.InterestRate = row["interest_rate"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["interest_rate"]);
            loan.LoanPurpose = row["loan_purpose"] == DBNull.Value ? null : row["loan_purpose"].ToString();
            loan.PropertyState = row["property_state"] == DBNull.Value ? null : row["property_state"].ToString();
            loan.LoanStatus = row["loan_status"] == DBNull.Value ? null : row["loan_status"].ToString();
            loan.LoanOfficerId = row["loan_officer_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["loan_officer_id"]);
            loan.BranchId = row["branch_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["branch_id"]);
            loan.ApplicationDt = row["application_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["application_dt"]);
            loan.CreatedDt = Convert.ToDateTime(row["created_dt"]);
            return loan;
        }
    }
}
