using System;
using System.Data;
using System.Collections.Generic;
using Npgsql;
using MortgageLOS;
using MortgageLOS.Origination;

// =========================================================================
// PATCH HISTORY - ComplianceCheckService
// (This file is the most patched in the entire system. Every team lead has
//  left their fingerprints here. Do NOT "clean up" this file without reading
//  every comment. Half of them are load-bearing.)
//
// 2012-03-15  Frank D. (contractor)     Original version. Only HMDA + TRID.
// 2012-11-02  Frank D.                  Added state compliance (CA/TX/NY only).
// 2014-01-20  J. Martinez               Migrated from SqlClient to Npgsql.
//                                        Added QM check (ATR/QM rule, Jan 2014).
// 2015-06-10  K. Thompson                Added HOEPA/HPML checks. Rewrote state
//                                        check to use state_disclosure_rules table.
// 2017-08-14  Sarah L.                   Added fair lending hook. Changed error
//                                        handling to throw instead of swallow.
//                                        (Reverted half of it in 2018, see below.)
// 2018-01-10  Sarah L.                   HMDA 2018 rule changes. New race/eth fields.
//                                        Added reporting year logic.
// 2020-04-01  M. Patel                   COVID-19 forbearance patches. "Temporary."
//                                        (STILL HERE. See region below.)
// 2023-11-20  D. Osei                    Added SCRA orchestration call. Added
//                                        GetComplianceStatus rollup.
// 2024-02-15  D. Osei                    Patched QM check for Non-QM loans.
//                                        Added MANUAL_REVIEW for missing DTI.
//
// TODO (2014): Store rule_set_id in a config table instead of hardcoding.
// TODO (2017): Add HOEPA status validation against HMDA data.
// TODO (2020): Remove COVID temporary code once forbearance reporting ends.
// TODO (2023): Add ability to re-run a single check type (currently re-runs all).
// =========================================================================

namespace MortgageLOS.Compliance
{
    // =========================================================================
    // ComplianceCheckService - Main compliance check runner
    //
    // This is the orchestrator. It calls the individual check methods and
    // saves results to the compliance_checks table. Each check method returns
    // a ComplianceCheckData which is then persisted.
    //
    // WARNING: RunComplianceChecks does NOT run SCRA or fair lending by default.
    // Those are separate workflows (SCRA is per-borrower, fair lending is
    // quarterly review). Calling them here was considered in 2023 but rejected
    // because of performance. - D. Osei
    // =========================================================================

    public class ComplianceCheckService
    {
        private readonly DatabaseHelper _db;
        private readonly LoanApplicationService _loanSvc;
        private readonly HmdaService _hmdaSvc;
        private readonly TridService _tridSvc;

        // Hardcoded rule set versions. Should be in a table. See TODO 2014 above.
        // Updated whenever a reg changes. Last update: 2024 (TRID 2017 amendment).
        private const string RULE_SET_HMDA = "HMDA-2024";
        private const string RULE_SET_TRID = "TRID-2024";
        private const string RULE_SET_QM = "QM-2024";
        private const string RULE_SET_STATE = "STATE-2024";
        private const string CHECK_VERSION = "8.2.1";

        public ComplianceCheckService()
        {
            _db = new DatabaseHelper();
            _loanSvc = new LoanApplicationService();
            _hmdaSvc = new HmdaService();
            _tridSvc = new TridService();
        }

        // For testing with a specific config
        public ComplianceCheckService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
            _loanSvc = new LoanApplicationService(_db);
            _hmdaSvc = new HmdaService(config);
            _tridSvc = new TridService(config);
        }

        #region Main Orchestration

