using System;
using System.Data;
using MortgageLOS;
using MortgageLOS.Credit;

namespace MortgageLOS.BatchJobs
{
    // =========================================================================
    // CreditSyncJob - Syncs credit scores from los_credit to los_core
    //
    // This is the "overnight sync" that copies representative credit scores
    // from the credit database to the core database. If this job fails, the
    // cached scores in los_core will be STALE. This has caused issues before.
    // See LOS-2289.
    // =========================================================================

    public class CreditSyncJob
    {
        private DatabaseHelper _db;
        private CreditReportService _creditService;

        public CreditSyncJob()
        {
            _db = new DatabaseHelper();
            _creditService = new CreditReportService();
        }

        public int Execute()
        {
            FileLogger.Info("BatchJobs", "CreditSyncJob: Starting credit score sync");

            // Get all loans that have credit reports but may have stale scores in core
            string sql = @"SELECT DISTINCT loan_number FROM credit_reports WHERE created_dt > CURRENT_DATE - 7";
            DataTable creditLoans = _db.ExecuteCreditQuery(sql);

            int synced = 0;
            int failed = 0;

            foreach (DataRow row in creditLoans.Rows)
            {
                string loanNumber = row["loan_number"].ToString();
                try
                {
                    bool success = _creditService.SyncCreditScoreToCore(loanNumber);
                    if (success)
                    {
                        synced++;
                    }
                    else
                    {
                        failed++;
                        FileLogger.Warn("BatchJobs", "CreditSyncJob: Failed to sync score for " + loanNumber);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    FileLogger.Error("BatchJobs", "CreditSyncJob: Error syncing " + loanNumber, ex);
                }
            }

            FileLogger.Info("BatchJobs", "CreditSyncJob: Complete. Synced: " + synced + ", Failed: " + failed);
            return failed > 0 ? 1 : 0;
        }
    }
}
