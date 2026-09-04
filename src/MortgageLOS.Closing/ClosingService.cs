using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;
using MortgageLOS.Underwriting;
using MortgageLOS.Compliance;
using MortgageLOS.Documents;

namespace MortgageLOS.Closing
{
    // =========================================================================
    // ClosingService - Handles loan closing and funding
    //
    // Added in 2016 when the closing module was created. Handles the final
    // stages of the loan pipeline: preparing closing documents, scheduling
    // closing, recording closing results, funding, and investor shipment.
    //
    // TODO: Add support for e-closing (requested 2020, never implemented)
    // TODO: Add title company integration (requested 2018, never implemented)
    // =========================================================================

    public class ClosingData
    {
        public int ClosingId { get; set; }
        public string LoanNumber { get; set; }
        public DateTime? ClosingDate { get; set; }
        public string ClosingLocation { get; set; }
        public string TitleCompany { get; set; }
        public string CloserName { get; set; }
        public decimal? ClosingCost { get; set; }
        public decimal? CashToClose { get; set; }
        public decimal? LoanAmount { get; set; }
        public decimal? InterestRate { get; set; }
        public DateTime? FundedDate { get; set; }
        public DateTime? ShippedDate { get; set; }
        public DateTime? PurchasedDate { get; set; }
        public string InvestorCode { get; set; }
        public string ClosingStatus { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedDt { get; set; }
    }

    public class ClosingService
    {
        private readonly DatabaseHelper _db;
        private readonly UnderwritingService _uwService;

        public ClosingService()
        {
            _db = new DatabaseHelper();
            _uwService = new UnderwritingService();
        }