        /// <summary>
        /// Runs all applicable compliance checks for a loan and saves results.
        /// Returns the list of check results.
        ///
        /// NOTE: This re-runs ALL checks every time. There is no "only run failed"
        /// option. This was requested in 2017 (LOS-2234) and never implemented.
        /// </summary>
        public List<ComplianceCheckData> RunComplianceChecks(string loanNumber)
        {
            FileLogger.Info("Compliance", "RunComplianceChecks started for loan " + loanNumber);

            List<ComplianceCheckData> results = new List<ComplianceCheckData>();

            // Get the loan data - we need it for most checks
            LoanData loan = _loanSvc.GetLoan(loanNumber);
            if (loan == null)
            {
                FileLogger.Error("Compliance", "Loan not found: " + loanNumber);
                throw new Exception("Loan not found: " + loanNumber);
            }

            // --- HMDA check ---
            try
            {
                ComplianceCheckData hmdaCheck = RunHmdaCheck(loanNumber);
                if (hmdaCheck != null)
                {
                    results.Add(hmdaCheck);
                }
            }
            catch (Exception ex)
            {
                // HACK (2017, Sarah): We used to swallow this. Now we log and create
                // a MANUAL_REVIEW check so it shows up in the dashboard.
                FileLogger.Error("Compliance", "HMDA check failed for loan " + loanNumber, ex);
                results.Add(CreateErrorCheck(loanNumber, ComplianceCheckType.HMDA, "HMDA check threw: " + ex.Message));
            }

            // --- TRID check ---
            try
            {
                ComplianceCheckData tridCheck = RunTridCheck(loanNumber);
                if (tridCheck != null)
                {
                    results.Add(tridCheck);
                }
            }
            catch (Exception ex)
            {
                // 2015 style: log and create error check
                FileLogger.Error("Compliance", "TRID check failed for loan " + loanNumber, ex);
                results.Add(CreateErrorCheck(loanNumber, ComplianceCheckType.TRID, "TRID check threw: " + ex.Message));
            }

            // --- QM check ---
            try
            {
                ComplianceCheckData qmCheck = RunQmCheck(loanNumber);
                if (qmCheck != null)
                {
                    results.Add(qmCheck);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Compliance", "QM check failed for loan " + loanNumber, ex);
                results.Add(CreateErrorCheck(loanNumber, ComplianceCheckType.QM, "QM check threw: " + ex.Message));
            }

            // --- State compliance check ---
            // 2012: Only ran for CA, TX, NY. 2015: Rewrote to use state_disclosure_rules table.
            if (!string.IsNullOrEmpty(loan.PropertyState))
            {
                try
                {
                    List<ComplianceCheckData> stateChecks = RunStateComplianceCheck(loanNumber, loan.PropertyState);
                    results.AddRange(stateChecks);
                }
                catch (Exception ex)
                {
                    // 2014 J. Martinez style - just log it, don't create an error check.
                    // (Sarah disagreed with this in 2017 but never changed it here.)
                    FileLogger.Error("Compliance", "State compliance check failed for loan " + loanNumber + " state " + loan.PropertyState, ex);
                }
            }

            // Save all results
            foreach (ComplianceCheckData check in results)
            {
                try
                {
                    int chkId = SaveComplianceCheck(check);
                    check.ChkId = chkId;
                }
                catch (Exception ex)
                {
                    // HACK (2020): If save fails, we still return the result but without
                    // a chk_id. The dashboard will show it as unsaved. Better than losing
                    // the check entirely.
                    FileLogger.Error("Compliance", "Failed to save compliance check for loan " + loanNumber, ex);
                }
            }

            FileLogger.Info("Compliance", "RunComplianceChecks completed for loan " + loanNumber + ". " + results.Count + " checks.");
            return results;
        }

        #endregion

        #region HMDA Check

        /// <summary>
        /// Validates that HMDA data exists and is complete for the loan.
        /// Per 12 CFR 1003 (Regulation C).
        /// </summary>
        public ComplianceCheckData RunHmdaCheck(string loanNumber)
        {
            FileLogger.Info("Compliance", "Running HMDA check for loan " + loanNumber);

            ComplianceCheckData check = new ComplianceCheckData();
            check.LoanNumber = loanNumber;
            check.ChkType = ComplianceCheckType.HMDA;
            check.ChkDt = DateTime.Now;
            check.ChkBy = "SYSTEM";
            check.ChkVersion = CHECK_VERSION;
            check.RuleSetId = RULE_SET_HMDA;

            HmdaData hmda = _hmdaSvc.GetHmdaData(loanNumber);

            if (hmda == null)
            {
                check.ChkStatus = ComplianceStatus.FAIL;
                check.ChkResult = "HMDA record not found. A HMDA record must exist for every applicable loan per 12 CFR 1003.4.";
                FileLogger.Warn("Compliance", "HMDA check FAIL (no record) for loan " + loanNumber);
                return check;
            }

            // Validate the HMDA data
            List<string> errors = _hmdaSvc.ValidateHmdaData(loanNumber);

            if (errors.Count == 0)
            {
                check.ChkStatus = ComplianceStatus.PASS;
                check.ChkResult = "HMDA data is complete and valid.";
            }
            else
            {
                // 2018: If there are errors, it's a WARNING not a FAIL, because
                // some fields are only required at final action (not at application).
                // But if critical fields are missing, it's a FAIL.
                // HACK: This logic is simplified. The real rules are more nuanced.
                // See 12 CFR 1003.4 for the actual requirements.
                bool hasCriticalError = false;
                foreach (string err in errors)
                {
                    if (err.Contains("action_taken") || err.Contains("loan_type") || err.Contains("loan_amount"))
                    {
                        hasCriticalError = true;
                        break;
                    }
                }

                check.ChkStatus = hasCriticalError ? ComplianceStatus.FAIL : ComplianceStatus.WARNING;
                check.ChkResult = string.Join("; ", errors);
            }

            return check;
        }

        #endregion

        #region TRID Check

        /// <summary>
        /// Validates TRID timing requirements.
        /// Per 12 CFR 1026.19 (Regulation Z, TRID Rule).
        ///
        /// Checks:
        /// - LE delivered within 3 business days of application (1026.19(e)(1)(iii))
        /// - CD received at least 3 business days before consummation (1026.19(f)(1)(ii))
        /// - Changed circumstance revised LE within 3 business days (1026.19(e)(3)(iv))
        /// </summary>
        public ComplianceCheckData RunTridCheck(string loanNumber)
        {
            FileLogger.Info("Compliance", "Running TRID check for loan " + loanNumber);

            ComplianceCheckData check = new ComplianceCheckData();
            check.LoanNumber = loanNumber;
            check.ChkType = ComplianceCheckType.TRID;
            check.ChkDt = DateTime.Now;
            check.ChkBy = "SYSTEM";
            check.ChkVersion = CHECK_VERSION;
            check.RuleSetId = RULE_SET_TRID;

            TridTimelineData timeline = _tridSvc.GetTridTimeline(loanNumber);

            if (timeline == null)
            {
                // No TRID timeline. If the loan is still in early stages, this might
                // be OK. But if we're past application, it's a problem.
                LoanData loan = _loanSvc.GetLoan(loanNumber);
                if (loan != null && loan.ApplicationDt.HasValue)
                {
                    check.ChkStatus = ComplianceStatus.FAIL;
                    check.ChkResult = "TRID timeline not initialized but loan has application date. LE may not have been delivered per 12 CFR 1026.19(e).";
                }
                else
                {
                    check.ChkStatus = ComplianceStatus.NOT_APPLICABLE;
                    check.ChkResult = "TRID timeline not yet initialized (no application date).";
                }
                return check;
            }

            List<string> issues = new List<string>();

            // Check LE timing
            if (!timeline.LeIsSent)
            {
                // LE not yet sent. Check if we're past the deadline.
                if (DateTime.Now > timeline.LeRequiredByDt)
                {
                    issues.Add("LE not sent by required date " + timeline.LeRequiredByDt.ToString("yyyy-MM-dd") + " per 1026.19(e)(1)(iii).");
                }
            }
            else if (timeline.LeIsLate)
            {
                issues.Add("LE sent late: sent " + timeline.LeSentDt.Value.ToString("yyyy-MM-dd") + ", required by " + timeline.LeRequiredByDt.ToString("yyyy-MM-dd") + ".");
            }

            // Check changed circumstance revised LE
            if (timeline.HasChangedCircumstance && timeline.RevisedLeRequiredDt.HasValue)
            {
                if (!timeline.RevisedLeSentDt.HasValue)
                {
                    if (DateTime.Now > timeline.RevisedLeRequiredDt.Value)
                    {
                        issues.Add("Revised LE not sent by required date " + timeline.RevisedLeRequiredDt.Value.ToString("yyyy-MM-dd") + " per 1026.19(e)(3)(iv).");
                    }
                }
                else if (timeline.RevisedLeSentDt.Value > timeline.RevisedLeRequiredDt.Value)
                {
                    issues.Add("Revised LE sent late: sent " + timeline.RevisedLeSentDt.Value.ToString("yyyy-MM-dd") + ", required by " + timeline.RevisedLeRequiredDt.Value.ToString("yyyy-MM-dd") + ".");
                }
            }

            // Check CD waiting period
            if (timeline.CdIsSent && timeline.CdReceivedDt.HasValue)
            {
                if (!timeline.ConsummationDt.HasValue)
                {
                    // Consummation not recorded yet. Check if waiting period met.
                    DateTime earliestConsummation = DateUtils.CalculateCdConsummationDate(timeline.CdReceivedDt.Value);
                    // HACK (2023): If consummation date is not set, we can't fully verify.
                    // Just check the waiting period.
                    if (timeline.CdWaitingMet.HasValue && !timeline.CdWaitingMet.Value)
                    {
                        issues.Add("CD 3-business-day waiting period not met per 1026.19(f)(1)(ii).");
                    }
                }
                else
                {
                    // Consummation recorded. Verify it was at least 3 business days after CD received.
                    int businessDaysBetween = DateUtils.CountBusinessDays(timeline.CdReceivedDt.Value, timeline.ConsummationDt.Value);
                    if (businessDaysBetween < 3)
                    {
                        issues.Add("Consummation occurred only " + businessDaysBetween + " business days after CD received. Minimum 3 required per 1026.19(f)(1)(ii).");
                    }
                }
            }

            if (issues.Count == 0)
            {
                check.ChkStatus = ComplianceStatus.PASS;
                check.ChkResult = "TRID timing requirements met.";
            }
            else
            {
                check.ChkStatus = ComplianceStatus.FAIL;
                check.ChkResult = string.Join("; ", issues);
            }

            return check;
        }

        #endregion

        #region QM Check

        /// <summary>
        /// Checks Qualified Mortgage status.
        /// Per 12 CFR 1026.43 (Ability to Repay / Qualified Mortgage rule).
        ///
        /// A loan is QM if:
        /// - DTI <= 43% (general QM, though there are exceptions)
        /// - No risky features (no interest-only, no negative amortization,
        ///   no terms > 30 years, no balloon payments)
        /// - Points and fees do not exceed 3% of the loan amount
        ///
        /// NOTE (2024): Non-QM loans are NOT necessarily non-compliant. They
        /// just need to meet ATR requirements. This check returns NOT_APPLICABLE
        /// for Non-QM loans. - D. Osei
        /// </summary>
        public ComplianceCheckData RunQmCheck(string loanNumber)
        {
            FileLogger.Info("Compliance", "Running QM check for loan " + loanNumber);

            ComplianceCheckData check = new ComplianceCheckData();
            check.LoanNumber = loanNumber;
            check.ChkType = ComplianceCheckType.QM;
            check.ChkDt = DateTime.Now;
            check.ChkBy = "SYSTEM";
            check.ChkVersion = CHECK_VERSION;
            check.RuleSetId = RULE_SET_QM;

            LoanData loan = _loanSvc.GetLoan(loanNumber);
            if (loan == null)
            {
                check.ChkStatus = ComplianceStatus.FAIL;
                check.ChkResult = "Loan not found.";
                return check;
            }

            // Non-QM loans: skip QM check
            // 2024 patch: We used to FAIL these. Now we return NOT_APPLICABLE.
            // The product code is in the loan product, but we don't have it here.
            // HACK: We check the amortization type as a proxy. This is wrong but
            // it's what we have. - D. Osei, 2024
            if (!string.IsNullOrEmpty(loan.AmortizationType) &&
                loan.AmortizationType.IndexOf("INTEREST_ONLY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Interest-only is a risky feature, not QM
                check.ChkStatus = ComplianceStatus.WARNING;
                check.ChkResult = "Loan has interest-only feature. Does not meet general QM definition per 12 CFR 1026.43(e). Verify ATR compliance manually.";
                return check;
            }

            // Check DTI
            // 2024 patch: If DTI is null, we can't verify. Used to FAIL, now MANUAL_REVIEW.
            if (!loan.Dti.HasValue)
            {
                check.ChkStatus = ComplianceStatus.MANUAL_REVIEW;
                check.ChkResult = "DTI not calculated. Cannot verify QM status per 12 CFR 1026.43(e). Manual review required.";
                FileLogger.Warn("Compliance", "QM check: DTI missing for loan " + loanNumber);
                return check;
            }

            List<string> issues = new List<string>();

            // DTI <= 43% for general QM
            // NOTE: There are temporary QM categories (small creditor, rural, etc.)
            // that allow higher DTI. We don't check those here. See 1026.43(e)(2).
            if (loan.Dti.Value > DtiLimits.QM_MAX_DTI)
            {
                issues.Add("DTI " + loan.Dti.Value.ToString("F2") + "% exceeds 43% QM threshold per 12 CFR 1026.43(e)(2).");
            }

            // Check term (no more than 30 years)
            if (loan.TermMonths.HasValue && loan.TermMonths.Value > 360)
            {
                issues.Add("Term " + loan.TermMonths.Value + " months exceeds 30-year maximum for QM per 12 CFR 1026.43(e)(2)(ii).");
            }

            // TODO (2017): Check points and fees <= 3% of loan amount.
            // This requires pulling LoanPricingDetail from los_core which we
            // don't have a service for in this module. The Underwriting module
            // does this check. K. Thompson started it but never finished.
            // --- K. Thompson, 2017

            if (issues.Count == 0)
            {
                check.ChkStatus = ComplianceStatus.PASS;
                check.ChkResult = "Loan meets general QM criteria (DTI <= 43%, no risky features detected).";
            }
            else
            {
                check.ChkStatus = ComplianceStatus.FAIL;
                check.ChkResult = string.Join("; ", issues);
            }

            return check;
        }

        #endregion

        #region State Compliance Check

        /// <summary>
        /// Checks state-specific compliance rules from the state_disclosure_rules table.
        ///
        /// 2012: This only checked CA, TX, NY (hardcoded).
        /// 2015: Rewritten by K. Thompson to use the state_disclosure_rules table.
        /// The old hardcoded logic is removed (see git history if you need it).
        /// </summary>
        public List<ComplianceCheckData> RunStateComplianceCheck(string loanNumber, string stateCode)
        {
            FileLogger.Info("Compliance", "Running state compliance check for loan " + loanNumber + " state " + stateCode);

            List<ComplianceCheckData> results = new List<ComplianceCheckData>();

            LoanData loan = _loanSvc.GetLoan(loanNumber);
            if (loan == null)
            {
                FileLogger.Warn("Compliance", "State check: loan not found " + loanNumber);
                return results;
            }

            // Determine loan type. We don't have it directly on LoanData, so
            // we infer from product. HACK: This is unreliable. The product code
            // would tell us but we don't load it here.
            // For now, use CONVENTIONAL as default. - Sarah, 2017
            string loanType = LoanType.CONVENTIONAL;
            string loanPurpose = loan.LoanPurpose;
            if (string.IsNullOrEmpty(loanPurpose))
            {
                loanPurpose = LoanPurpose.PURCHASE;
            }

            // Use the DisclosureService to check state disclosures
            DisclosureService disclosureSvc = new DisclosureService();
            List<ComplianceCheckData> stateResults = disclosureSvc.CheckStateDisclosures(loanNumber, stateCode, loanType, loanPurpose);

            foreach (ComplianceCheckData stateCheck in stateResults)
            {
                stateCheck.ChkVersion = CHECK_VERSION;
                stateCheck.RuleSetId = RULE_SET_STATE;
                stateCheck.ChkDt = DateTime.Now;
                stateCheck.ChkBy = "SYSTEM";
                results.Add(stateCheck);
            }

            return results;
        }

        #endregion

        #region Retrieve Checks

        /// <summary>
        /// Retrieves all compliance checks for a loan, ordered by date descending.
        /// </summary>
        public List<ComplianceCheckData> GetComplianceChecks(string loanNumber)
        {
            List<ComplianceCheckData> results = new List<ComplianceCheckData>();

            // 2014 style: parameterized query
            string sql = "SELECT * FROM compliance_checks WHERE loan_number = @loanNumber ORDER BY chk_dt DESC";
            DataTable dt = _db.ExecuteComplianceQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapCheckFromRow(row));
            }

            return results;
        }

