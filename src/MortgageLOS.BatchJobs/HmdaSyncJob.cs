using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;
using MortgageLOS.Compliance;

namespace MortgageLOS.BatchJobs
{
    // =========================================================================
    // HmdaSyncJob - Syncs HMDA data for loans that changed status
    //
    // Updates the action_taken field in los_compliance.hmda_data based on
    // the current loan status in los_core. This runs nightly to keep HMDA
    // data current for regulatory reporting.
    // =========================================================================

    public class HmdaSyncJob
    {
        private DatabaseHelper _db;
        private HmdaService _hmdaService;

        public HmdaSyncJob()
        {
            _db = new DatabaseHelper();
            _hmdaService = new HmdaService();
        }

        public int Execute()
        {
            FileLogger.Info("BatchJobs", "HmdaSyncJob: Starting HMDA data sync");

            int reportingYear = AppConfig.Instance.HmdaReportingYear;

            // Get loans that have HMDA records but may need action_taken updates
            string sql = @"SELECT l.loan_number, l.loan_status, l.funded_dt
                FROM loans l
                WHERE l.loan_number IN (
                    SELECT loan_number FROM hmda_data WHERE reporting_year = @year
                )
                AND l.loan_status IN ('FUNDED', 'DENIED', 'WITHDRAWN', 'CANCELLED', 'PURCHASED')";
            DataTable loans = _db.ExecuteCoreQuery(sql,
                _db.CreateParam("@year", reportingYear, DbType.Int32));

            int updated = 0;
            int failed = 0;

            foreach (DataRow row in loans.Rows)
            {
                string loanNumber = row["loan_number"].ToString();
                string status = row["loan_status"].ToString();

                try
                {
                    string actionTaken = MapStatusToHmdaAction(status);
                    DateTime? actionDt = row["funded_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["funded_dt"]);
                    if (actionDt == null) actionDt = DateTime.Now;

                    _hmdaService.UpdateHmdaAction(loanNumber, actionTaken, actionDt.Value);
                    updated++;
                }
                catch (Exception ex)
                {
                    failed++;
                    FileLogger.Error("BatchJobs", "HmdaSyncJob: Error updating " + loanNumber, ex);
                }
            }

            FileLogger.Info("BatchJobs", "HmdaSyncJob: Complete. Updated: " + updated + ", Failed: " + failed);
            return failed > 0 ? 1 : 0;
        }

        private string MapStatusToHmdaAction(string status)
        {
            switch (status)
            {
                case LoanStatus.FUNDED:
                case LoanStatus.SHIPPED:
                case LoanStatus.PURCHASED:
                    return HmdaAction.ORIGINATED;
                case LoanStatus.DENIED:
                    return HmdaAction.DENIED;
                case LoanStatus.WITHDRAWN:
                    return HmdaAction.WITHDRAWN;
                case LoanStatus.CANCELLED:
                    return HmdaAction.CLOSED_INCOMPLETE;
                default:
                    return HmdaAction.ORIGINATED;
            }
        }
    }
}
