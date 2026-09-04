using System;
using System.Data;
using System.Collections.Generic;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Compliance
{
    // =========================================================================
    // ScraService - Servicemembers Civil Relief Act (SCRA) checks
    //
    // The SCRA (50 U.S.C. 3901 et seq., formerly 50 App. U.S.C. 3901) provides
    // protections for active-duty military members, including:
    //   - 6% cap on interest rates for pre-service debts (50 U.S.C. 3937)
    //   - Stay of foreclosure proceedings (50 U.S.C. 3956)
    //   - Stay of civil court proceedings
    //
    // HISTORY:
    //   2014-06-10  J. Martinez        Original. Called DMDC (Defense Manpower
    //                                  Data Center) SCRA database.
    //   2015-03-20  K. Thompson        Added rate cap application logic.
    //   2017-01-15  Sarah L.           Added foreclosure protection flag.
    //   2018-09-10  Sarah L.           Added verification reference tracking.
    //   2020-05-01  M. Patel           Patched for COVID-19 SCRA extensions.
    //   2023-11-20  D. Osei            Converted to simulated check (DMDC API
    //                                  integration broke and nobody fixed it).
    //
    // NOTE (2023): The DMDC SCRA API integration broke in 2022 when DMDC changed
    // their endpoint. Rather than fix it, we now SIMULATE the check with random
    // results. This is obviously not production-ready but management approved it
    // as a temporary measure. The real fix requires a new DMDC API integration.
    // - D. Osei
    //
    // TODO (2014): Cache SCRA results to avoid re-checking on every call.
    // TODO (2023): Re-integrate with DMDC SCRA API (https://scra.dmdc.osd.mil/).
    // TODO (2023): Add periodic re-check for loans in servicing (status changes).
    // =========================================================================

    public class ScraService
    {
        private readonly DatabaseHelper _db;
        private static readonly Random _rng = new Random();

        // Service branches for simulation
        private static readonly string[] _serviceBranches = {
            "ARMY", "NAVY", "AIR_FORCE", "MARINES", "COAST_GUARD", "SPACE_FORCE"
        };

        public ScraService()
        {
            _db = new DatabaseHelper();
        }

        public ScraService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
        }

        #region SCRA Check

        /// <summary>
        /// Runs an SCRA check for a borrower. SIMULATED - randomly determines
        /// active military status.
        ///
        /// In production, this should call the DMDC SCRA database
        /// (https://scra.dmdc.osd.mil/) to verify active duty status.
        /// The simulation returns ~2% of borrowers as active military, which
        /// is roughly the percentage of US adults who are active duty.
        ///
        /// The result is saved to the scra_checks table.
        /// </summary>
        public ScraCheckData RunScraCheck(string loanNumber, string borrowerName, string ssnHash, string verifiedBy)
        {
            FileLogger.Info("ScraService", "Running SCRA check for loan " + loanNumber + " borrower " + borrowerName);

            ScraCheckData scra = new ScraCheckData();
            scra.LoanNumber = loanNumber;
            scra.BorrowerName = borrowerName;
            scra.BorrowerSsnHash = ssnHash;
            scra.CheckDt = DateTime.Now;
            scra.VerifiedBy = verifiedBy;

            // SIMULATED SCRA CHECK
            // HACK (2023): This is a simulation. The real DMDC API integration is
            // broken. We randomly determine active military status with ~2% probability.
            // This is NOT production-ready. See method comment and TODO above.
            bool isActiveMilitary = _rng.NextDouble() < 0.02;

            scra.IsActiveMilitary = isActiveMilitary;
            scra.VerificationMethod = "SIMULATED";

            if (isActiveMilitary)
            {
                // Pick a random service branch
                scra.ServiceBranch = _serviceBranches[_rng.Next(_serviceBranches.Length)];

                // Simulate active duty dates (random start in last 5 years, end in next 2)
                scra.ActiveDutyStartDt = DateTime.Now.AddDays(-_rng.Next(365, 1825));
                scra.ActiveDutyEndDt = DateTime.Now.AddDays(_rng.Next(30, 730));

                // Generate a fake verification reference
                scra.VerificationReference = "SIM-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + _rng.Next(10000, 99999);

                FileLogger.Info("ScraService", "SCRA check: borrower " + borrowerName + " IS active military (" + scra.ServiceBranch + ") for loan " + loanNumber);
            }
            else
            {
                scra.ServiceBranch = null;
                scra.ActiveDutyStartDt = null;
                scra.ActiveDutyEndDt = null;
                scra.VerificationReference = "SIM-NOTACTIVE-" + DateTime.Now.ToString("yyyyMMddHHmmss");

                FileLogger.Info("ScraService", "SCRA check: borrower " + borrowerName + " is NOT active military for loan " + loanNumber);
            }

            // Save the check
            int scraId = SaveScraCheck(scra);
            scra.ScraId = scraId;

            // If active military, apply protections
            if (isActiveMilitary)
            {
                ApplyScraProtections(loanNumber);
            }

            return scra;
        }

        #endregion

        #region Retrieve

        /// <summary>
        /// Gets the most recent SCRA check for a loan. Returns null if not found.
        /// </summary>
        public ScraCheckData GetScraCheck(string loanNumber)
        {
            string sql = "SELECT * FROM scra_checks WHERE loan_number = @ln ORDER BY check_dt DESC LIMIT 1";
            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String));

            if (dt.Rows.Count == 0)
                return null;

            return MapScraFromRow(dt.Rows[0]);
        }

        #endregion

        #region Apply Protections

        /// <summary>
        /// Applies SCRA protections if the borrower is active military.
        /// - 6% interest rate cap (50 U.S.C. 3937)
        /// - Foreclosure protection (50 U.S.C. 3956)
        ///
        /// Returns true if protections were applied.
        /// </summary>
        public bool ApplyScraProtections(string loanNumber)
        {
            ScraCheckData scra = GetScraCheck(loanNumber);
            if (scra == null)
            {
                FileLogger.Warn("ScraService", "No SCRA check found for loan " + loanNumber + ". Cannot apply protections.");
                return false;
            }

            if (!scra.IsActiveMilitary)
            {
                FileLogger.Info("ScraService", "Borrower for loan " + loanNumber + " is not active military. No SCRA protections to apply.");
                return false;
            }

            FileLogger.Info("ScraService", "Applying SCRA protections for loan " + loanNumber + " (rate cap + foreclosure protection)");

            // 2015 style: parameterized update
            // Per 50 U.S.C. 3937: interest rate capped at 6% for pre-service debts
            // Per 50 U.S.C. 3956: stay of foreclosure proceedings
            string sql = @"UPDATE scra_checks
                SET rate_cap_applied = @cap,
                    rate_cap_pct = @pct,
                    foreclosure_protection = @fp
                WHERE scra_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@cap", true, DbType.Boolean),
                _db.CreateParam("@pct", 6.0m, DbType.Decimal),
                _db.CreateParam("@fp", true, DbType.Boolean),
                _db.CreateParam("@id", scra.ScraId, DbType.Int32)
            );

            // HACK (2017): We should also update the loan's interest rate in los_core
            // to actually cap it at 6%. But that requires a cross-database call and
            // the compliance DB can't write to los_core. The origination module would
            // need to pick this up. There's no automated mechanism for this.
            // Sarah L. flagged this in 2017 but it was never resolved.
            // TODO (2017): Build SCRA rate cap sync to los_core (NEVER DONE).
            // TODO (2020): During COVID, SCRA protections were extended. We don't
            //              handle the extended dates here. Manual process only.

            FileLogger.Info("ScraService", "SCRA protections applied for loan " + loanNumber + ". Rate capped at 6%, foreclosure protection enabled.");
            return rows > 0;
        }

        #endregion

        #region Private

        private int SaveScraCheck(ScraCheckData scra)
        {
            // 2018 style: parameterized insert with RETURNING
            string sql = @"INSERT INTO scra_checks
                (loan_number, borrower_name, borrower_ssn_hash, check_dt,
                 is_active_military, service_branch, active_duty_start_dt,
                 active_duty_end_dt, rate_cap_applied, rate_cap_pct,
                 foreclosure_protection, verified_by, verification_method,
                 verification_reference)
                VALUES
                (@ln, @bn, @ssn, @cdt, @active, @branch, @start, @end,
                 @cap, @pct, @fp, @vby, @vmethod, @vref)
                RETURNING scra_id";

            int newId = Convert.ToInt32(_db.ExecuteComplianceScalar(sql,
                _db.CreateParam("@ln", scra.LoanNumber, DbType.String),
                _db.CreateParam("@bn", (object)scra.BorrowerName ?? DBNull.Value, DbType.String),
                _db.CreateParam("@ssn", (object)scra.BorrowerSsnHash ?? DBNull.Value, DbType.String),
                _db.CreateParam("@cdt", scra.CheckDt, DbType.DateTime),
                _db.CreateParam("@active", scra.IsActiveMilitary, DbType.Boolean),
                _db.CreateParam("@branch", (object)scra.ServiceBranch ?? DBNull.Value, DbType.String),
                _db.CreateParam("@start", (object)scra.ActiveDutyStartDt ?? DBNull.Value, DbType.DateTime),
                _db.CreateParam("@end", (object)scra.ActiveDutyEndDt ?? DBNull.Value, DbType.DateTime),
                _db.CreateParam("@cap", scra.RateCapApplied, DbType.Boolean),
                _db.CreateParam("@pct", (object)scra.RateCapPct ?? DBNull.Value, DbType.Decimal),
                _db.CreateParam("@fp", scra.ForeclosureProtection, DbType.Boolean),
                _db.CreateParam("@vby", (object)scra.VerifiedBy ?? DBNull.Value, DbType.String),
                _db.CreateParam("@vmethod", (object)scra.VerificationMethod ?? DBNull.Value, DbType.String),
                _db.CreateParam("@vref", (object)scra.VerificationReference ?? DBNull.Value, DbType.String)
            ));

            return newId;
        }

        private ScraCheckData MapScraFromRow(DataRow row)
        {
            ScraCheckData s = new ScraCheckData();
            s.ScraId = Convert.ToInt32(row["scra_id"]);
            s.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            s.BorrowerName = row["borrower_name"] == DBNull.Value ? null : row["borrower_name"].ToString();
            s.BorrowerSsnHash = row["borrower_ssn_hash"] == DBNull.Value ? null : row["borrower_ssn_hash"].ToString();
            s.CheckDt = row["check_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["check_dt"]);
            s.IsActiveMilitary = row["is_active_military"] != DBNull.Value && Convert.ToBoolean(row["is_active_military"]);
            s.ServiceBranch = row["service_branch"] == DBNull.Value ? null : row["service_branch"].ToString();
            s.ActiveDutyStartDt = row["active_duty_start_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["active_duty_start_dt"]);
            s.ActiveDutyEndDt = row["active_duty_end_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["active_duty_end_dt"]);
            s.RateCapApplied = row["rate_cap_applied"] != DBNull.Value && Convert.ToBoolean(row["rate_cap_applied"]);
            s.RateCapPct = row["rate_cap_pct"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["rate_cap_pct"]);
            s.ForeclosureProtection = row["foreclosure_protection"] != DBNull.Value && Convert.ToBoolean(row["foreclosure_protection"]);
            s.VerifiedBy = row["verified_by"] == DBNull.Value ? null : row["verified_by"].ToString();
            s.VerificationMethod = row["verification_method"] == DBNull.Value ? null : row["verification_method"].ToString();
            s.VerificationReference = row["verification_reference"] == DBNull.Value ? null : row["verification_reference"].ToString();
            return s;
        }

        #endregion
    }
}
