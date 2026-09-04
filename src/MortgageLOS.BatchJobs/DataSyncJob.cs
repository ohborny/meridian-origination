using System;
using System.Data;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.BatchJobs
{
    // =========================================================================
    // DataSyncJob - Syncs customer data from los_customer to los_core
    //
    // This is the "denormalization" job that copies borrower name and SSN
    // from the customer database into the loans table in the core database.
    // This exists for "performance" - the original developers didn't want
    // to join across databases for every query.
    //
    // KNOWN ISSUES:
    // - Sync is one-way only. Changes in los_core are NOT propagated back.
    // - The join uses SSN hash, which can fail if entered differently.
    // - No conflict resolution. Last write wins.
    // =========================================================================

    public class DataSyncJob
    {
        private DatabaseHelper _db;

        public DataSyncJob()
        {
            _db = new DatabaseHelper();
        }

        public int Execute()
        {
            FileLogger.Info("BatchJobs", "DataSyncJob: Starting customer data sync");

            // Get loans that need customer data synced
            // We sync loans created in the last 30 days that don't have borrower data
            string coreSql = @"SELECT loan_id, loan_number, borrower_ssn_last4
                FROM loans
                WHERE created_dt > CURRENT_DATE - 30
                AND (borrower_firstname IS NULL OR borrower_lastname IS NULL)";
            DataTable coreLoans = _db.ExecuteCoreQuery(coreSql);

            if (coreLoans.Rows.Count == 0)
            {
                FileLogger.Info("BatchJobs", "DataSyncJob: No loans need syncing. Done.");
                return 0;
            }

            int synced = 0;
            int notFound = 0;
            int failed = 0;

            foreach (DataRow row in coreLoans.Rows)
            {
                int loanId = Convert.ToInt32(row["loan_id"]);
                string loanNumber = row["loan_number"].ToString();
                string ssnLast4 = row["borrower_ssn_last4"] == DBNull.Value ? null : row["borrower_ssn_last4"].ToString();

                try
                {
                    // Look up customer by SSN last 4 (fragile, see LOS-1873)
                    if (string.IsNullOrEmpty(ssnLast4))
                    {
                        notFound++;
                        continue;
                    }

                    string customerSql = @"SELECT c.FirstName, c.LastName, c.SsnLast4
                        FROM customers c
                        WHERE c.SsnLast4 = @ssn4
                        LIMIT 1";
                    DataTable customerData = _db.ExecuteCustomerQuery(customerSql,
                        _db.CreateParam("@ssn4", ssnLast4, DbType.String));

                    if (customerData.Rows.Count == 0)
                    {
                        notFound++;
                        FileLogger.Warn("BatchJobs", "DataSyncJob: No customer found for " + loanNumber + " (SSN4: " + ssnLast4 + ")");
                        continue;
                    }

                    DataRow custRow = customerData.Rows[0];
                    string firstName = custRow["FirstName"]?.ToString();
                    string lastName = custRow["LastName"]?.ToString();

                    // Update core loans table
                    string updateSql = @"UPDATE loans 
                        SET borrower_firstname = @firstName, borrower_lastname = @lastName,
                        updated_dt = @now, updated_by = 'batch_sync'
                        WHERE loan_id = @loanId";
                    _db.ExecuteCoreNonQuery(updateSql,
                        _db.CreateParam("@firstName", (object)firstName ?? DBNull.Value, DbType.String),
                        _db.CreateParam("@lastName", (object)lastName ?? DBNull.Value, DbType.String),
                        _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                        _db.CreateParam("@loanId", loanId, DbType.Int32));

                    synced++;
                }
                catch (Exception ex)
                {
                    failed++;
                    FileLogger.Error("BatchJobs", "DataSyncJob: Error syncing " + loanNumber, ex);
                }
            }

            FileLogger.Info("BatchJobs", "DataSyncJob: Complete. Synced: " + synced + ", Not found: " + notFound + ", Failed: " + failed);
            return failed > 0 ? 1 : 0;
        }
    }
}