        /// <summary>
        /// Prepares a loan for closing. Verifies CTC status and creates closing record.
        /// </summary>
        public ClosingData PrepareClosing(string loanNumber, string preparedBy)
        {
            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                throw new Exception("Loan not found: " + loanNumber);

            if (loan.LoanStatus != LoanStatus.CLEAR_TO_CLOSE)
                throw new Exception("Loan must be CLEAR_TO_CLOSE. Current: " + loan.LoanStatus);

            EnsureClosingTableExists();

            // Check if closing record already exists
            ClosingData existing = GetClosingData(loanNumber);
            if (existing != null)
                return existing;

            string sql = @"INSERT INTO loan_closing (loan_number, closing_status, created_dt, created_by)
                VALUES (@loanNumber, 'PREPARED', @now, @preparedBy) RETURNING closing_id";

            object result = _db.ExecuteCoreScalar(sql,
                _db.CreateParam("@loanNumber", loanNumber, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@preparedBy", preparedBy, DbType.String));

            int closingId = result != null ? Convert.ToInt32(result) : 0;

            FileLogger.Info("Closing", "Closing prepared for " + loanNumber);
            return new ClosingData
            {
                ClosingId = closingId,
                LoanNumber = loanNumber,
                ClosingStatus = "PREPARED",
                CreatedDt = DateTime.Now
            };
        }

        /// <summary>
        /// Schedules the closing date.
        /// </summary>
        public bool ScheduleClosing(string loanNumber, DateTime closingDate, string closingLocation, string titleCompany, string scheduledBy)
        {
            string sql = @"UPDATE loan_closing SET closing_date = @closingDate, closing_location = @location,
                title_company = @titleCompany, closing_status = 'SCHEDULED',
                updated_dt = @now, updated_by = @scheduledBy
                WHERE loan_number = @loanNumber";

            int rows = _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@closingDate", closingDate, DbType.Date),
                _db.CreateParam("@location", (object)closingLocation ?? DBNull.Value, DbType.String),
                _db.CreateParam("@titleCompany", (object)titleCompany ?? DBNull.Value, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@scheduledBy", scheduledBy, DbType.String),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            FileLogger.Info("Closing", "Closing scheduled for " + loanNumber + " on " + closingDate.ToString("MM/dd/yyyy"));
            return rows > 0;
        }

        /// <summary>
        /// Records the closing results and funds the loan.
        /// </summary>
        public bool RecordClosing(string loanNumber, decimal closingCost, decimal cashToClose, decimal finalLoanAmount, decimal interestRate, string closedBy)
        {
            // Update closing record
            string sql = @"UPDATE loan_closing SET closing_status = 'CLOSED', closing_cost = @cost,
                cash_to_close = @cash, loan_amount = @loanAmt, interest_rate = @rate,
                closer_name = @closer, updated_dt = @now, updated_by = @closedBy
                WHERE loan_number = @loanNumber";

            _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@cost", closingCost, DbType.Decimal),
                _db.CreateParam("@cash", cashToClose, DbType.Decimal),
                _db.CreateParam("@loanAmt", finalLoanAmount, DbType.Decimal),
                _db.CreateParam("@rate", interestRate, DbType.Decimal),
                _db.CreateParam("@closer", closedBy, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@closedBy", closedBy, DbType.String),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            // Update loan status to CLOSING and set closing date
            string loanSql = @"UPDATE loans SET loan_status = @status, closing_dt = @now,
                loan_amount = @loanAmt, interest_rate = @rate,
                updated_dt = @now, updated_by = @closedBy WHERE loan_number = @loanNumber";

            _db.ExecuteCoreNonQuery(loanSql,
                _db.CreateParam("@status", LoanStatus.CLOSING, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@loanAmt", finalLoanAmount, DbType.Decimal),
                _db.CreateParam("@rate", interestRate, DbType.Decimal),
                _db.CreateParam("@closedBy", closedBy, DbType.String),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            FileLogger.Info("Closing", "Closing recorded for " + loanNumber);
            return true;
        }

        /// <summary>
        /// Marks a loan as funded.
        /// </summary>
        public bool FundLoan(string loanNumber, string fundedBy)
        {
            string sql = @"UPDATE loans SET loan_status = @status, funded_dt = @now,
                updated_dt = @now, updated_by = @fundedBy WHERE loan_number = @loanNumber";

            int rows = _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", LoanStatus.FUNDED, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@fundedBy", fundedBy, DbType.String),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            // Update closing record
            _db.ExecuteCoreNonQuery(@"UPDATE loan_closing SET funded_date = @now, closing_status = 'FUNDED',
                updated_dt = @now, updated_by = @fundedBy WHERE loan_number = @loanNumber",
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@fundedBy", fundedBy, DbType.String),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            FileLogger.Info("Closing", "Loan " + loanNumber + " funded by " + fundedBy);
            return rows > 0;
        }

        /// <summary>
        /// Ships a funded loan to the investor.
        /// </summary>
        public bool ShipToInvestor(string loanNumber, string shippedBy)
        {
            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                return false;

            if (loan.LoanStatus != LoanStatus.FUNDED)
            {
                FileLogger.Warn("Closing", "ShipToInvestor: Loan " + loanNumber + " is not FUNDED. Status: " + loan.LoanStatus);
                return false;
            }

            // Get investor code from product
            string investorCode = AppConfig.Instance.GetSetting("DefaultInvestorCode", "FNM");
            if (loan.ProductId.HasValue)
            {
                object investor = _db.ExecuteCoreScalar("SELECT investor_code FROM loan_products WHERE product_id = @id",
                    _db.CreateParam("@id", loan.ProductId.Value, DbType.Int32));
                if (investor != null && investor != DBNull.Value)
                    investorCode = investor.ToString();
            }

            string sql = @"UPDATE loans SET loan_status = @status, shipped_dt = @now,
                updated_dt = @now, updated_by = @shippedBy WHERE loan_number = @loanNumber";

            _db.ExecuteCoreNonQuery(sql,
                _db.CreateParam("@status", LoanStatus.SHIPPED, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@shippedBy", shippedBy, DbType.String),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            // Update closing record
            _db.ExecuteCoreNonQuery(@"UPDATE loan_closing SET shipped_date = @now, investor_code = @investor,
                closing_status = 'SHIPPED', updated_dt = @now, updated_by = @shippedBy WHERE loan_number = @loanNumber",
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@investor", investorCode, DbType.String),
                _db.CreateParam("@shippedBy", shippedBy, DbType.String),
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            FileLogger.Info("Closing", "Loan " + loanNumber + " shipped to investor " + investorCode);
            return true;
        }

        /// <summary>
        /// Gets closing data for a loan.
        /// </summary>
        public ClosingData GetClosingData(string loanNumber)
        {
            try
            {
                string sql = "SELECT * FROM loan_closing WHERE loan_number = @loanNumber ORDER BY created_dt DESC LIMIT 1";
                DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
                if (dt.Rows.Count == 0)
                    return null;

                DataRow row = dt.Rows[0];
                return new ClosingData
                {
                    ClosingId = Convert.ToInt32(row["closing_id"]),
                    LoanNumber = row["loan_number"]?.ToString(),
                    ClosingDate = row["closing_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["closing_date"]),
                    ClosingLocation = row["closing_location"] == DBNull.Value ? null : row["closing_location"].ToString(),
                    TitleCompany = row["title_company"] == DBNull.Value ? null : row["title_company"].ToString(),
                    CloserName = row["closer_name"] == DBNull.Value ? null : row["closer_name"].ToString(),
                    ClosingCost = row["closing_cost"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["closing_cost"]),
                    CashToClose = row["cash_to_close"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["cash_to_close"]),
                    LoanAmount = row["loan_amount"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["loan_amount"]),
                    InterestRate = row["interest_rate"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["interest_rate"]),
                    FundedDate = row["funded_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["funded_date"]),
                    ShippedDate = row["shipped_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["shipped_date"]),
                    InvestorCode = row["investor_code"] == DBNull.Value ? null : row["investor_code"].ToString(),
                    ClosingStatus = row["closing_status"]?.ToString(),
                    Notes = row["notes"] == DBNull.Value ? null : row["notes"].ToString(),
                    CreatedDt = Convert.ToDateTime(row["created_dt"])
                };
            }
            catch (Exception ex)
            {
                FileLogger.Error("Closing", "GetClosingData failed for " + loanNumber, ex);
                return null;
            }
        }

        #region Private Helpers

        private void EnsureClosingTableExists()
        {
            _db.ExecuteCoreNonQuery(@"CREATE TABLE IF NOT EXISTS loan_closing (
                closing_id SERIAL PRIMARY KEY,
                loan_number VARCHAR(20) NOT NULL,
                closing_date DATE,
                closing_location VARCHAR(200),
                title_company VARCHAR(100),
                closer_name VARCHAR(100),
                closing_cost NUMERIC(12,2),
                cash_to_close NUMERIC(12,2),
                loan_amount NUMERIC(12,2),
                interest_rate NUMERIC(6,3),
                funded_date DATE,
                shipped_date DATE,
                purchased_date DATE,
                investor_code VARCHAR(20),
                closing_status VARCHAR(20) DEFAULT 'PREPARED',
                notes TEXT,
                created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                created_by VARCHAR(50),
                updated_by VARCHAR(50))");
        }

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
                LoanAmount = row["loan_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(row["loan_amount"]),
                InterestRate = row["interest_rate"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["interest_rate"]),
                LoanStatus = row["loan_status"]?.ToString(),
                ProductId = row["product_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["product_id"])
            };
        }

        #endregion
    }
}