        /// <summary>
        /// Returns the overall compliance status for a loan.
        /// PASS if all checks pass (or N/A). FAIL if any check fails.
        /// WARNING if any check has warning (but none fail).
        /// </summary>
        public string GetComplianceStatus(string loanNumber)
        {
            // 2023 addition by D. Osei. Previously you had to look at each check
            // individually. This is a convenience rollup.

            List<ComplianceCheckData> checks = GetComplianceChecks(loanNumber);
            if (checks.Count == 0)
            {
                return ComplianceStatus.MANUAL_REVIEW;
            }

            bool hasFail = false;
            bool hasWarning = false;
            bool hasManualReview = false;

            foreach (ComplianceCheckData check in checks)
            {
                if (check.ChkStatus == ComplianceStatus.FAIL)
                {
                    hasFail = true;
                }
                else if (check.ChkStatus == ComplianceStatus.WARNING)
                {
                    hasWarning = true;
                }
                else if (check.ChkStatus == ComplianceStatus.MANUAL_REVIEW)
                {
                    hasManualReview = true;
                }
            }

            if (hasFail)
                return ComplianceStatus.FAIL;
            if (hasWarning)
                return ComplianceStatus.WARNING;
            if (hasManualReview)
                return ComplianceStatus.MANUAL_REVIEW;
            return ComplianceStatus.PASS;
        }

