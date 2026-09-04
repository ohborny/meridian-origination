using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;
using MortgageLOS.Compliance;

namespace MortgageLOS.Reporting
{
    // =========================================================================
    // ReportingService - Generates regulatory and investor reports
    //
    // Added in 2018 when HMDA reporting requirements expanded. This module
    // generates HMDA LAR files, pipeline reports, investor reports, and
    // compliance summaries.
    //
    // NOTE: The HMDA LAR generation delegates to HmdaService.GenerateHmdaLar
    // which has a known bug with co-applicant race data. See comment in
    // HmdaService.cs.
    // =========================================================================

    public class PipelineReportItem
    {
        public string LoanNumber { get; set; }
        public string BorrowerName { get; set; }
        public string Status { get; set; }
        public string LoanOfficer { get; set; }
        public string Branch { get; set; }
        public decimal LoanAmount { get; set; }
        public string PropertyState { get; set; }
        public DateTime? ApplicationDate { get; set; }
        public int DaysInPipeline { get; set; }
    }

    public class ComplianceSummaryItem
    {
        public string CheckType { get; set; }
        public int TotalChecks { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Warnings { get; set; }
        public int ManualReview { get; set; }
    }

    public class ReportingService
    {
        private readonly DatabaseHelper _db;
        private readonly HmdaService _hmdaService;

        public ReportingService()
        {
            _db = new DatabaseHelper();
            _hmdaService = new HmdaService();
        }

        /// <summary>
        /// Generates HMDA LAR (Loan Application Register) data for a reporting year.
        /// </summary>
        public List<string[]> GenerateHmdaReport(int reportingYear)
        {
            FileLogger.Info("Reporting", "Generating HMDA LAR for year " + reportingYear);
            List<string[]> larData = _hmdaService.GenerateHmdaLar(reportingYear);
            FileLogger.Info("Reporting", "HMDA LAR generated: " + larData.Count + " records");
            return larData;
        }

