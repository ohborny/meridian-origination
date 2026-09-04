using System;
using System.Collections.Generic;
using System.IO;
using MortgageLOS;
using MortgageLOS.Credit;

namespace MortgageLOS.BatchJobs
{
    // =========================================================================
    // CreditPullJob - Processes pending credit pull requests
    //
    // Reads CSV files from data/batch/credit-pulls/ and processes each one.
    // Each CSV file contains a credit pull request with borrower info.
    //
    // NOTE: This job has a limit of 100 pulls per run to control costs.
    // Credit pulls cost $15-30 each. Don't remove the limit.
    // =========================================================================

    public class CreditPullJob
    {
        private DatabaseHelper _db;
        private FileHandoffHelper _fileHelper;
        private CreditReportService _creditService;

        public CreditPullJob()
        {
            _db = new DatabaseHelper();
            _fileHelper = new FileHandoffHelper();
            _creditService = new CreditReportService();
        }

        public int Execute()
        {
            string[] pendingFiles = _fileHelper.GetPendingFiles("credit-pulls", "*.csv");
            FileLogger.Info("BatchJobs", "CreditPullJob: Found " + pendingFiles.Length + " pending credit pull files");

            if (pendingFiles.Length == 0)
            {
                FileLogger.Info("BatchJobs", "CreditPullJob: No pending requests. Done.");
                return 0;
            }

            int processed = 0;
            int failed = 0;
            int maxPulls = 100; // Cost control

            foreach (string file in pendingFiles)
            {
                if (processed >= maxPulls)
                {
                    FileLogger.Warn("BatchJobs", "CreditPullJob: Reached max pulls (" + maxPulls + "). Remaining files will be processed next run.");
                    break;
                }

                try
                {
                    List<string[]> rows = _fileHelper.ReadCsvSkipHeader(file);
                    foreach (string[] row in rows)
                    {
                        if (row.Length < 3) continue;

                        string loanNumber = row[0];
                        FileLogger.Info("BatchJobs", "CreditPullJob: Processing credit pull for " + loanNumber);

                        CreditReportData report = _creditService.ProcessCreditPull(loanNumber);
                        if (report != null)
                        {
                            FileLogger.Info("BatchJobs", "CreditPullJob: Credit pulled for " + loanNumber + " - Score: " + report.RepresentativeScore);
                            processed++;
                        }
                        else
                        {
                            FileLogger.Error("BatchJobs", "CreditPullJob: Failed to pull credit for " + loanNumber);
                            failed++;
                        }
                    }

                    // Archive the processed file
                    _fileHelper.ArchiveFile(file, "credit-pulls");
                }
                catch (Exception ex)
                {
                    FileLogger.Error("BatchJobs", "CreditPullJob: Error processing file " + file, ex);
                    failed++;
                }
            }

            FileLogger.Info("BatchJobs", "CreditPullJob: Complete. Processed: " + processed + ", Failed: " + failed);
            return failed > 0 ? 1 : 0;
        }
    }
}
