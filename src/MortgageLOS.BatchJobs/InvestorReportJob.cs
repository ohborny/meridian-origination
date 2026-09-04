using System;
using System.Collections.Generic;
using System.Data;
using MortgageLOS;

namespace MortgageLOS.BatchJobs
{
    // =========================================================================
    // InvestorReportJob - Generates investor reports for funded loans
    //
    // Generates CSV files in data/batch/investor-reports/ for loans that have
    // been funded but not yet shipped to the investor.
    // =========================================================================

    public class InvestorReportJob
    {
        private DatabaseHelper _db;
        private FileHandoffHelper _fileHelper;

        public InvestorReportJob()
        {
            _db = new DatabaseHelper();
            _fileHelper = new FileHandoffHelper();
        }

        public int Execute()
        {
            FileLogger.Info("BatchJobs", "InvestorReportJob: Starting investor report generation");

            string sql = @"SELECT l.loan_number, l.loan_amount, l.interest_rate, l.term_months,
                l.borrower_lastname, l.borrower_firstname, l.property_state, l.property_type,
                l.property_occupancy, l.funded_dt, p.product_type, p.investor_code
                FROM loans l
                LEFT JOIN loan_products p ON l.product_id = p.product_id
                WHERE l.loan_status = 'FUNDED' AND l.shipped_dt IS NULL";
            DataTable loans = _db.ExecuteCoreQuery(sql);

            if (loans.Rows.Count == 0)
            {
                FileLogger.Info("BatchJobs", "InvestorReportJob: No funded loans to report. Done.");
                return 0;
            }

            string[] headers = { "LoanNumber", "InvestorCode", "ProductType", "LoanAmount", "InterestRate",
                "TermMonths", "BorrowerName", "PropertyState", "PropertyType", "Occupancy", "FundedDate" };

            List<string[]> dataRows = new List<string[]>();
            foreach (DataRow row in loans.Rows)
            {
                string loanNumber = row["loan_number"]?.ToString() ?? "";
                string investor = row["investor_code"] == DBNull.Value ? "FNM" : row["investor_code"].ToString();
                string productType = row["product_type"]?.ToString() ?? "";
                string loanAmount = row["loan_amount"] == DBNull.Value ? "0" : row["loan_amount"].ToString();
                string rate = row["interest_rate"] == DBNull.Value ? "" : row["interest_rate"].ToString();
                string term = row["term_months"] == DBNull.Value ? "" : row["term_months"].ToString();
                string borrowerName = (row["borrower_firstname"]?.ToString() ?? "") + " " + (row["borrower_lastname"]?.ToString() ?? "");
                string state = row["property_state"] == DBNull.Value ? "" : row["property_state"].ToString();
                string propType = row["property_type"] == DBNull.Value ? "" : row["property_type"].ToString();
                string occupancy = row["property_occupancy"] == DBNull.Value ? "" : row["property_occupancy"].ToString();
                string fundedDt = row["funded_dt"] == DBNull.Value ? "" : Convert.ToDateTime(row["funded_dt"]).ToString("yyyy-MM-dd");

                dataRows.Add(new string[] { loanNumber, investor, productType, loanAmount, rate, term, borrowerName, state, propType, occupancy, fundedDt });
            }

            string fileName = FileHandoffHelper.CreateTimestampedFileName("investor_report", "csv");
            string filePath = _fileHelper.WriteCsvWithHeader("investor-reports", fileName, headers, dataRows);

            FileLogger.Info("BatchJobs", "InvestorReportJob: Generated " + filePath + " with " + dataRows.Count + " loans");
            return 0;
        }
    }
}