        /// <summary>
        /// Generates a pipeline report showing all active loans.
        /// </summary>
        public List<PipelineReportItem> GeneratePipelineReport()
        {
            List<PipelineReportItem> report = new List<PipelineReportItem>();

            string sql = @"SELECT l.loan_number, l.borrower_firstname, l.borrower_lastname,
                l.loan_status, l.loan_amount, l.property_state, l.application_dt,
                lo.last_name AS lo_name, b.branch_name,
                EXTRACT(DAY FROM (CURRENT_DATE - COALESCE(l.application_dt, l.created_dt)))::INT AS days_in_pipeline
                FROM loans l
                LEFT JOIN loan_officers lo ON l.loan_officer_id = lo.lo_id
                LEFT JOIN branches b ON l.branch_id = b.branch_id
                WHERE l.loan_status NOT IN ('PURCHASED', 'DENIED', 'WITHDRAWN', 'CANCELLED', 'SUSPENDED')
                ORDER BY l.application_dt DESC";

            DataTable dt = _db.ExecuteCoreQuery(sql);
            foreach (DataRow row in dt.Rows)
            {
                PipelineReportItem item = new PipelineReportItem();
                item.LoanNumber = row["loan_number"]?.ToString();
                item.BorrowerName = (row["borrower_firstname"]?.ToString() ?? "") + " " + (row["borrower_lastname"]?.ToString() ?? "");
                item.Status = row["loan_status"]?.ToString();
                item.LoanOfficer = row["lo_name"] == DBNull.Value ? "Unassigned" : row["lo_name"].ToString();
                item.Branch = row["branch_name"] == DBNull.Value ? "Unknown" : row["branch_name"].ToString();
                item.LoanAmount = row["loan_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(row["loan_amount"]);
                item.PropertyState = row["property_state"] == DBNull.Value ? "" : row["property_state"].ToString();
                item.ApplicationDate = row["application_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["application_dt"]);
                item.DaysInPipeline = row["days_in_pipeline"] == DBNull.Value ? 0 : Convert.ToInt32(row["days_in_pipeline"]);
                report.Add(item);
            }

            FileLogger.Info("Reporting", "Pipeline report generated: " + report.Count + " loans");
            return report;
        }

        /// <summary>
        /// Generates an investor report for funded loans in a date range.
        /// </summary>
        public List<string[]> GenerateInvestorReport(string investorCode, DateTime fromDate, DateTime toDate)
        {
            List<string[]> report = new List<string[]>();

            string[] headers = { "LoanNumber", "InvestorCode", "ProductType", "LoanAmount", "InterestRate",
                "TermMonths", "BorrowerName", "PropertyState", "PropertyType", "Occupancy", "FundedDate" };

            string sql = @"SELECT l.loan_number, l.loan_amount, l.interest_rate, l.term_months,
                l.borrower_firstname, l.borrower_lastname, l.property_state, l.property_type,
                l.property_occupancy, l.funded_dt, p.product_type, p.investor_code
                FROM loans l
                LEFT JOIN loan_products p ON l.product_id = p.product_id
                WHERE l.loan_status IN ('FUNDED', 'SHIPPED', 'PURCHASED')
                AND l.funded_dt >= @fromDate AND l.funded_dt <= @toDate";

            List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
            parameters.Add(_db.CreateParam("@fromDate", fromDate, DbType.Date));
            parameters.Add(_db.CreateParam("@toDate", toDate, DbType.Date));

            if (!string.IsNullOrEmpty(investorCode))
            {
                sql += " AND p.investor_code = @investorCode";
                parameters.Add(_db.CreateParam("@investorCode", investorCode, DbType.String));
            }

            sql += " ORDER BY l.funded_dt";

            DataTable dt = _db.ExecuteCoreQuery(sql, parameters.ToArray());

            report.Add(headers);
            foreach (DataRow row in dt.Rows)
            {
                string loanNumber = row["loan_number"]?.ToString() ?? "";
                string investor = row["investor_code"] == DBNull.Value ? "" : row["investor_code"].ToString();
                string productType = row["product_type"]?.ToString() ?? "";
                string loanAmount = row["loan_amount"] == DBNull.Value ? "0" : row["loan_amount"].ToString();
                string rate = row["interest_rate"] == DBNull.Value ? "" : row["interest_rate"].ToString();
                string term = row["term_months"] == DBNull.Value ? "" : row["term_months"].ToString();
                string borrower = (row["borrower_firstname"]?.ToString() ?? "") + " " + (row["borrower_lastname"]?.ToString() ?? "");
                string state = row["property_state"] == DBNull.Value ? "" : row["property_state"].ToString();
                string propType = row["property_type"] == DBNull.Value ? "" : row["property_type"].ToString();
                string occ = row["property_occupancy"] == DBNull.Value ? "" : row["property_occupancy"].ToString();
                string funded = row["funded_dt"] == DBNull.Value ? "" : Convert.ToDateTime(row["funded_dt"]).ToString("yyyy-MM-dd");

                report.Add(new string[] { loanNumber, investor, productType, loanAmount, rate, term, borrower, state, propType, occ, funded });
            }

            FileLogger.Info("Reporting", "Investor report generated: " + (report.Count - 1) + " loans for " + investorCode);
            return report;
        }

        /// <summary>
        /// Generates a compliance summary for a date range.
        /// </summary>
        public List<ComplianceSummaryItem> GenerateComplianceSummary(DateTime fromDate, DateTime toDate)
        {
            List<ComplianceSummaryItem> summary = new List<ComplianceSummaryItem>();

            string sql = @"SELECT chk_type, chk_status, COUNT(*) as cnt
                FROM compliance_checks
                WHERE chk_dt >= @fromDate AND chk_dt <= @toDate
                GROUP BY chk_type, chk_status
                ORDER BY chk_type";

            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@fromDate", fromDate, DbType.DateTime),
                _db.CreateParam("@toDate", toDate, DbType.DateTime));

            Dictionary<string, ComplianceSummaryItem> byType = new Dictionary<string, ComplianceSummaryItem>();

            foreach (DataRow row in dt.Rows)
            {
                string chkType = row["chk_type"]?.ToString() ?? "UNKNOWN";
                string chkStatus = row["chk_status"]?.ToString() ?? "UNKNOWN";
                int count = Convert.ToInt32(row["cnt"]);

                if (!byType.ContainsKey(chkType))
                {
                    byType[chkType] = new ComplianceSummaryItem { CheckType = chkType };
                }

                ComplianceSummaryItem item = byType[chkType];
                item.TotalChecks += count;

                switch (chkStatus)
                {
                    case ComplianceStatus.PASS:
                        item.Passed += count;
                        break;
                    case ComplianceStatus.FAIL:
                        item.Failed += count;
                        break;
                    case ComplianceStatus.WARNING:
                        item.Warnings += count;
                        break;
                    case ComplianceStatus.MANUAL_REVIEW:
                        item.ManualReview += count;
                        break;
                }
            }

            foreach (var kvp in byType)
            {
                summary.Add(kvp.Value);
            }

            FileLogger.Info("Reporting", "Compliance summary generated: " + summary.Count + " check types");
            return summary;
        }

        /// <summary>
        /// Gets loan metrics for a single loan.
        /// </summary>
        public Dictionary<string, object> GetLoanMetrics(string loanNumber)
        {
            Dictionary<string, object> metrics = new Dictionary<string, object>();

            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                return metrics;

            metrics["loanNumber"] = loanNumber;
            metrics["loanStatus"] = loan.LoanStatus;
            metrics["loanAmount"] = loan.LoanAmount;
            metrics["interestRate"] = loan.InterestRate;
            metrics["ltv"] = loan.Ltv;
            metrics["dti"] = loan.Dti;
            metrics["creditScore"] = loan.BorrowerCreditScore;
            metrics["applicationDate"] = loan.ApplicationDt;

            // Calculate days in pipeline
            if (loan.ApplicationDt.HasValue)
            {
                metrics["daysInPipeline"] = (DateTime.Now - loan.ApplicationDt.Value).Days;
            }

            // Get compliance status
            try
            {
                string complianceSql = "SELECT chk_status, COUNT(*) FROM compliance_checks WHERE loan_number = @loanNumber GROUP BY chk_status";
                DataTable complianceDt = _db.ExecuteComplianceQuery(complianceSql,
                    _db.CreateParam("@loanNumber", loanNumber, DbType.String));

                int passCount = 0, failCount = 0;
                foreach (DataRow row in complianceDt.Rows)
                {
                    string status = row["chk_status"]?.ToString();
                    int count = Convert.ToInt32(row[1]);
                    if (status == ComplianceStatus.PASS) passCount += count;
                    if (status == ComplianceStatus.FAIL) failCount += count;
                }
                metrics["complianceChecksPassed"] = passCount;
                metrics["complianceChecksFailed"] = failCount;
            }
            catch { }

            return metrics;
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
                Ltv = row["ltv"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["ltv"]),
                Dti = row["dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["dti"]),
                BorrowerCreditScore = row["borrower_credit_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["borrower_credit_score"]),
                LoanStatus = row["loan_status"]?.ToString(),
                ApplicationDt = row["application_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["application_dt"]),
                CreatedDt = Convert.ToDateTime(row["created_dt"])
            };
        }
    }
}
