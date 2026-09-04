using System;
using System.Threading;
using MortgageLOS;
using MortgageLOS.BatchJobs;

namespace MortgageLOS.BatchJobs
{
    // =========================================================================
    // MortgageLOS Batch Jobs - Console Application
    //
    // This is the entry point for all overnight batch processing. It takes a
    // single argument that specifies which job to run.
    //
    // USAGE:
    //   MortgageLOS.BatchJobs overnight       - Run all jobs in sequence
    //   MortgageLOS.BatchJobs creditpull      - Process pending credit pulls
    //   MortgageLOS.BatchJobs creditsync      - Sync credit scores to core DB
    //   MortgageLOS.BatchJobs compliancecheck - Run compliance checks
    //   MortgageLOS.BatchJobs investorreport  - Generate investor reports
    //   MortgageLOS.BatchJobs hmdasync        - Sync HMDA data
    //   MortgageLOS.BatchJobs databsync       - Sync customer data to core
    //
    // NOTE: This was originally a Windows Service (2008). It was converted to
    // a console app in 2015 for Docker deployment. The Windows Service version
    // is still running on the production server because nobody has decommissioned it.
    // =========================================================================

    class Program
    {
        static int Main(string[] args)
        {
            // Initialize logging
            FileLogger.Initialize("./logs", "INFO");
            FileLogger.Info("BatchJobs", "Batch job starting. Args: " + (args.Length > 0 ? args[0] : "(none)"));

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: MortgageLOS.BatchJobs <jobname>");
                Console.WriteLine("");
                Console.WriteLine("Available jobs:");
                Console.WriteLine("  overnight       - Run all jobs in sequence");
                Console.WriteLine("  creditpull      - Process pending credit pulls");
                Console.WriteLine("  creditsync      - Sync credit scores to core DB");
                Console.WriteLine("  compliancecheck - Run compliance checks");
                Console.WriteLine("  investorreport  - Generate investor reports");
                Console.WriteLine("  hmdasync        - Sync HMDA data");
                Console.WriteLine("  databsync       - Sync customer data to core");
                return 1;
            }

            string jobName = args[0].ToLowerInvariant();
            int exitCode = 0;

            try
            {
                switch (jobName)
                {
                    case "overnight":
                        exitCode = RunOvernightBatch(args);
                        break;
                    case "creditpull":
                        exitCode = RunCreditPullJob(args);
                        break;
                    case "creditsync":
                        exitCode = RunCreditSyncJob(args);
                        break;
                    case "compliancecheck":
                        exitCode = RunComplianceCheckJob(args);
                        break;
                    case "investorreport":
                        exitCode = RunInvestorReportJob(args);
                        break;
                    case "hmdasync":
                        exitCode = RunHmdaSyncJob(args);
                        break;
                    case "databsync":
                        exitCode = RunDataSyncJob(args);
                        break;
                    default:
                        Console.WriteLine("Unknown job: " + jobName);
                        FileLogger.Error("BatchJobs", "Unknown job: " + jobName);
                        return 1;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Fatal("BatchJobs", "Job " + jobName + " failed with exception", ex);
                Console.WriteLine("FATAL: " + ex.Message);
                exitCode = 99;
            }

            FileLogger.Info("BatchJobs", "Job " + jobName + " completed with exit code " + exitCode);
            return exitCode;
        }

        static int RunOvernightBatch(string[] args)
        {
            FileLogger.Info("BatchJobs", "=== Starting overnight batch ===");

            // Run all jobs in sequence. If one fails, log and continue.
            int failures = 0;

            if (RunDataSyncJob(args) != 0) { failures++; FileLogger.Error("BatchJobs", "DataSync failed"); }
            if (RunCreditPullJob(args) != 0) { failures++; FileLogger.Error("BatchJobs", "CreditPull failed"); }
            if (RunCreditSyncJob(args) != 0) { failures++; FileLogger.Error("BatchJobs", "CreditSync failed"); }
            if (RunComplianceCheckJob(args) != 0) { failures++; FileLogger.Error("BatchJobs", "ComplianceCheck failed"); }
            if (RunInvestorReportJob(args) != 0) { failures++; FileLogger.Error("BatchJobs", "InvestorReport failed"); }
            if (RunHmdaSyncJob(args) != 0) { failures++; FileLogger.Error("BatchJobs", "HmdaSync failed"); }

            FileLogger.Info("BatchJobs", "=== Overnight batch complete. " + failures + " failures. ===");
            return failures > 0 ? 1 : 0;
        }

        static int RunCreditPullJob(string[] args)
        {
            FileLogger.Info("BatchJobs", "Starting CreditPullJob");
            CreditPullJob job = new CreditPullJob();
            return job.Execute();
        }

        static int RunCreditSyncJob(string[] args)
        {
            FileLogger.Info("BatchJobs", "Starting CreditSyncJob");
            CreditSyncJob job = new CreditSyncJob();
            return job.Execute();
        }

        static int RunComplianceCheckJob(string[] args)
        {
            FileLogger.Info("BatchJobs", "Starting ComplianceCheckJob");
            ComplianceCheckJob job = new ComplianceCheckJob();
            return job.Execute();
        }

        static int RunInvestorReportJob(string[] args)
        {
            FileLogger.Info("BatchJobs", "Starting InvestorReportJob");
            InvestorReportJob job = new InvestorReportJob();
            return job.Execute();
        }

        static int RunHmdaSyncJob(string[] args)
        {
            FileLogger.Info("BatchJobs", "Starting HmdaSyncJob");
            HmdaSyncJob job = new HmdaSyncJob();
            return job.Execute();
        }

        static int RunDataSyncJob(string[] args)
        {
            FileLogger.Info("BatchJobs", "Starting DataSyncJob");
            DataSyncJob job = new DataSyncJob();
            return job.Execute();
        }
    }
}
