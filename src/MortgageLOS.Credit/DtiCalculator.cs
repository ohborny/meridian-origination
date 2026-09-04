using System;
using System.Data;
using System.Collections.Generic;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Credit
{
    // =========================================================================
    // DtiCalculator - Debt-to-Income ratio calculation service
    //
    // Calculates front-end and back-end DTI ratios based on credit report
    // liabilities and borrower income. Results are cached in the
    // dti_calculations table.
    //
    // HISTORY:
    //   2009 - Original version by Frank D. (basic front/back DTI)
    //   2012 - Added loan-type-specific guidelines (J. Martinez)
    //   2016 - Added manual override support (K. Thompson)
    //   2020 - Added liability exclusion/inclusion methods
    //
    // TODO: Support DU/LP DTI overrides (requested 2018, never implemented)
    // TODO: Add residual income calculation for VA loans (requested 2019)
    // =========================================================================

    public class DtiCalculator
    {
        private readonly DatabaseHelper _db;

        public DtiCalculator()
        {
            _db = new DatabaseHelper();
        }

        public DtiCalculator(AppConfig config)
        {
            _db = new DatabaseHelper(config);
        }

        #region DTI Calculation

        /// <summary>
        /// Calculates DTI for a loan based on credit liabilities and income.
        /// Pulls liabilities from the credit report, sums monthly payments,
        /// calculates front-end and back-end DTI, checks against guidelines,
        /// and saves the result to dti_calculations.
        /// </summary>
        public DtiResultData CalculateDti(string loanNumber, decimal monthlyIncome,
            decimal coborrowerIncome, decimal proposedHousingPayment, string loanType)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                throw new ArgumentException("loanNumber is required");
            }

            if (monthlyIncome < 0)
            {
                throw new ArgumentException("monthlyIncome cannot be negative");
            }

            FileLogger.Info("Credit", "CalculateDti for loan " + loanNumber +
                " - income=" + monthlyIncome + " coborrower=" + coborrowerIncome +
                " housing=" + proposedHousingPayment + " type=" + loanType);

            try
            {
                // Get the latest credit report to link the DTI calculation
                CreditReportData report = getLatestReport(loanNumber);

                // Pull liabilities and sum monthly payments for DTI
                // Only include liabilities where is_included_in_dti = TRUE
                decimal totalDebts = 0m;
                if (report != null && report.ReportId > 0)
                {
                    totalDebts = sumIncludedLiabilities(report.ReportId);
                }
                else
                {
                    FileLogger.Warn("Credit", "No credit report found for loan " + loanNumber +
                        " - DTI will be calculated with zero debts");
                }

                decimal totalIncome = monthlyIncome + coborrowerIncome;

                // Front-end DTI = Housing Payment / Total Income
                decimal? frontEndDti = null;
                if (totalIncome > 0)
                {
                    frontEndDti = Math.Round((proposedHousingPayment / totalIncome) * 100m, 2);
                }

                // Back-end DTI = (Housing Payment + Total Debts) / Total Income
                decimal? backEndDti = null;
                if (totalIncome > 0)
                {
                    backEndDti = Math.Round(((proposedHousingPayment + totalDebts) / totalIncome) * 100m, 2);
                }

                // Determine the DTI guideline based on loan type
                decimal guideline = getDtiGuideline(loanType);

                // Check if back-end DTI exceeds the guideline
                bool exceedsGuideline = backEndDti.HasValue && backEndDti.Value > guideline;

                // Build the result object
                DtiResultData result = new DtiResultData();
                result.LoanNumber = loanNumber;
                result.ReportId = report != null ? (int?)report.ReportId : null;
                result.MonthlyIncome = monthlyIncome;
                result.CoborrowerIncome = coborrowerIncome;
                result.TotalMonthlyIncome = totalIncome;
                result.TotalMonthlyDebts = totalDebts;
                result.ProposedHousingPayment = proposedHousingPayment;
                result.FrontEndDti = frontEndDti;
                result.BackEndDti = backEndDti;
                result.ExceedsGuideline = exceedsGuideline;
                result.DtiGuideline = guideline;
                result.CalculatedBy = "DtiCalculator";
                result.CalculatedDt = DateTime.Now;

                // Save to database (upsert - one record per loan)
                saveDtiResult(result);

                FileLogger.Info("Credit", "DTI calculated for loan " + loanNumber +
                    " - Front=" + (frontEndDti.HasValue ? frontEndDti.Value.ToString() : "N/A") +
                    "% Back=" + (backEndDti.HasValue ? backEndDti.Value.ToString() : "N/A") +
                    "% Guideline=" + guideline + "% Exceeds=" + exceedsGuideline);

                return result;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "CalculateDti failed for loan " + loanNumber, ex);
                throw;
            }
        }

        /// <summary>
        /// Retrieves the cached DTI result for a loan.
        /// Returns null if no calculation exists.
        /// </summary>
        public DtiResultData GetDtiResult(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                return null;
            }

            try
            {
                // Using parameterized query (added 2016)
                string sql = "SELECT * FROM dti_calculations WHERE loan_number = @loanNumber";
                DataTable dt = _db.ExecuteCreditQuery(sql,
                    _db.CreateParam("@loanNumber", loanNumber, DbType.String));

                if (dt.Rows.Count == 0)
                {
                    return null;
                }

                return mapDtiRow(dt.Rows[0]);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetDtiResult failed for loan " + loanNumber, ex);
                return null;
            }
        }

        #endregion

        #region DTI Override

        /// <summary>
        /// Applies a manual DTI override. Requires approval reason and approver.
        /// The override value replaces the calculated back-end DTI for qualification.
        /// </summary>
        public bool OverrideDti(string loanNumber, decimal newDti, string reason, string approvedBy)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                return false;
            }

            if (string.IsNullOrEmpty(reason))
            {
                FileLogger.Warn("Credit", "OverrideDti called without a reason for loan " + loanNumber);
                return false;
            }

            if (string.IsNullOrEmpty(approvedBy))
            {
                FileLogger.Warn("Credit", "OverrideDti called without approver for loan " + loanNumber);
                return false;
            }

            try
            {
                string sql = "UPDATE dti_calculations SET manual_override_dti = @newDti, " +
                             "override_reason = @reason, override_approved_by = @approvedBy " +
                             "WHERE loan_number = @loanNumber";

                int rows = _db.ExecuteCreditNonQuery(sql,
                    _db.CreateParam("@newDti", newDti, DbType.Decimal),
                    _db.CreateParam("@reason", reason, DbType.String),
                    _db.CreateParam("@approvedBy", approvedBy, DbType.String),
                    _db.CreateParam("@loanNumber", loanNumber, DbType.String));

                if (rows > 0)
                {
                    FileLogger.Info("Credit", "DTI override applied for loan " + loanNumber +
                        " - new DTI=" + newDti + "% approved by " + approvedBy);
                    return true;
                }
                else
                {
                    FileLogger.Warn("Credit", "No DTI record found to override for loan " + loanNumber);
                    return false;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "OverrideDti failed for loan " + loanNumber, ex);
                return false;
            }
        }

        #endregion

        #region Liability DTI Inclusion

        /// <summary>
        /// Excludes a liability from DTI calculation (e.g., being paid off at closing).
        /// Sets is_included_in_dti = FALSE and records the reason.
        /// </summary>
        public bool ExcludeLiabilityFromDti(int liabilityId, string reason)
        {
            if (liabilityId <= 0)
            {
                return false;
            }

            try
            {
                string sql = "UPDATE credit_liabilities SET is_included_in_dti = FALSE, " +
                             "exclude_reason = @reason WHERE liability_id = @liabilityId";

                int rows = _db.ExecuteCreditNonQuery(sql,
                    _db.CreateParam("@reason", reason ?? "", DbType.String),
                    _db.CreateParam("@liabilityId", liabilityId, DbType.Int32));

                if (rows > 0)
                {
                    FileLogger.Info("Credit", "Liability " + liabilityId +
                        " excluded from DTI - reason: " + reason);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "ExcludeLiabilityFromDti failed for liability " +
                    liabilityId, ex);
                return false;
            }
        }

        /// <summary>
        /// Re-includes a previously excluded liability in DTI calculation.
        /// Clears the exclude_reason.
        /// </summary>
        public bool IncludeLiabilityInDti(int liabilityId)
        {
            if (liabilityId <= 0)
            {
                return false;
            }

            try
            {
                // Legacy string concatenation pattern (this method was written in 2020
                // but the developer copied the style from the old code)
                string sql = "UPDATE credit_liabilities SET is_included_in_dti = TRUE, " +
                             "exclude_reason = NULL WHERE liability_id = " + liabilityId.ToString();

                int rows = _db.ExecuteCreditNonQuery(sql);

                if (rows > 0)
                {
                    FileLogger.Info("Credit", "Liability " + liabilityId + " re-included in DTI");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "IncludeLiabilityInDti failed for liability " +
                    liabilityId, ex);
                return false;
            }
        }

        #endregion

        #region Total Monthly Debts

        /// <summary>
        /// Sums all monthly payments for liabilities on a loan.
        /// </summary>
        public decimal GetTotalMonthlyDebts(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
            {
                return 0m;
            }

            try
            {
                // BUG: This query does NOT filter by is_included_in_dti = TRUE.
                // It sums ALL liabilities regardless of whether they were excluded.
                // This means excluded liabilities (e.g., paid off at closing) are still
                // counted in the total. See LOS-3421.
                // Not fixed because changing it would alter existing DTI calculations
                // and nobody wants to recalculate all the cached values. The
                // CalculateDti method above uses sumIncludedLiabilities() which DOES
                // filter correctly, so this bug only affects callers of this method
                // directly. - K. Thompson, 2020
                string sql = "SELECT COALESCE(SUM(monthly_payment), 0) FROM credit_liabilities " +
                             "WHERE loan_number = @loanNumber";

                object result = _db.ExecuteCreditScalar(sql,
                    _db.CreateParam("@loanNumber", loanNumber, DbType.String));

                if (result == null || result == DBNull.Value)
                {
                    return 0m;
                }

                return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Credit", "GetTotalMonthlyDebts failed for loan " + loanNumber, ex);
                return 0m;
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

            DataRow row = dt.Rows[0];
            CreditReportData report = new CreditReportData();
            report.ReportId = Convert.ToInt32(row["ReportId"]);
            report.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            report.RepresentativeScore = row["representative_score"] == DBNull.Value
                ? (int?)null : Convert.ToInt32(row["representative_score"]);
            return report;
        }

        /// <summary>
        /// Sums monthly payments for liabilities that are included in DTI.
        /// This is the CORRECT method that filters by is_included_in_dti.
        /// </summary>
        private decimal sumIncludedLiabilities(int reportId)
        {
            string sql = "SELECT COALESCE(SUM(monthly_payment), 0) FROM credit_liabilities " +
                         "WHERE report_id = @reportId AND is_included_in_dti = TRUE";

            object result = _db.ExecuteCreditScalar(sql,
                _db.CreateParam("@reportId", reportId, DbType.Int32));

            if (result == null || result == DBNull.Value)
            {
                return 0m;
            }

            return Convert.ToDecimal(result);
        }

        private decimal getDtiGuideline(string loanType)
        {
            // Default to QM max
            if (string.IsNullOrEmpty(loanType))
            {
                return DtiLimits.QM_MAX_DTI;
            }

            switch (loanType.ToUpper())
            {
                case "CONVENTIONAL":
                case "CONV":
                    return DtiLimits.CONV_MAX_DTI;
                case "FHA":
                    return DtiLimits.FHA_MAX_DTI;
                case "VA":
                    return DtiLimits.VA_MAX_DTI;
                case "USDA":
                    return DtiLimits.USDA_MAX_DTI;
                case "NON_QM":
                    return DtiLimits.NON_QM_MAX_DTI;
                case "JUMBO":
                    // Jumbo uses QM threshold by default but can go higher
                    return DtiLimits.QM_MAX_DTI;
                default:
                    return DtiLimits.QM_MAX_DTI;
            }
        }

        private void saveDtiResult(DtiResultData result)
        {
            // Upsert: dti_calculations has UNIQUE constraint on loan_number
            // Using ON CONFLICT to handle insert-or-update in one statement
            string sql = "INSERT INTO dti_calculations (loan_number, report_id, " +
                         "monthly_income, coborrower_income, total_monthly_income, " +
                         "total_monthly_debts, proposed_housing_payment, front_end_dti, " +
                         "back_end_dti, exceeds_guideline, dti_guideline, calculated_by, " +
                         "calculated_dt, manual_override_dti, override_reason, " +
                         "override_approved_by) VALUES (" +
                         "@loanNumber, @reportId, @monthlyIncome, @coborrowerIncome, " +
                         "@totalIncome, @totalDebts, @housingPayment, @frontEndDti, " +
                         "@backEndDti, @exceeds, @guideline, @calculatedBy, " +
                         "@calculatedDt, NULL, NULL, NULL) " +
                         "ON CONFLICT (loan_number) DO UPDATE SET " +
                         "report_id = EXCLUDED.report_id, " +
                         "monthly_income = EXCLUDED.monthly_income, " +
                         "coborrower_income = EXCLUDED.coborrower_income, " +
                         "total_monthly_income = EXCLUDED.total_monthly_income, " +
                         "total_monthly_debts = EXCLUDED.total_monthly_debts, " +
                         "proposed_housing_payment = EXCLUDED.proposed_housing_payment, " +
                         "front_end_dti = EXCLUDED.front_end_dti, " +
                         "back_end_dti = EXCLUDED.back_end_dti, " +
                         "exceeds_guideline = EXCLUDED.exceeds_guideline, " +
                         "dti_guideline = EXCLUDED.dti_guideline, " +
                         "calculated_by = EXCLUDED.calculated_by, " +
                         "calculated_dt = EXCLUDED.calculated_dt";

            _db.ExecuteCreditNonQuery(sql,
                _db.CreateParam("@loanNumber", result.LoanNumber, DbType.String),
                _db.CreateParam("@reportId", (object)result.ReportId ?? DBNull.Value, DbType.Int32),
                _db.CreateParam("@monthlyIncome", result.MonthlyIncome, DbType.Decimal),
                _db.CreateParam("@coborrowerIncome", result.CoborrowerIncome, DbType.Decimal),
                _db.CreateParam("@totalIncome", result.TotalMonthlyIncome, DbType.Decimal),
                _db.CreateParam("@totalDebts", result.TotalMonthlyDebts, DbType.Decimal),
                _db.CreateParam("@housingPayment", result.ProposedHousingPayment, DbType.Decimal),
                _db.CreateParam("@frontEndDti", (object)result.FrontEndDti ?? DBNull.Value, DbType.Decimal),
                _db.CreateParam("@backEndDti", (object)result.BackEndDti ?? DBNull.Value, DbType.Decimal),
                _db.CreateParam("@exceeds", result.ExceedsGuideline, DbType.Boolean),
                _db.CreateParam("@guideline", result.DtiGuideline, DbType.Decimal),
                _db.CreateParam("@calculatedBy", result.CalculatedBy, DbType.String),
                _db.CreateParam("@calculatedDt", result.CalculatedDt, DbType.DateTime)
            );
        }

        private DtiResultData mapDtiRow(DataRow row)
        {
            DtiResultData result = new DtiResultData();
            result.DtiId = Convert.ToInt32(row["dti_id"]);
            result.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            result.ReportId = row["report_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["report_id"]);
            result.MonthlyIncome = row["monthly_income"] == DBNull.Value ? 0m : Convert.ToDecimal(row["monthly_income"]);
            result.CoborrowerIncome = row["coborrower_income"] == DBNull.Value ? 0m : Convert.ToDecimal(row["coborrower_income"]);
            result.TotalMonthlyIncome = row["total_monthly_income"] == DBNull.Value ? 0m : Convert.ToDecimal(row["total_monthly_income"]);
            result.TotalMonthlyDebts = row["total_monthly_debts"] == DBNull.Value ? 0m : Convert.ToDecimal(row["total_monthly_debts"]);
            result.ProposedHousingPayment = row["proposed_housing_payment"] == DBNull.Value ? 0m : Convert.ToDecimal(row["proposed_housing_payment"]);
            result.FrontEndDti = row["front_end_dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["front_end_dti"]);
            result.BackEndDti = row["back_end_dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["back_end_dti"]);
            result.ExceedsGuideline = row["exceeds_guideline"] == DBNull.Value ? false : Convert.ToBoolean(row["exceeds_guideline"]);
            result.DtiGuideline = row["dti_guideline"] == DBNull.Value ? DtiLimits.QM_MAX_DTI : Convert.ToDecimal(row["dti_guideline"]);
            result.CalculatedBy = row["calculated_by"] == DBNull.Value ? null : row["calculated_by"].ToString();
            result.CalculatedDt = row["calculated_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["calculated_dt"]);
            result.ManualOverrideDti = row["manual_override_dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["manual_override_dti"]);
            result.OverrideReason = row["override_reason"] == DBNull.Value ? null : row["override_reason"].ToString();
            result.OverrideApprovedBy = row["override_approved_by"] == DBNull.Value ? null : row["override_approved_by"].ToString();
            return result;
        }

        #endregion

        #region DEPRECATED - Use CreditReportService instead

        // Old DTI methods from the pre-2012 era. These used a simpler
        // calculation that didn't distinguish between front-end and back-end.
        // TODO: Remove after verifying no callers remain (requested 2014)

        /// <summary>
        /// DEPRECATED: Use CalculateDti instead.
        /// Old method that only calculated a single "total DTI" without
        /// separating front-end and back-end.
        /// </summary>
        public decimal calculateTotalDti(string loanNumber, decimal monthlyIncome, decimal totalDebts)
        {
            FileLogger.Warn("Credit", "calculateTotalDti (deprecated) called for loan " + loanNumber);
            if (monthlyIncome <= 0)
            {
                return 0m;
            }
            return Math.Round((totalDebts / monthlyIncome) * 100m, 2);
        }

        /// <summary>
        /// DEPRECATED: Use GetDtiResult instead.
        /// </summary>
        public decimal getDti(string loanNumber)
        {
            FileLogger.Warn("Credit", "getDti (deprecated) called for loan " + loanNumber);
            DtiResultData result = GetDtiResult(loanNumber);
            if (result != null && result.BackEndDti.HasValue)
            {
                return result.BackEndDti.Value;
            }
            return 0m;
        }

        #endregion

        #region Dead Code

        // This was an attempt to calculate DTI using trended data (2017).
        // Never completed because the trended data format kept changing.
        // - K. Thompson
        //
        // private decimal calculateTrendedDti(string loanNumber)
        // {
        //     // Would have used 24 months of payment history to project
        //     // future debt obligations. The idea was to catch borrowers
        //     // who were paying down debt rapidly vs accumulating.
        //     throw new NotImplementedException("Trended DTI not implemented");
        // }

        // Old helper from 2009 that checked if DTI was within "acceptable"
        // range. The acceptable range was hardcoded and changed per investor.
        // Replaced by getDtiGuideline in 2012.
        private bool isDtiAcceptable(decimal dti, string loanType)
        {
            // Always returned true because the original developer didn't
            // want to block loans in the calculation step. This was "by design"
            // according to the 2009 code review notes.
            return true;
        }

        #endregion
    }
}
