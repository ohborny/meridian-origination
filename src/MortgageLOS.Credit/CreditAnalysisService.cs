using System;
using System.Data;
using System.Collections.Generic;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Credit
{
    // =========================================================================
    // CreditAnalysisService - Credit analysis and summary helpers
    //
    // Provides analysis of credit history, fraud alerts, public records,
    // utilization ratios, and credit summaries. These are used by the
    // underwriting module to make quick decisions without pulling the full
    // credit report.
    //
    // HISTORY:
    //   2010 - Original version by Frank D. (basic analysis)
    //   2013 - Added utilization calculation (J. Martinez)
    //   2015 - Added fraud alert checking
    //   2018 - Added public record waiting period analysis
    //   2021 - Added GetCreditSummary for dashboard
    //
    // TODO: Add trended data analysis (requested 2017, deferred indefinitely)
    // TODO: Add capacity to export analysis as PDF (requested 2020)
    // =========================================================================

    public class CreditAnalysisService
    {
        private readonly DatabaseHelper _db;

        public CreditAnalysisService()
        {
            _db = new DatabaseHelper();
        }

        public CreditAnalysisService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
        }

        #region Credit History Analysis

        /// <summary>
        /// Analyzes the credit history for a loan and returns a summary
        /// dictionary with key metrics.
        /// </summary>
        public Dictionary<string, object> AnalyzeCreditHistory(string loanNumber)
        {
            Dictionary<string, object> analysis = new Dictionary<string, object>();

            if (string.IsNullOrEmpty(loanNumber))
            {
                FileLogger.Warn("Credit", "AnalyzeCreditHistory called with empty loan number");
                return analysis;
            }

            try
            {
                // Get the latest credit report
                CreditReportData report = getLatestReport(loanNumber);
                if (report == null)
                {
                    FileLogger.Warn("Credit", "No credit report found for analysis - loan " + loanNumber);
                    analysis["error"] = "No credit report found";
                    return analysis;
                }

                // Get liabilities for this loan
                List<CreditLiabilityData> liabilities = getLiabilities(loanNumber);

                // Count late payments across all liabilities
                int totalLate30 = 0;
                int totalLate60 = 0;
                int totalLate90 = 0;
                int delinquentCount = 0;
                int openAccountCount = 0;
                int closedAccountCount = 0;
                DateTime? oldestAccountDate = null;
                decimal totalCurrentBalance = 0m;

                foreach (CreditLiabilityData liab in liabilities)
                {
                    totalLate30 += liab.Late30Count;
                    totalLate60 += liab.Late60Count;
                    totalLate90 += liab.Late90Count;

                    if (liab.IsDelinquent)
                    {
                        delinquentCount++;
                    }

                    if (liab.AccountStatus == "OPEN")
                    {
                        openAccountCount++;
                    }
                    else
                    {
                        closedAccountCount++;
                    }

                    if (liab.DateOpened.HasValue)
                    {
                        if (oldestAccountDate == null || liab.DateOpened.Value < oldestAccountDate.Value)
                        {
                            oldestAccountDate = liab.DateOpened.Value;
                        }
                    }

                    if (liab.CurrentBalance.HasValue)
                    {
                        totalCurrentBalance += liab.CurrentBalance.Value;
                    }
                }

                // Calculate oldest account age in years
                int oldestAccountAgeYears = 0;
                if (oldestAccountDate.HasValue)
                {
                    oldestAccountAgeYears = (int)((DateTime.Now - oldestAccountDate.Value).TotalDays / 365.25);
                }

                // Calculate utilization ratio
                decimal utilizationRatio = CalculateUtilizationRatio(loanNumber);

                // Build the analysis dictionary
                analysis["loanNumber"] = loanNumber;
                analysis["reportId"] = report.ReportId;
                analysis["representativeScore"] = report.RepresentativeScore ?? 0;
                analysis["experianScore"] = report.ExperianScore ?? 0;
                analysis["equifaxScore"] = report.EquifaxScore ?? 0;
                analysis["transunionScore"] = report.TransunionScore ?? 0;
                analysis["lowestScore"] = report.LowestScore;
                analysis["highestScore"] = report.HighestScore;
                analysis["totalAccounts"] = liabilities.Count;
                analysis["openAccounts"] = openAccountCount;
                analysis["closedAccounts"] = closedAccountCount;
                analysis["delinquentAccounts"] = delinquentCount;
                analysis["totalLate30"] = totalLate30;
                analysis["totalLate60"] = totalLate60;
                analysis["totalLate90"] = totalLate90;
                analysis["oldestAccountAgeYears"] = oldestAccountAgeYears;
                analysis["totalCurrentBalance"] = totalCurrentBalance;
                analysis["utilizationRatio"] = utilizationRatio;
                analysis["fileStatus"] = report.FileStatus ?? "UNKNOWN";
                analysis["fraudAlert"] = report.FraudAlert;
                analysis["hasPublicRecords"] = CheckForPublicRecords(loanNumber).Count > 0;

                FileLogger.Info("Credit", "Credit history analysis complete for loan " + loanNumber +
                    " - accounts=" + liabilities.Count + " utilization=" + utilizationRatio +
                    "% late30=" + totalLate30 + " late60=" + totalLate60 + " late90=" + totalLate90);

                return analysis;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "AnalyzeCreditHistory failed for loan " + loanNumber, ex);
                analysis["error"] = ex.Message;
                return analysis;
            }
        }

        #endregion

        #region Fraud Alert

        /// <summary>
        /// Checks if the latest credit report for a loan has a fraud alert.
        /// </summary>
        public bool CheckForFraudAlert(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                return false;
            }

            try
            {
                // Legacy string concatenation - this method was written in 2015
                // and the developer (J. Martinez) preferred the old style.
                // The loan number comes from internal calls so it's "safe."
                string sql = "SELECT fraud_alert FROM credit_reports WHERE loan_number = '" +
                    loanNumber.Replace("'", "''") + "' ORDER BY pulled_dt DESC LIMIT 1";

                object result = _db.ExecuteCreditScalar(sql);

                if (result == null || result == DBNull.Value)
                {
                    return false;
                }

                bool hasAlert = Convert.ToBoolean(result);
                if (hasAlert)
                {
                    FileLogger.Warn("Credit", "Fraud alert found on credit report for loan " + loanNumber);
                }
                return hasAlert;
            }
            catch
            {
                // Inconsistent error handling: this method swallows the exception
                // and returns false. Other methods log and return. This was
                // intentional because a fraud alert check failing should not
                // block the loan process - it's better to proceed and let
                // underwriting catch it manually. - J. Martinez, 2015
                return false;
            }
        }

        #endregion

        #region Public Records

        /// <summary>
        /// Checks for public records on the latest credit report for a loan.
        /// Returns the list of public records found (empty if none).
        /// </summary>
        public List<CreditPublicRecordData> CheckForPublicRecords(string loanNumber)
        {
            List<CreditPublicRecordData> records = new List<CreditPublicRecordData>();

            if (string.IsNullOrEmpty(loanNumber))
            {
                return records;
            }

            try
            {
                // First get the latest report ID
                CreditReportData report = getLatestReport(loanNumber);
                if (report == null || report.ReportId <= 0)
                {
                    return records;
                }

                string sql = "SELECT * FROM credit_public_records WHERE report_id = @reportId " +
                             "ORDER BY filed_date";
                DataTable dt = _db.ExecuteCreditQuery(sql,
                    _db.CreateParam("@reportId", report.ReportId, DbType.Int32));

                foreach (DataRow row in dt.Rows)
                {
                    records.Add(mapPublicRecordRow(row));
                }

                if (records.Count > 0)
                {
                    FileLogger.Info("Credit", "Found " + records.Count +
                        " public records for loan " + loanNumber);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "CheckForPublicRecords failed for loan " + loanNumber, ex);
            }

            return records;
        }

        #endregion

        #region Utilization Ratio

        /// <summary>
        /// Calculates the credit utilization ratio for revolving accounts.
        /// Formula: sum of revolving balances / sum of revolving credit limits.
        /// Returns the ratio as a percentage (e.g., 35.5 for 35.5%).
        /// </summary>
        public decimal CalculateUtilizationRatio(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                return 0m;
            }

            try
            {
                // Get revolving liabilities
                List<CreditLiabilityData> liabilities = getLiabilities(loanNumber);

                decimal totalBalance = 0m;
                decimal totalLimit = 0m;

                foreach (CreditLiabilityData liab in liabilities)
                {
                    if (liab.IsRevolving && liab.AccountStatus == "OPEN")
                    {
                        if (liab.CurrentBalance.HasValue)
                        {
                            totalBalance += liab.CurrentBalance.Value;
                        }
                        if (liab.HighCredit.HasValue)
                        {
                            totalLimit += liab.HighCredit.Value;
                        }
                    }
                }

                if (totalLimit == 0m)
                {
                    // No revolving credit limits found, can't calculate
                    return 0m;
                }

                decimal ratio = Math.Round((totalBalance / totalLimit) * 100m, 2);
                return ratio;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "CalculateUtilizationRatio failed for loan " +
                    loanNumber, ex);
                return 0m;
            }
        }

        #endregion

        #region Credit Summary

        /// <summary>
        /// Gets a comprehensive credit summary for a loan.
        /// Returns a dictionary with scores, total debt, utilization, DTI, etc.
        /// Used by the underwriting dashboard.
        /// </summary>
        public Dictionary<string, object> GetCreditSummary(string loanNumber)
        {
            Dictionary<string, object> summary = new Dictionary<string, object>();

            if (string.IsNullOrEmpty(loanNumber))
            {
                FileLogger.Warn("Credit", "GetCreditSummary called with empty loan number");
                return summary;
            }

            try
            {
                // Get the latest credit report
                CreditReportData report = getLatestReport(loanNumber);

                if (report == null)
                {
                    summary["error"] = "No credit report found";
                    summary["loanNumber"] = loanNumber;
                    return summary;
                }

                // Get liabilities
                List<CreditLiabilityData> liabilities = getLiabilities(loanNumber);

                // Calculate totals
                decimal totalDebt = 0m;
                decimal totalMonthlyPayments = 0m;
                int revolvingCount = 0;
                int installmentCount = 0;
                int mortgageCount = 0;
                int autoCount = 0;
                int studentCount = 0;

                foreach (CreditLiabilityData liab in liabilities)
                {
                    if (liab.CurrentBalance.HasValue)
                    {
                        totalDebt += liab.CurrentBalance.Value;
                    }
                    if (liab.MonthlyPayment.HasValue && liab.IsIncludedInDti)
                    {
                        totalMonthlyPayments += liab.MonthlyPayment.Value;
                    }

                    if (liab.AccountType == "REVOLVING") revolvingCount++;
                    else if (liab.AccountType == "INSTALLMENT") installmentCount++;
                    else if (liab.AccountType == "MORTGAGE") mortgageCount++;
                    else if (liab.AccountType == "AUTO") autoCount++;
                    else if (liab.AccountType == "STUDENT") studentCount++;
                }

                // Get utilization ratio
                decimal utilization = CalculateUtilizationRatio(loanNumber);

                // Get public records
                List<CreditPublicRecordData> publicRecords = CheckForPublicRecords(loanNumber);

                // Get fraud alert status
                bool fraudAlert = CheckForFraudAlert(loanNumber);

                // Build summary
                summary["loanNumber"] = loanNumber;
                summary["reportId"] = report.ReportId;
                summary["reportDate"] = report.PulledDt;
                summary["pullType"] = report.PullType;
                summary["vendorName"] = report.VendorName;

                // Scores
                summary["experianScore"] = report.ExperianScore ?? 0;
                summary["equifaxScore"] = report.EquifaxScore ?? 0;
                summary["transunionScore"] = report.TransunionScore ?? 0;
                summary["representativeScore"] = report.RepresentativeScore ?? 0;
                summary["lowestScore"] = report.LowestScore;
                summary["highestScore"] = report.HighestScore;
                summary["hasAllScores"] = report.HasAllScores;

                // File info
                summary["fileStatus"] = report.FileStatus ?? "UNKNOWN";
                summary["fraudAlert"] = fraudAlert;
                summary["activeAlertCount"] = report.ActiveAlertCount;

                // Debt summary
                summary["totalDebt"] = totalDebt;
                summary["totalMonthlyPayments"] = totalMonthlyPayments;
                summary["totalAccounts"] = liabilities.Count;
                summary["revolvingAccounts"] = revolvingCount;
                summary["installmentAccounts"] = installmentCount;
                summary["mortgageAccounts"] = mortgageCount;
                summary["autoAccounts"] = autoCount;
                summary["studentAccounts"] = studentCount;

                // Utilization
                summary["utilizationRatio"] = utilization;

                // Public records
                summary["hasPublicRecords"] = publicRecords.Count > 0;
                summary["publicRecordCount"] = publicRecords.Count;
                if (publicRecords.Count > 0)
                {
                    List<string> recordTypes = new List<string>();
                    foreach (CreditPublicRecordData pr in publicRecords)
                    {
                        recordTypes.Add(pr.RecordType ?? "UNKNOWN");
                    }
                    summary["publicRecordTypes"] = recordTypes;
                }

                FileLogger.Info("Credit", "Credit summary generated for loan " + loanNumber +
                    " - repScore=" + (report.RepresentativeScore ?? 0) +
                    " totalDebt=" + totalDebt + " utilization=" + utilization + "%");

                return summary;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetCreditSummary failed for loan " + loanNumber, ex);
                summary["error"] = ex.Message;
                summary["loanNumber"] = loanNumber;
                return summary;
            }
        }

        #endregion

        #region Private Helpers

        private CreditReportData getLatestReport(string loanNumber)
        {
            string sql = "SELECT * FROM credit_reports WHERE loan_number = @loanNumber " +
                         "ORDER BY pulled_dt DESC LIMIT 1";
            DataTable dt = _db.ExecuteCreditQuery(sql,
                _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            if (dt.Rows.Count == 0)
            {
                return null;
            }

            return mapReportRow(dt.Rows[0]);
        }

        private List<CreditLiabilityData> getLiabilities(string loanNumber)
        {
            List<CreditLiabilityData> result = new List<CreditLiabilityData>();

            // Using string concatenation here because this helper was copy-pasted
            // from the old CreditPullManager in 2010. Nobody updated it.
            // The loan number is always from an internal call.
            string sql = "SELECT * FROM credit_liabilities WHERE loan_number = '" +
                loanNumber.Replace("'", "''") + "' ORDER BY date_opened";

            DataTable dt = _db.ExecuteCreditQuery(sql);

            foreach (DataRow row in dt.Rows)
            {
                result.Add(mapLiabilityRow(row));
            }

            return result;
        }

        private CreditReportData mapReportRow(DataRow row)
        {
            CreditReportData report = new CreditReportData();
            report.ReportId = Convert.ToInt32(row["ReportId"]);
            report.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            report.BorrowerSsnHash = row["borrower_ssn_hash"] == DBNull.Value ? null : row["borrower_ssn_hash"].ToString();
            report.ReportType = row["report_type"] == DBNull.Value ? null : row["report_type"].ToString();
            report.PullType = row["pull_type"] == DBNull.Value ? null : row["pull_type"].ToString();
            report.PulledBy = row["pulled_by"] == DBNull.Value ? null : row["pulled_by"].ToString();
            report.PulledDt = row["pulled_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["pulled_dt"]);
            report.ExperianReportId = row["experian_report_id"] == DBNull.Value ? null : row["experian_report_id"].ToString();
            report.EquifaxReportId = row["equifax_report_id"] == DBNull.Value ? null : row["equifax_report_id"].ToString();
            report.TransunionReportId = row["transunion_report_id"] == DBNull.Value ? null : row["transunion_report_id"].ToString();
            report.ExperianScore = row["experian_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["experian_score"]);
            report.EquifaxScore = row["equifax_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["equifax_score"]);
            report.TransunionScore = row["transunion_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["transunion_score"]);
            report.RepresentativeScore = row["representative_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["representative_score"]);
            report.FileStatus = row["file_status"] == DBNull.Value ? null : row["file_status"].ToString();
            report.FraudAlert = row["fraud_alert"] == DBNull.Value ? false : Convert.ToBoolean(row["fraud_alert"]);
            report.ActiveAlertCount = row["active_alert_count"] == DBNull.Value ? 0 : Convert.ToInt32(row["active_alert_count"]);
            report.RawReportData = row["raw_report_data"] == DBNull.Value ? null : row["raw_report_data"].ToString();
            report.VendorName = row["vendor_name"] == DBNull.Value ? null : row["vendor_name"].ToString();
            report.VendorReference = row["vendor_reference"] == DBNull.Value ? null : row["vendor_reference"].ToString();
            report.CreatedDt = row["created_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["created_dt"]);
            return report;
        }

        private CreditLiabilityData mapLiabilityRow(DataRow row)
        {
            CreditLiabilityData liab = new CreditLiabilityData();
            liab.LiabilityId = Convert.ToInt32(row["liability_id"]);
            liab.ReportId = Convert.ToInt32(row["report_id"]);
            liab.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            liab.CreditorName = row["creditor_name"] == DBNull.Value ? null : row["creditor_name"].ToString();
            liab.AccountNumber = row["account_number"] == DBNull.Value ? null : row["account_number"].ToString();
            liab.AccountType = row["account_type"] == DBNull.Value ? null : row["account_type"].ToString();
            liab.AccountStatus = row["account_status"] == DBNull.Value ? null : row["account_status"].ToString();
            liab.MonthlyPayment = row["monthly_payment"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["monthly_payment"]);
            liab.CurrentBalance = row["current_balance"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["current_balance"]);
            liab.HighCredit = row["high_credit"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["high_credit"]);
            liab.PastDueAmount = row["past_due_amount"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["past_due_amount"]);
            liab.MonthsReviewed = row["months_reviewed"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["months_reviewed"]);
            liab.Late30Count = row["late_30_count"] == DBNull.Value ? 0 : Convert.ToInt32(row["late_30_count"]);
            liab.Late60Count = row["late_60_count"] == DBNull.Value ? 0 : Convert.ToInt32(row["late_60_count"]);
            liab.Late90Count = row["late_90_count"] == DBNull.Value ? 0 : Convert.ToInt32(row["late_90_count"]);
            liab.IsIncludedInDti = row["is_included_in_dti"] == DBNull.Value ? true : Convert.ToBoolean(row["is_included_in_dti"]);
            liab.ExcludeReason = row["exclude_reason"] == DBNull.Value ? null : row["exclude_reason"].ToString();
            liab.AusResponsibleParty = row["aus_responsible_party"] == DBNull.Value ? null : row["aus_responsible_party"].ToString();
            liab.DateOpened = row["date_opened"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["date_opened"]);
            liab.DateReported = row["date_reported"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["date_reported"]);
            liab.Remarks = row["remarks"] == DBNull.Value ? null : row["remarks"].ToString();
            liab.CreatedDt = row["created_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["created_dt"]);
            return liab;
        }

        private CreditPublicRecordData mapPublicRecordRow(DataRow row)
        {
            CreditPublicRecordData record = new CreditPublicRecordData();
            record.PublicRecordId = Convert.ToInt32(row["public_record_id"]);
            record.ReportId = Convert.ToInt32(row["report_id"]);
            record.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            record.RecordType = row["record_type"] == DBNull.Value ? null : row["record_type"].ToString();
            record.CourtName = row["court_name"] == DBNull.Value ? null : row["court_name"].ToString();
            record.CaseNumber = row["case_number"] == DBNull.Value ? null : row["case_number"].ToString();
            record.FiledDate = row["filed_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["filed_date"]);
            record.Disposition = row["disposition"] == DBNull.Value ? null : row["disposition"].ToString();
            record.DispositionDate = row["disposition_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["disposition_date"]);
            record.Amount = row["amount"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["amount"]);
            record.MeetsWaitingPeriod = row["meets_waiting_period"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(row["meets_waiting_period"]);
            record.WaitingPeriodYears = row["waiting_period_years"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["waiting_period_years"]);
            record.ExtNotes = row["ext_notes"] == DBNull.Value ? null : row["ext_notes"].ToString();
            return record;
        }

        #endregion

        #region DEPRECATED - Use CreditReportService instead

        // Old analysis methods from the 2010 era. These returned simpler
        // data structures before we had proper model classes.
        // TODO: Remove after all callers are migrated (requested 2016)

        /// <summary>
        /// DEPRECATED: Use AnalyzeCreditHistory instead.
        /// Old method that returned a simple string summary.
        /// </summary>
        public string getCreditAnalysis(string loanNumber)
        {
            FileLogger.Warn("Credit", "getCreditAnalysis (deprecated) called for loan " + loanNumber);

            Dictionary<string, object> analysis = AnalyzeCreditHistory(loanNumber);
            if (analysis.ContainsKey("error"))
            {
                return "ERROR: " + analysis["error"];
            }

            int repScore = analysis.ContainsKey("representativeScore")
                ? Convert.ToInt32(analysis["representativeScore"]) : 0;
            int totalAccounts = analysis.ContainsKey("totalAccounts")
                ? Convert.ToInt32(analysis["totalAccounts"]) : 0;
            decimal utilization = analysis.ContainsKey("utilizationRatio")
                ? Convert.ToDecimal(analysis["utilizationRatio"]) : 0m;

            return "Score=" + repScore + " Accounts=" + totalAccounts + " Util=" + utilization + "%";
        }

        /// <summary>
        /// DEPRECATED: Use CheckForFraudAlert instead.
        /// </summary>
        public bool hasFraudAlert(string loanNumber)
        {
            FileLogger.Warn("Credit", "hasFraudAlert (deprecated) called for loan " + loanNumber);
            return CheckForFraudAlert(loanNumber);
        }

        /// <summary>
        /// DEPRECATED: Use CheckForPublicRecords instead.
        /// Old method that just returned a count instead of the actual records.
        /// </summary>
        public int getPublicRecordCount(string loanNumber)
        {
            FileLogger.Warn("Credit", "getPublicRecordCount (deprecated) called for loan " + loanNumber);
            return CheckForPublicRecords(loanNumber).Count;
        }

        #endregion

        #region Dead Code

        // This was an attempt to calculate a "credit grade" (A+ through D)
        // in 2011. It was never used because underwriting preferred to use
        // the actual FICO score. Keeping it here for reference.
        // - Frank D., 2011
        //
        // private string calculateCreditGrade(int ficoScore)
        // {
        //     if (ficoScore >= 760) return "A+";
        //     if (ficoScore >= 720) return "A";
        //     if (ficoScore >= 680) return "B+";
        //     if (ficoScore >= 640) return "B";
        //     if (ficoScore >= 600) return "C";
        //     return "D";
        // }

        // Unused helper from 2013. Was supposed to count "satisfactory"
        // accounts but nobody could agree on the definition.
        private int countSatisfactoryAccounts(List<CreditLiabilityData> liabilities)
        {
            int count = 0;
            foreach (CreditLiabilityData liab in liabilities)
            {
                // "Satisfactory" was never defined. This was the last attempt.
                if (liab.AccountStatus == "OPEN" && !liab.IsDelinquent)
                {
                    count++;
                }
            }
            return count;
        }

        // Another unused helper. I think this was for determining if a
        // borrower had "thin credit" but the logic was wrong. - K.T.
        private bool isThinFile(List<CreditLiabilityData> liabilities)
        {
            // Original logic: less than 3 accounts = thin file
            // This is wrong - thin file should also consider account age
            return liabilities.Count < 3;
        }

        #endregion
    }
}