        #endregion

        #region Save

        /// <summary>
        /// Saves a compliance check result. Returns the chk_id.
        /// </summary>
        public int SaveComplianceCheck(ComplianceCheckData check)
        {
            // 2014 style: parameterized INSERT with RETURNING
            string sql = @"INSERT INTO compliance_checks
                (loan_number, chk_type, chk_subtype, chk_status, chk_result, chk_dt,
                 chk_by, chk_version, rule_set_id, has_exception, exception_id)
                VALUES
                (@loanNumber, @chkType, @chkSubtype, @chkStatus, @chkResult, @chkDt,
                 @chkBy, @chkVersion, @ruleSetId, @hasException, @exceptionId)
                RETURNING chk_id";

            int newId = Convert.ToInt32(_db.ExecuteComplianceScalar(sql,
                _db.CreateParam("@loanNumber", check.LoanNumber, DbType.String),
                _db.CreateParam("@chkType", check.ChkType, DbType.String),
                _db.CreateParam("@chkSubtype", (object)check.ChkSubtype ?? DBNull.Value, DbType.String),
                _db.CreateParam("@chkStatus", check.ChkStatus, DbType.String),
                _db.CreateParam("@chkResult", (object)check.ChkResult ?? DBNull.Value, DbType.String),
                _db.CreateParam("@chkDt", check.ChkDt, DbType.DateTime),
                _db.CreateParam("@chkBy", (object)check.ChkBy ?? DBNull.Value, DbType.String),
                _db.CreateParam("@chkVersion", (object)check.ChkVersion ?? DBNull.Value, DbType.String),
                _db.CreateParam("@ruleSetId", (object)check.RuleSetId ?? DBNull.Value, DbType.String),
                _db.CreateParam("@hasException", check.HasException, DbType.Boolean),
                _db.CreateParam("@exceptionId", (object)check.ExceptionId ?? DBNull.Value, DbType.Int32)
            ));

            FileLogger.Info("Compliance", "Saved compliance check " + newId + " for loan " + check.LoanNumber + " type " + check.ChkType);
            return newId;
        }

