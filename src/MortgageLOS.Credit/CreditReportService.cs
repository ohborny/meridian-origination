using System;
using System.Data;
using System.Collections.Generic;
using System.Text;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Credit
{
    // =========================================================================
    // CreditReportService - Main credit report management service
    //
    // This service handles pulling credit reports (simulated), storing them
    // in los_credit, and syncing summary data to los_core.
    //
    // HISTORY:
    //   2008 - Original version by Frank D. (contractor, left 2009)
    //   2011 - Added reissue support (J. Martinez)
    //   2014 - Migrated from SqlClient to Npgsql (K. Thompson)
    //   2017 - Added trended data support (never fully implemented)
    //   2020 - Added sync to core (always flaky, see comments below)
    //
    // TODO: Replace mock credit pull with real vendor integration (was supposed
    //       to happen in 2012, then 2015, then 2019... still using mock data)
    // TODO: Add retry logic for SyncCreditScoreToCore (requested 2021)
    // =========================================================================

    public class CreditReportService
    {
        private readonly DatabaseHelper _db;
        private readonly FileHandoffHelper _fileHelper;
        private static readonly Random _rng = new Random();

        // Vendor names for mock data
        private static readonly string[] _vendors = { "CREDITINFO", "CORELOGIC", "MERIDIAN", "AVANTUS" };
        private static readonly string[] _creditorNames = {
            "CAPITAL ONE BANK", "CHASE CARD SERVICES", "BANK OF AMERICA", "CITIBANK",
            "DISCOVER FINANCIAL", "WELLS FARGO", "AMERICAN EXPRESS", "SALLIE MAE",
            "NAVIENT", "TOYOTA MOTOR CREDIT", "FORD MOTOR CREDIT", "ALLY FINANCIAL",
            "MACYS", "TARGET NATIONAL BANK", "HOME DEPOT CREDIT", "SYNCB",
            "BARCLAYS BANK DELAWARE", "COMENITY BANK", "JC PENNEY", "LOWES SYNCHRONY"
        };
        private static readonly string[] _accountTypes = {
            "REVOLVING", "INSTALLMENT", "MORTGAGE", "AUTO", "STUDENT", "OTHER"
        };
        private static readonly string[] _accountStatuses = {
            "OPEN", "OPEN", "OPEN", "OPEN", "CLOSED", "PAID", "COLLECTION", "CHARGEOFF"
        };
        private static readonly string[] _inquiryCompanies = {
            "QUICKEN LOANS", "WELLS FARGO HOME MORTGAGE", "BANK OF AMERICA",
            "ROCKET MORTGAGE", "LOANDEPOT", "CROSS COUNTRY MORTGAGE"
        };
        private static readonly string[] _scoreReasons = {
            "SERIOUS DELINQUENCY", "DEROGATORY PUBLIC RECORD", "LATE PAYMENTS ON ACCOUNTS",
            "TOO FEW ACCOUNTS CURRENTLY PAID", "LENGTH OF TIME ACCOUNTS ESTABLISHED",
            "TOO MANY INQUIRIES", "PROPORTION OF BALANCES TO LIMITS TOO HIGH",
            "AMOUNT OWED ON ACCOUNTS TOO HIGH", "LACK OF RECENT INSTALLMENT LOAN INFO"
        };

        public CreditReportService()
        {
            _db = new DatabaseHelper();
            _fileHelper = new FileHandoffHelper();
        }

        // For testing with a specific config
        public CreditReportService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
            _fileHelper = new FileHandoffHelper(config);
        }

        #region Credit Pull Request

        /// <summary>
        /// Writes a credit pull request to a CSV file for batch processing.
        /// The overnight batch job picks up files from data/batch/credit-pulls/.
        /// Returns the file path of the written CSV.
        /// </summary>
        public string RequestCreditPull(CreditPullRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (string.IsNullOrEmpty(request.LoanNumber))
            {
                FileLogger.Warn("Credit", "RequestCreditPull called with empty loan number");
                throw new ArgumentException("LoanNumber is required");
            }

            try
            {
                string[] headers = {
                    "LoanNumber", "BorrowerFirstName", "BorrowerLastName",
                    "BorrowerSsn", "BorrowerAddress", "BorrowerCity", "BorrowerState",
                    "BorrowerZip", "PullType", "RequestedBy", "RequestDt",
                    "CoborrowerFirstName", "CoborrowerLastName", "CoborrowerSsn", "IsJoint"
                };

                List<string[]> rows = new List<string[]>();
                rows.Add(new string[] {
                    request.LoanNumber ?? "",
                    request.BorrowerFirstName ?? "",
                    request.BorrowerLastName ?? "",
                    request.BorrowerSsn ?? "",
                    request.BorrowerAddress ?? "",
                    request.BorrowerCity ?? "",
                    request.BorrowerState ?? "",
                    request.BorrowerZip ?? "",
                    request.PullType ?? CreditPullType.HARD,
                    request.RequestedBy ?? "",
                    request.RequestDt.ToString("yyyy-MM-dd HH:mm:ss"),
                    request.CoborrowerFirstName ?? "",
                    request.CoborrowerLastName ?? "",
                    request.CoborrowerSsn ?? "",
                    request.IsJoint.ToString()
                });

                string fileName = FileHandoffHelper.CreateTimestampedFileName(
                    "creditpull_" + request.LoanNumber, "csv");

                string filePath = _fileHelper.WriteCsvWithHeader(
                    "credit-pulls", fileName, headers, rows);

                FileLogger.Info("Credit", "Credit pull request written: " + filePath +
                    " for loan " + request.LoanNumber);

                return filePath;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "Failed to write credit pull request for loan " +
                    request.LoanNumber, ex);
                throw;
            }
        }

        #endregion

        #region Credit Pull Processing

        /// <summary>
        /// Simulates pulling credit for a loan by generating realistic mock data.
        /// Saves the report and all detail records to los_credit.
        /// Returns the CreditReportData with the generated report.
        /// </summary>
        public CreditReportData ProcessCreditPull(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                throw new ArgumentException("loanNumber is required");
            }

            FileLogger.Info("Credit", "Processing credit pull for loan " + loanNumber);

            try
            {
                // HACK: There's a race condition here. If two users request a credit pull
                // for the same loan at the same time, we can end up with duplicate
                // reports in the database. The "fix" is to check if a report already
                // exists and skip if it does, but this is a TOCTOU bug - the check and
                // insert are not atomic. The real fix would be a unique constraint on
                // (loan_number, pulled_dt) or using a lock table, but nobody has time
                // to do that. This has been here since 2008. - Frank D.
                CreditReportData existing = GetCreditReport(loanNumber);
                if (existing != null && existing.ReportId > 0)
                {
                    FileLogger.Warn("Credit", "Credit report already exists for loan " +
                        loanNumber + ", returning existing report (race condition workaround)");
                    return existing;
                }

                // Generate mock credit data
                CreditReportData report = generateMockReport(loanNumber);

                // saveReport must run first: it is what assigns report.ReportId. Generating
                // the child rows beforehand stamped them all with ReportId 0, and every
                // insert then failed the report_id foreign key.
                saveReport(report);

                List<CreditScoreData> scores = generateMockScores(report.ReportId, loanNumber, report);
                List<CreditLiabilityData> liabilities = generateMockLiabilities(report.ReportId, loanNumber);
                List<CreditInquiryData> inquiries = generateMockInquiries(report.ReportId, loanNumber);
                List<CreditPublicRecordData> publicRecords = generateMockPublicRecords(report.ReportId, loanNumber);

                saveScores(scores);
                saveLiabilities(liabilities);
                saveInquiries(inquiries);
                savePublicRecords(publicRecords);

                FileLogger.Info("Credit", "Credit pull complete for loan " + loanNumber +
                    " - ReportId=" + report.ReportId + " RepScore=" + report.RepresentativeScore +
                    " Liabilities=" + liabilities.Count);

                return report;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "Failed to process credit pull for loan " + loanNumber, ex);
                throw;
            }
        }

        /// <summary>
        /// Reissues a credit report (re-pulls with REISSUE pull type).
        /// This creates a new report record linked to the same loan.
        /// </summary>
        public CreditReportData ReissueCreditReport(string loanNumber, string requestedBy)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                throw new ArgumentException("loanNumber is required");
            }

            FileLogger.Info("Credit", "Reissuing credit report for loan " + loanNumber +
                " requested by " + (requestedBy ?? "UNKNOWN"));

            try
            {
                // Generate new mock report with REISSUE pull type
                CreditReportData report = generateMockReport(loanNumber);
                report.PullType = CreditPullType.REISSUE;
                report.PulledBy = requestedBy ?? "SYSTEM";

                // Same ordering requirement as ProcessCreditPull: saveReport assigns ReportId.
                saveReport(report);

                List<CreditScoreData> scores = generateMockScores(report.ReportId, loanNumber, report);
                List<CreditLiabilityData> liabilities = generateMockLiabilities(report.ReportId, loanNumber);
                List<CreditInquiryData> inquiries = generateMockInquiries(report.ReportId, loanNumber);
                List<CreditPublicRecordData> publicRecords = generateMockPublicRecords(report.ReportId, loanNumber);

                saveScores(scores);
                saveLiabilities(liabilities);
                saveInquiries(inquiries);
                savePublicRecords(publicRecords);

                FileLogger.Info("Credit", "Credit reissue complete for loan " + loanNumber +
                    " - new ReportId=" + report.ReportId);

                return report;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "Failed to reissue credit for loan " + loanNumber, ex);
                throw;
            }
        }

        #endregion

        #region Credit Report Retrieval

        /// <summary>
        /// Retrieves the latest credit report for a loan from the database.
        /// Returns null if no report exists.
        /// </summary>
        public CreditReportData GetCreditReport(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                return null;
            }

            try
            {
                // Using parameterized query (newer pattern, added 2014)
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
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetCreditReport failed for loan " + loanNumber, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets all credit scores for a report.
        /// </summary>
        public List<CreditScoreData> GetCreditScores(int reportId)
        {
            List<CreditScoreData> result = new List<CreditScoreData>();

            if (reportId <= 0)
            {
                return result;
            }

            try
            {
                string sql = "SELECT * FROM credit_scores WHERE report_id = @reportId ORDER BY bureau";
                DataTable dt = _db.ExecuteCreditQuery(sql,
                    _db.CreateParam("@reportId", reportId, DbType.Int32));

                foreach (DataRow row in dt.Rows)
                {
                    result.Add(mapScoreRow(row));
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetCreditScores failed for reportId " + reportId, ex);
            }

            return result;
        }

        /// <summary>
        /// Gets all credit liabilities for a loan.
        /// </summary>
        public List<CreditLiabilityData> GetCreditLiabilities(string loanNumber)
        {
            List<CreditLiabilityData> result = new List<CreditLiabilityData>();

            if (string.IsNullOrEmpty(loanNumber))
            {
                return result;
            }

            try
            {
                // Legacy string concatenation pattern - don't change this, it works
                // and nobody wants to touch it. The loan_number comes from an internal
                // call so SQL injection is "unlikely" (famous last words).
                string sql = "SELECT * FROM credit_liabilities WHERE loan_number = '" +
                    loanNumber.Replace("'", "''") + "' ORDER BY date_opened";

                DataTable dt = _db.ExecuteCreditQuery(sql);

                foreach (DataRow row in dt.Rows)
                {
                    result.Add(mapLiabilityRow(row));
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetCreditLiabilities failed for loan " + loanNumber, ex);
            }

            return result;
        }

        /// <summary>
        /// Gets all credit inquiries for a report.
        /// </summary>
        public List<CreditInquiryData> GetCreditInquiries(int reportId)
        {
            List<CreditInquiryData> result = new List<CreditInquiryData>();

            if (reportId <= 0)
            {
                return result;
            }

            try
            {
                string sql = "SELECT * FROM credit_inquiries WHERE report_id = @reportId " +
                             "ORDER BY inquiry_date DESC";
                DataTable dt = _db.ExecuteCreditQuery(sql,
                    _db.CreateParam("@reportId", reportId, DbType.Int32));

                foreach (DataRow row in dt.Rows)
                {
                    result.Add(mapInquiryRow(row));
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetCreditInquiries failed for reportId " + reportId, ex);
            }

            return result;
        }

        /// <summary>
        /// Gets all public records for a report.
        /// </summary>
        public List<CreditPublicRecordData> GetPublicRecords(int reportId)
        {
            List<CreditPublicRecordData> result = new List<CreditPublicRecordData>();

            if (reportId <= 0)
            {
                return result;
            }

            try
            {
                string sql = "SELECT * FROM credit_public_records WHERE report_id = @reportId " +
                             "ORDER BY filed_date";
                DataTable dt = _db.ExecuteCreditQuery(sql,
                    _db.CreateParam("@reportId", reportId, DbType.Int32));

                foreach (DataRow row in dt.Rows)
                {
                    result.Add(mapPublicRecordRow(row));
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetPublicRecords failed for reportId " + reportId, ex);
            }

            return result;
        }

        #endregion

        #region Sync To Core

        /// <summary>
        /// Syncs the representative credit score from los_credit to los_core.loans.
        /// This is the "overnight sync" that copies the cached score so the core
        /// system doesn't have to query the credit database directly.
        ///
        /// WARNING: This sync job is UNRELIABLE. It fails silently about 5% of the
        /// time (network timeout between databases, usually). When it fails, the
        /// cached score in los_core.loans.borrower_credit_score becomes STALE and
        /// underwriting may use the wrong score. This has caused 3 denied loans to
        /// be approved (see LOS-2289) and 2 approved loans to be denied (LOS-2451).
        /// The overnight batch job retries 3 times but still fails occasionally.
        /// There is no alerting when this fails. You have been warned.
        /// </summary>
        public bool SyncCreditScoreToCore(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                FileLogger.Warn("Credit", "SyncCreditScoreToCore called with empty loan number");
                return false;
            }

            try
            {
                // Get the latest credit report from los_credit
                CreditReportData report = GetCreditReport(loanNumber);
                if (report == null || !report.RepresentativeScore.HasValue)
                {
                    FileLogger.Warn("Credit", "No credit report or representative score found for loan " +
                        loanNumber + " - cannot sync to core");
                    return false;
                }

                int scoreToSync = report.RepresentativeScore.Value;

                // Update los_core.loans with the cached score
                // Using parameterized query (added 2020 when we finally realized
                // string concat was a bad idea for user-facing data)
                string sql = "UPDATE loans SET borrower_credit_score = @score, " +
                             "updated_dt = CURRENT_TIMESTAMP, updated_by = 'credit_sync' " +
                             "WHERE loan_number = @loanNumber";

                int rowsAffected = _db.ExecuteCoreNonQuery(sql,
                    _db.CreateParam("@score", scoreToSync, DbType.Int32),
                    _db.CreateParam("@loanNumber", loanNumber, DbType.String));

                if (rowsAffected > 0)
                {
                    FileLogger.Info("Credit", "Synced credit score " + scoreToSync +
                        " to core for loan " + loanNumber);
                    return true;
                }
                else
                {
                    FileLogger.Warn("Credit", "Loan " + loanNumber +
                        " not found in los_core - score not synced");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // This happens more often than we'd like. The sync job will retry
                // but if it keeps failing, the score in los_core will be stale.
                FileLogger.Error("Credit", "SyncCreditScoreToCore FAILED for loan " +
                    loanNumber + " - score in los_core may be stale!", ex);
                return false;
            }
        }

        #endregion

        #region Mock Data Generation (private)

        private CreditReportData generateMockReport(string loanNumber)
        {
            // Generate realistic FICO scores (620-850 range)
            int experianScore = _rng.Next(620, 851);
            int equifaxScore = _rng.Next(620, 851);
            int transunionScore = _rng.Next(620, 851);

            // Representative score = middle score (standard industry practice)
            int repScore = getMiddleScore(experianScore, equifaxScore, transunionScore);

            // File status - mostly clear, sometimes frozen or thin
            string fileStatus = "CLEAR";
            int statusRoll = _rng.Next(1, 101);
            if (statusRoll <= 5) fileStatus = "FROZEN";
            else if (statusRoll <= 10) fileStatus = "THIN_FILE";
            else if (statusRoll <= 12) fileStatus = "NO_HIT";

            // Fraud alert - rare (~3% chance)
            bool fraudAlert = _rng.Next(1, 101) <= 3;
            int activeAlertCount = fraudAlert ? _rng.Next(1, 4) : 0;

            string vendor = _vendors[_rng.Next(_vendors.Length)];
            string vendorRef = "REF-" + _rng.Next(100000, 999999).ToString();

            CreditReportData report = new CreditReportData();
            report.LoanNumber = loanNumber;
            report.BorrowerSsnHash = FormatUtils.HashSsn("123-45-" + _rng.Next(1000, 9999).ToString());
            report.ReportType = "MERGED";
            report.PullType = CreditPullType.HARD;
            report.PulledBy = "SYSTEM";
            report.PulledDt = DateTime.Now;
            report.ExperianReportId = "EXP-" + _rng.Next(1000000, 9999999).ToString();
            report.EquifaxReportId = "EQF-" + _rng.Next(1000000, 9999999).ToString();
            report.TransunionReportId = "TUC-" + _rng.Next(1000000, 9999999).ToString();
            report.ExperianScore = experianScore;
            report.EquifaxScore = equifaxScore;
            report.TransunionScore = transunionScore;
            report.RepresentativeScore = repScore;
            report.FileStatus = fileStatus;
            report.FraudAlert = fraudAlert;
            report.ActiveAlertCount = activeAlertCount;
            report.RawReportData = "<credit_report><bureaus>3</bureaus></credit_report>";
            report.VendorName = vendor;
            report.VendorReference = vendorRef;
            report.CreatedDt = DateTime.Now;

            return report;
        }

        private List<CreditScoreData> generateMockScores(int reportId, string loanNumber, CreditReportData report)
        {
            List<CreditScoreData> scores = new List<CreditScoreData>();

            // Experian
            scores.Add(createScore(reportId, loanNumber, CreditBureau.EXPERIAN, CreditBureau.FICO_8,
                report.ExperianScore.Value));
            // Equifax
            scores.Add(createScore(reportId, loanNumber, CreditBureau.EQUIFAX, CreditBureau.FICO_8,
                report.EquifaxScore.Value));
            // TransUnion
            scores.Add(createScore(reportId, loanNumber, CreditBureau.TRANSUNION, CreditBureau.FICO_8,
                report.TransunionScore.Value));

            return scores;
        }

        private CreditScoreData createScore(int reportId, string loanNumber, string bureau,
            string scoreModel, int scoreValue)
        {
            CreditScoreData score = new CreditScoreData();
            score.ReportId = reportId;
            score.LoanNumber = loanNumber;
            score.Bureau = bureau;
            score.ScoreModel = scoreModel;
            score.ScoreValue = scoreValue;

            // Add 1-4 adverse action reason codes (more reasons for lower scores)
            int reasonCount = scoreValue < 680 ? 4 : (scoreValue < 720 ? 2 : 1);
            score.ScoreReason1 = reasonCount >= 1 ? _scoreReasons[_rng.Next(_scoreReasons.Length)] : null;
            score.ScoreReason2 = reasonCount >= 2 ? _scoreReasons[_rng.Next(_scoreReasons.Length)] : null;
            score.ScoreReason3 = reasonCount >= 3 ? _scoreReasons[_rng.Next(_scoreReasons.Length)] : null;
            score.ScoreReason4 = reasonCount >= 4 ? _scoreReasons[_rng.Next(_scoreReasons.Length)] : null;

            // Trended data - 20% chance
            score.TrendedDataAvailable = _rng.Next(1, 101) <= 20;
            score.TrendedData = score.TrendedDataAvailable ? "<trended>24months</trended>" : null;

            return score;
        }

        private List<CreditLiabilityData> generateMockLiabilities(int reportId, string loanNumber)
        {
            List<CreditLiabilityData> liabilities = new List<CreditLiabilityData>();

            // Generate 5-15 liabilities
            int count = _rng.Next(5, 16);
            for (int i = 0; i < count; i++)
            {
                CreditLiabilityData liab = new CreditLiabilityData();
                liab.ReportId = reportId;
                liab.LoanNumber = loanNumber;
                liab.CreditorName = _creditorNames[_rng.Next(_creditorNames.Length)];
                liab.AccountNumber = "****" + _rng.Next(1000, 9999).ToString();
                liab.AccountType = _accountTypes[_rng.Next(_accountTypes.Length)];
                liab.AccountStatus = _accountStatuses[_rng.Next(_accountStatuses.Length)];

                // Financials based on account type
                if (liab.AccountType == "REVOLVING")
                {
                    decimal balance = _rng.Next(0, 15001);
                    decimal limit = balance + _rng.Next(0, 20001);
                    liab.CurrentBalance = balance;
                    liab.HighCredit = limit;
                    liab.MonthlyPayment = Math.Round(balance * 0.03m, 2); // ~3% of balance
                }
                else if (liab.AccountType == "MORTGAGE")
                {
                    liab.CurrentBalance = _rng.Next(50000, 400001);
                    liab.HighCredit = liab.CurrentBalance;
                    liab.MonthlyPayment = Math.Round(liab.CurrentBalance.Value / 360m, 2);
                }
                else if (liab.AccountType == "AUTO")
                {
                    liab.CurrentBalance = _rng.Next(5000, 50001);
                    liab.HighCredit = liab.CurrentBalance + _rng.Next(0, 5001);
                    liab.MonthlyPayment = _rng.Next(200, 801);
                }
                else if (liab.AccountType == "STUDENT")
                {
                    liab.CurrentBalance = _rng.Next(5000, 80001);
                    liab.HighCredit = liab.CurrentBalance;
                    liab.MonthlyPayment = _rng.Next(100, 601);
                }
                else // INSTALLMENT or OTHER
                {
                    liab.CurrentBalance = _rng.Next(500, 20001);
                    liab.HighCredit = liab.CurrentBalance + _rng.Next(0, 5001);
                    liab.MonthlyPayment = _rng.Next(50, 501);
                }

                // Past due - 10% chance
                int pastDueRoll = _rng.Next(1, 101);
                if (pastDueRoll <= 10)
                {
                    liab.PastDueAmount = _rng.Next(50, 2001);
                }
                else
                {
                    liab.PastDueAmount = 0;
                }

                // History
                liab.MonthsReviewed = _rng.Next(12, 84);
                liab.Late30Count = pastDueRoll <= 10 ? _rng.Next(1, 5) : _rng.Next(0, 3);
                liab.Late60Count = pastDueRoll <= 5 ? _rng.Next(1, 3) : 0;
                liab.Late90Count = pastDueRoll <= 3 ? _rng.Next(1, 2) : 0;

                // DTI inclusion - default true, 10% excluded
                liab.IsIncludedInDti = _rng.Next(1, 101) > 10;
                liab.ExcludeReason = liab.IsIncludedInDti ? null : "PAID_AT_CLOSING";

                // AUS responsible party
                int ausRoll = _rng.Next(1, 4);
                liab.AusResponsibleParty = ausRoll == 1 ? "BORROWER" : (ausRoll == 2 ? "COBORROWER" : "JOINT");

                // Dates
                int yearsAgo = _rng.Next(1, 20);
                liab.DateOpened = DateTime.Now.AddYears(-yearsAgo).AddDays(-_rng.Next(0, 365));
                liab.DateReported = DateTime.Now.AddDays(-_rng.Next(1, 60));
                liab.Remarks = "";
                liab.CreatedDt = DateTime.Now;

                liabilities.Add(liab);
            }

            return liabilities;
        }

        private List<CreditInquiryData> generateMockInquiries(int reportId, string loanNumber)
        {
            List<CreditInquiryData> inquiries = new List<CreditInquiryData>();

            // 0-5 inquiries
            int count = _rng.Next(0, 6);
            for (int i = 0; i < count; i++)
            {
                CreditInquiryData inq = new CreditInquiryData();
                inq.ReportId = reportId;
                inq.LoanNumber = loanNumber;
                inq.InquiringCompany = _inquiryCompanies[_rng.Next(_inquiryCompanies.Length)];
                inq.InquiryDate = DateTime.Now.AddDays(-_rng.Next(1, 180));
                inq.InquiryType = "MORTGAGE";
                inq.IsRateShopping = _rng.Next(1, 101) <= 30;
                inq.ShoppingWindowDays = inq.IsRateShopping ? 14 : 0;
                inquiries.Add(inq);
            }

            return inquiries;
        }

        private List<CreditPublicRecordData> generateMockPublicRecords(int reportId, string loanNumber)
        {
            List<CreditPublicRecordData> records = new List<CreditPublicRecordData>();

            // 8% chance of having a public record
            if (_rng.Next(1, 101) <= 8)
            {
                CreditPublicRecordData record = new CreditPublicRecordData();
                record.ReportId = reportId;
                record.LoanNumber = loanNumber;

                int recordTypeRoll = _rng.Next(1, 5);
                if (recordTypeRoll == 1)
                {
                    record.RecordType = "BANKRUPTCY_CH7";
                    record.WaitingPeriodYears = 2;
                }
                else if (recordTypeRoll == 2)
                {
                    record.RecordType = "BANKRUPTCY_CH13";
                    record.WaitingPeriodYears = 2;
                }
                else if (recordTypeRoll == 3)
                {
                    record.RecordType = "JUDGEMENT";
                    record.WaitingPeriodYears = 7;
                }
                else
                {
                    record.RecordType = "TAX_LIEN";
                    record.WaitingPeriodYears = 7;
                }

                record.CourtName = "COUNTY COURT";
                record.CaseNumber = "CV-" + _rng.Next(2000, 9999).ToString() + "-" + _rng.Next(1000, 9999).ToString();
                record.FiledDate = DateTime.Now.AddYears(-_rng.Next(1, 10));
                record.Disposition = _rng.Next(1, 3) == 1 ? "DISCHARGED" : "ACTIVE";
                record.DispositionDate = record.Disposition == "DISCHARGED"
                    ? record.FiledDate.Value.AddYears(1) : (DateTime?)null;
                record.Amount = _rng.Next(1000, 100001);
                record.MeetsWaitingPeriod = (DateTime.Now - record.FiledDate.Value).TotalDays >
                    (record.WaitingPeriodYears.Value * 365.25);
                record.ExtNotes = "Auto-generated from credit pull";

                records.Add(record);
            }

            return records;
        }

        private int getMiddleScore(int a, int b, int c)
        {
            // Sort and return middle
            int[] scores = { a, b, c };
            Array.Sort(scores);
            return scores[1];
        }

        #endregion

        #region Database Save (private)

        private void saveReport(CreditReportData report)
        {
            // Using string concatenation for the INSERT because the original
            // developer (Frank D.) wrote it this way in 2008 and nobody changed it.
            // The report data is all internally generated so injection risk is low.
            // TODO: Convert to parameterized query (requested 2016, never done)
            StringBuilder sb = new StringBuilder();
            sb.Append("INSERT INTO credit_reports (");
            sb.Append("loan_number, borrower_ssn_hash, report_type, pull_type, ");
            sb.Append("pulled_by, pulled_dt, experian_report_id, equifax_report_id, ");
            sb.Append("transunion_report_id, experian_score, equifax_score, ");
            sb.Append("transunion_score, representative_score, file_status, ");
            sb.Append("fraud_alert, active_alert_count, raw_report_data, ");
            sb.Append("vendor_name, vendor_reference, created_dt");
            sb.Append(") VALUES (");
            sb.Append("'" + report.LoanNumber.Replace("'", "''") + "', ");
            sb.Append("'" + (report.BorrowerSsnHash ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + (report.ReportType ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + (report.PullType ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + (report.PulledBy ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + FormatUtils.FormatTimestampForDb(report.PulledDt) + "', ");
            sb.Append("'" + (report.ExperianReportId ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + (report.EquifaxReportId ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + (report.TransunionReportId ?? "").Replace("'", "''") + "', ");
            sb.Append(report.ExperianScore.HasValue ? report.ExperianScore.Value.ToString() : "NULL");
            sb.Append(", ");
            sb.Append(report.EquifaxScore.HasValue ? report.EquifaxScore.Value.ToString() : "NULL");
            sb.Append(", ");
            sb.Append(report.TransunionScore.HasValue ? report.TransunionScore.Value.ToString() : "NULL");
            sb.Append(", ");
            sb.Append(report.RepresentativeScore.HasValue ? report.RepresentativeScore.Value.ToString() : "NULL");
            sb.Append(", ");
            sb.Append("'" + (report.FileStatus ?? "").Replace("'", "''") + "', ");
            sb.Append(report.FraudAlert ? "TRUE" : "FALSE");
            sb.Append(", ");
            sb.Append(report.ActiveAlertCount.ToString());
            sb.Append(", ");
            sb.Append("'" + (report.RawReportData ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + (report.VendorName ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + (report.VendorReference ?? "").Replace("'", "''") + "', ");
            sb.Append("'" + FormatUtils.FormatTimestampForDb(report.CreatedDt) + "'");
            sb.Append(") RETURNING ReportId");

            object result = _db.ExecuteCreditScalar(sb.ToString());
            report.ReportId = Convert.ToInt32(result);
        }

        private void saveScores(List<CreditScoreData> scores)
        {
            foreach (CreditScoreData score in scores)
            {
                string sql = "INSERT INTO credit_scores (report_id, loan_number, bureau, " +
                             "score_model, score_value, score_reason_1, score_reason_2, " +
                             "score_reason_3, score_reason_4, trended_data_available, " +
                             "trended_data, created_dt) VALUES (" +
                             "@reportId, @loanNumber, @bureau, @scoreModel, @scoreValue, " +
                             "@reason1, @reason2, @reason3, @reason4, @trendedAvail, " +
                             "@trendedData, CURRENT_TIMESTAMP)";

                _db.ExecuteCreditNonQuery(sql,
                    _db.CreateParam("@reportId", score.ReportId, DbType.Int32),
                    _db.CreateParam("@loanNumber", score.LoanNumber, DbType.String),
                    _db.CreateParam("@bureau", score.Bureau, DbType.String),
                    _db.CreateParam("@scoreModel", score.ScoreModel, DbType.String),
                    _db.CreateParam("@scoreValue", score.ScoreValue, DbType.Int32),
                    _db.CreateParam("@reason1", (object)score.ScoreReason1 ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@reason2", (object)score.ScoreReason2 ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@reason3", (object)score.ScoreReason3 ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@reason4", (object)score.ScoreReason4 ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@trendedAvail", score.TrendedDataAvailable, DbType.Boolean),
                    _db.CreateParam("@trendedData", (object)score.TrendedData ?? DBNull.Value, DbType.Xml)
                );
            }
        }

        private void saveLiabilities(List<CreditLiabilityData> liabilities)
        {
            foreach (CreditLiabilityData liab in liabilities)
            {
                string sql = "INSERT INTO credit_liabilities (report_id, loan_number, " +
                             "creditor_name, account_number, account_type, account_status, " +
                             "monthly_payment, current_balance, high_credit, past_due_amount, " +
                             "months_reviewed, late_30_count, late_60_count, late_90_count, " +
                             "is_included_in_dti, exclude_reason, aus_responsible_party, " +
                             "date_opened, date_reported, remarks, created_dt) VALUES (" +
                             "@reportId, @loanNumber, @creditorName, @accountNumber, " +
                             "@accountType, @accountStatus, @monthlyPayment, @currentBalance, " +
                             "@highCredit, @pastDue, @monthsReviewed, @late30, @late60, " +
                             "@late90, @includedInDti, @excludeReason, @ausParty, " +
                             "@dateOpened, @dateReported, @remarks, CURRENT_TIMESTAMP)";

                _db.ExecuteCreditNonQuery(sql,
                    _db.CreateParam("@reportId", liab.ReportId, DbType.Int32),
                    _db.CreateParam("@loanNumber", liab.LoanNumber, DbType.String),
                    _db.CreateParam("@creditorName", liab.CreditorName, DbType.String),
                    _db.CreateParam("@accountNumber", liab.AccountNumber, DbType.String),
                    _db.CreateParam("@accountType", liab.AccountType, DbType.String),
                    _db.CreateParam("@accountStatus", liab.AccountStatus, DbType.String),
                    _db.CreateParam("@monthlyPayment", (object)liab.MonthlyPayment ?? DBNull.Value, DbType.Decimal),
                    _db.CreateParam("@currentBalance", (object)liab.CurrentBalance ?? DBNull.Value, DbType.Decimal),
                    _db.CreateParam("@highCredit", (object)liab.HighCredit ?? DBNull.Value, DbType.Decimal),
                    _db.CreateParam("@pastDue", (object)liab.PastDueAmount ?? DBNull.Value, DbType.Decimal),
                    _db.CreateParam("@monthsReviewed", (object)liab.MonthsReviewed ?? DBNull.Value, DbType.Int32),
                    _db.CreateParam("@late30", liab.Late30Count, DbType.Int32),
                    _db.CreateParam("@late60", liab.Late60Count, DbType.Int32),
                    _db.CreateParam("@late90", liab.Late90Count, DbType.Int32),
                    _db.CreateParam("@includedInDti", liab.IsIncludedInDti, DbType.Boolean),
                    _db.CreateParam("@excludeReason", (object)liab.ExcludeReason ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@ausParty", (object)liab.AusResponsibleParty ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@dateOpened", (object)liab.DateOpened ?? DBNull.Value, DbType.Date),
                    _db.CreateParam("@dateReported", (object)liab.DateReported ?? DBNull.Value, DbType.Date),
                    _db.CreateParam("@remarks", (object)liab.Remarks ?? DBNull.Value, DbType.String)
                );
            }
        }

        private void saveInquiries(List<CreditInquiryData> inquiries)
        {
            foreach (CreditInquiryData inq in inquiries)
            {
                string sql = "INSERT INTO credit_inquiries (report_id, loan_number, " +
                             "inquiring_company, inquiry_date, inquiry_type, " +
                             "is_rate_shopping, shopping_window_days, created_dt) VALUES (" +
                             "@reportId, @loanNumber, @company, @inquiryDate, @inquiryType, " +
                             "@rateShopping, @windowDays, CURRENT_TIMESTAMP)";

                _db.ExecuteCreditNonQuery(sql,
                    _db.CreateParam("@reportId", inq.ReportId, DbType.Int32),
                    _db.CreateParam("@loanNumber", inq.LoanNumber, DbType.String),
                    _db.CreateParam("@company", inq.InquiringCompany, DbType.String),
                    _db.CreateParam("@inquiryDate", inq.InquiryDate, DbType.Date),
                    _db.CreateParam("@inquiryType", inq.InquiryType, DbType.String),
                    _db.CreateParam("@rateShopping", inq.IsRateShopping, DbType.Boolean),
                    _db.CreateParam("@windowDays", inq.ShoppingWindowDays, DbType.Int32)
                );
            }
        }

        private void savePublicRecords(List<CreditPublicRecordData> records)
        {
            foreach (CreditPublicRecordData record in records)
            {
                string sql = "INSERT INTO credit_public_records (report_id, loan_number, " +
                             "record_type, court_name, case_number, filed_date, " +
                             "disposition, disposition_date, amount, meets_waiting_period, " +
                             "waiting_period_years, ext_notes, created_dt) VALUES (" +
                             "@reportId, @loanNumber, @recordType, @courtName, @caseNumber, " +
                             "@filedDate, @disposition, @dispositionDate, @amount, " +
                             "@meetsWaiting, @waitingYears, @extNotes, CURRENT_TIMESTAMP)";

                _db.ExecuteCreditNonQuery(sql,
                    _db.CreateParam("@reportId", record.ReportId, DbType.Int32),
                    _db.CreateParam("@loanNumber", record.LoanNumber, DbType.String),
                    _db.CreateParam("@recordType", record.RecordType, DbType.String),
                    _db.CreateParam("@courtName", (object)record.CourtName ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@caseNumber", (object)record.CaseNumber ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@filedDate", (object)record.FiledDate ?? DBNull.Value, DbType.Date),
                    _db.CreateParam("@disposition", (object)record.Disposition ?? DBNull.Value, DbType.String),
                    _db.CreateParam("@dispositionDate", (object)record.DispositionDate ?? DBNull.Value, DbType.Date),
                    _db.CreateParam("@amount", (object)record.Amount ?? DBNull.Value, DbType.Decimal),
                    _db.CreateParam("@meetsWaiting", (object)record.MeetsWaitingPeriod ?? DBNull.Value, DbType.Boolean),
                    _db.CreateParam("@waitingYears", (object)record.WaitingPeriodYears ?? DBNull.Value, DbType.Int32),
                    _db.CreateParam("@extNotes", (object)record.ExtNotes ?? DBNull.Value, DbType.String)
                );
            }
        }

        #endregion

        #region DataRow Mappers (private)

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

        private CreditScoreData mapScoreRow(DataRow row)
        {
            CreditScoreData score = new CreditScoreData();
            score.ScoreId = Convert.ToInt32(row["score_id"]);
            score.ReportId = Convert.ToInt32(row["report_id"]);
            score.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            score.Bureau = row["bureau"] == DBNull.Value ? null : row["bureau"].ToString();
            score.ScoreModel = row["score_model"] == DBNull.Value ? null : row["score_model"].ToString();
            score.ScoreValue = Convert.ToInt32(row["score_value"]);
            score.ScoreReason1 = row["score_reason_1"] == DBNull.Value ? null : row["score_reason_1"].ToString();
            score.ScoreReason2 = row["score_reason_2"] == DBNull.Value ? null : row["score_reason_2"].ToString();
            score.ScoreReason3 = row["score_reason_3"] == DBNull.Value ? null : row["score_reason_3"].ToString();
            score.ScoreReason4 = row["score_reason_4"] == DBNull.Value ? null : row["score_reason_4"].ToString();
            score.TrendedDataAvailable = row["trended_data_available"] == DBNull.Value ? false : Convert.ToBoolean(row["trended_data_available"]);
            score.TrendedData = row["trended_data"] == DBNull.Value ? null : row["trended_data"].ToString();
            return score;
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

        private CreditInquiryData mapInquiryRow(DataRow row)
        {
            CreditInquiryData inq = new CreditInquiryData();
            inq.InquiryId = Convert.ToInt32(row["inquiry_id"]);
            inq.ReportId = Convert.ToInt32(row["report_id"]);
            inq.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            inq.InquiringCompany = row["inquiring_company"] == DBNull.Value ? null : row["inquiring_company"].ToString();
            inq.InquiryDate = row["inquiry_date"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["inquiry_date"]);
            inq.InquiryType = row["inquiry_type"] == DBNull.Value ? null : row["inquiry_type"].ToString();
            inq.IsRateShopping = row["is_rate_shopping"] == DBNull.Value ? false : Convert.ToBoolean(row["is_rate_shopping"]);
            inq.ShoppingWindowDays = row["shopping_window_days"] == DBNull.Value ? 0 : Convert.ToInt32(row["shopping_window_days"]);
            return inq;
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

        // These methods are from the old CreditPullManager class that was merged
        // into this service in 2014. They are kept for backward compatibility but
        // should NOT be used. Use the PascalCase methods above instead.
        // TODO: Remove these after all callers are updated (requested 2015, still not done)

        /// <summary>
        /// DEPRECATED: Use RequestCreditPull instead.
        /// </summary>
        public string pullCredit(string loanNumber, string borrowerName, string ssn)
        {
            FileLogger.Warn("Credit", "pullCredit (deprecated) called for loan " + loanNumber);
            CreditPullRequest req = new CreditPullRequest();
            req.LoanNumber = loanNumber;
            req.BorrowerLastName = borrowerName;
            req.BorrowerSsn = ssn;
            req.PullType = CreditPullType.HARD;
            req.RequestedBy = "legacy_caller";
            req.RequestDt = DateTime.Now;
            return RequestCreditPull(req);
        }

        /// <summary>
        /// DEPRECATED: Use GetCreditReport instead.
        /// </summary>
        public int getCreditScore(string loanNumber)
        {
            FileLogger.Warn("Credit", "getCreditScore (deprecated) called for loan " + loanNumber);
            CreditReportData report = GetCreditReport(loanNumber);
            if (report != null && report.RepresentativeScore.HasValue)
            {
                return report.RepresentativeScore.Value;
            }
            return 0;
        }

        /// <summary>
        /// DEPRECATED: Use ProcessCreditPull instead.
        /// This old method doesn't save to the database, it just returns a score.
        /// </summary>
        public int pullCreditScore(string loanNumber)
        {
            FileLogger.Warn("Credit", "pullCreditScore (deprecated) called for loan " + loanNumber);
            CreditReportData report = ProcessCreditPull(loanNumber);
            if (report != null && report.RepresentativeScore.HasValue)
            {
                return report.RepresentativeScore.Value;
            }
            return 0;
        }

        // DEPRECATED: Old method that used to call the vendor API directly.
        // Kept here because some old batch jobs still reference it.
        // public CreditReportData pullCreditFromVendor(string loanNumber, string vendorCode)
        // {
        //     // This used to call the old XML-based vendor API
        //     // The vendor went out of business in 2013
        //     throw new NotSupportedException("Vendor API no longer available");
        // }

        #endregion

        #region Dead Code (kept for reference, do not use)

        // This was used in 2009 for the old "soft pull" feature that was
        // discontinued. Keeping it here in case we ever bring it back.
        // - Frank D., 2009
        //
        // private bool validateSoftPullEligibility(string loanNumber)
        // {
        //     // Soft pulls were only allowed for pre-qualification
        //     // and required special authorization
        //     string sql = "SELECT COUNT(*) FROM credit_pull_log " +
        //                  "WHERE loan_number = '" + loanNumber + "' " +
        //                  "AND pull_purpose = 'PREQUAL'";
        //     int count = Convert.ToInt32(_db.ExecuteCreditScalar(sql));
        //     return count > 0;
        // }

        // Unused helper from 2011 - was supposed to format the raw XML
        // from the credit bureau into something readable. Never finished.
        private string formatRawReportData(string rawData)
        {
            if (string.IsNullOrEmpty(rawData))
                return "";
            // TODO: Implement XML formatting (2011, never completed)
            return rawData;
        }

        // Another unused helper. I think this was for determining which
        // bureau to use as the "primary" but I can't remember. - K.T.
        private string determinePrimaryBureau(CreditReportData report)
        {
            // Always returned Experian because that was the default in 2008
            return CreditBureau.EXPERIAN;
        }

        #endregion
    }
}
