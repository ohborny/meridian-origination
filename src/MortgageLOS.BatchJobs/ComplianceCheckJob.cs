using System;
using System.Collections.Generic;
using System.Data;
using MortgageLOS;
using MortgageLOS.Compliance;

namespace MortgageLOS.BatchJobs
{
    // =========================================================================
    // ComplianceCheckJob - Runs compliance checks for loans in underwriting
    //
    // Queries all loans in UNDERWRITING or CONDITIONAL_APPROVAL status and
    // runs compliance checks for each one.
    // =========================================================================

    public class ComplianceCheckJob
    {
        private DatabaseHelper _db;
        private ComplianceCheckService _complianceService;

        public ComplianceCheckJob()
        {
            _db = new DatabaseHelper();
            _complianceService = new ComplianceCheckService();
        }

        public int Execute()
        {
            FileLogger.Info("BatchJobs", "ComplianceCheckJob: Starting compliance checks");

            // Get loans that need compliance checks
            string sql = @"SELECT loan_number FROM loans 
                WHERE loan_status IN ('UNDERWRITING', 'CONDITIONAL_APPROVAL', 'CLEAR_TO_CLOSE')
                AND loan_number NOT IN (
                    SELECT DISTINCT loan_number FROM compliance_checks 
                    WHERE chk_dt > CURRENT_DATE
                )";
            DataTable loans = _db.ExecuteCoreQuery(sql);

            int checked_count = 0;
            int failed = 0;

            foreach (DataRow row in loans.Rows)
            {
                string loanNumber = row["loan_number"].ToString();
                try
                {
                    List<ComplianceCheckData> results = _complianceService.RunComplianceChecks(loanNumber);
                    checked_count++;
                    FileLogger.Info("BatchJobs", "ComplianceCheckJob: Checked " + loanNumber + " - " + results.Count + " checks");
                }
                catch (Exception ex)
                {
                    failed++;
                    FileLogger.Error("BatchJobs", "ComplianceCheckJob: Error checking " + loanNumber, ex);
                }
            }

            FileLogger.Info("BatchJobs", "ComplianceCheckJob: Complete. Checked: " + checked_count + ", Failed: " + failed);
            return failed > 0 ? 1 : 0;
        }
    }
}