        #endregion

        #region Private Helpers

        private ComplianceCheckData CreateErrorCheck(string loanNumber, string chkType, string errorMsg)
        {
            return new ComplianceCheckData
            {
                LoanNumber = loanNumber,
                ChkType = chkType,
                ChkStatus = ComplianceStatus.MANUAL_REVIEW,
                ChkResult = errorMsg,
                ChkDt = DateTime.Now,
                ChkBy = "SYSTEM",
                ChkVersion = CHECK_VERSION,
                RuleSetId = "ERROR"
            };
        }

        private ComplianceCheckData MapCheckFromRow(DataRow row)
        {
            return new ComplianceCheckData
            {
                ChkId = Convert.ToInt32(row["chk_id"]),
                LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString(),
                ChkType = row["chk_type"] == DBNull.Value ? null : row["chk_type"].ToString(),
                ChkSubtype = row["chk_subtype"] == DBNull.Value ? null : row["chk_subtype"].ToString(),
                ChkStatus = row["chk_status"] == DBNull.Value ? null : row["chk_status"].ToString(),
                ChkResult = row["chk_result"] == DBNull.Value ? null : row["chk_result"].ToString(),
                ChkDt = row["chk_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["chk_dt"]),
                ChkBy = row["chk_by"] == DBNull.Value ? null : row["chk_by"].ToString(),
                ChkVersion = row["chk_version"] == DBNull.Value ? null : row["chk_version"].ToString(),
                RuleSetId = row["rule_set_id"] == DBNull.Value ? null : row["rule_set_id"].ToString(),
                HasException = row["has_exception"] != DBNull.Value && Convert.ToBoolean(row["has_exception"]),
                ExceptionId = row["exception_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["exception_id"]),
                ReviewedBy = row["reviewed_by"] == DBNull.Value ? null : row["reviewed_by"].ToString(),
                ReviewedDt = row["reviewed_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["reviewed_dt"])
            };
        }

        #endregion

        #region COVID-19 TEMPORARY CODE (2020)

        // =========================================================================
        // COVID-19 TEMPORARY CODE (2020)
        // Added by M. Patel, April 2020. This was supposed to be removed once the
        // COVID forbearance reporting requirement ended. It's still here.
        // The CFPB's COVID-19 reporting guidance (Procedural Statement, 2020)
        // required reporting of COVID forbearances on HMDA LAR.
        //
        // TODO (2020): Remove this once COVID reporting is no longer required.
        // TODO (2023): This is STILL here. CFPB made it permanent in 2023.
        //              So maybe don't remove it. But rename the region. - D. Osei
        // =========================================================================

        /// <summary>
        /// Flags a loan as having a COVID-19 forbearance for HMDA reporting.
        /// This sets the covid_forbearance flag on the HMDA record.
        /// </summary>
        public bool FlagCovidForbearance(string loanNumber, DateTime forbearanceDt)
        {
            // HACK (2020): Direct SQL update because HmdaService didn't have a method
            // for this. We should add one but everyone was busy with other COVID stuff.
            string sql = "UPDATE hmda_data SET covid_forbearance = @cf, covid_forbearance_dt = @cfdt, updated_dt = @now WHERE loan_number = @ln";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@cf", "1", DbType.String),
                _db.CreateParam("@cfdt", forbearanceDt, DbType.DateTime),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@ln", loanNumber, DbType.String)
            );

            FileLogger.Info("Compliance", "Flagged COVID forbearance for loan " + loanNumber + " on " + forbearanceDt.ToString("yyyy-MM-dd"));
            return rows > 0;
        }

        /// <summary>
        /// Clears the COVID forbearance flag. Used when a loan exits forbearance.
        /// </summary>
        public bool ClearCovidForbearance(string loanNumber)
        {
            string sql = "UPDATE hmda_data SET covid_forbearance = @cf, updated_dt = @now WHERE loan_number = @ln";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@cf", "0", DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@ln", loanNumber, DbType.String)
            );

            FileLogger.Info("Compliance", "Cleared COVID forbearance for loan " + loanNumber);
            return rows > 0;
        }

        #endregion
    }
}
