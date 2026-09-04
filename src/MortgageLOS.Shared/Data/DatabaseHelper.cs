using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Npgsql;

namespace MortgageLOS
{
    // =========================================================================
    // DatabaseHelper - Central database access for all 4 siloed databases
    //
    // This class was created in 2005 as a thin wrapper around SqlConnection.
    // In 2012 it was migrated to Npgsql (PostgreSQL) but the method signatures
    // were kept the same to "minimize disruption." As a result, some method
    // names still say "Sql" even though we're talking to PostgreSQL.
    //
    // IMPORTANT: Each database has its own connection. Transactions CANNOT span
    // multiple databases. If you need cross-database consistency, you have to
    // use compensating transactions or the overnight sync job. This is a
    // fundamental architectural limitation that will not be fixed.
    // =========================================================================

    public class DatabaseHelper
    {
        private readonly AppConfig _config;

        static DatabaseHelper()
        {
            // Npgsql 6 changed DbType.DateTime to map to 'timestamptz', which rejects any
            // DateTime with Kind=Local. Every caller in this codebase passes DateTime.Now,
            // so the driver is put back on the pre-6 mapping instead of rewriting them all.
            // Must be set before the first connection is opened.
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        public DatabaseHelper()
        {
            _config = AppConfig.Instance;
        }

        // For testing with a specific config
        public DatabaseHelper(AppConfig config)
        {
            _config = config;
        }

        #region Connection Management

        public NpgsqlConnection GetCoreConnection()
        {
            return CreateConnection("los_core");
        }

        public NpgsqlConnection GetCreditConnection()
        {
            return CreateConnection("los_credit");
        }

        public NpgsqlConnection GetComplianceConnection()
        {
            return CreateConnection("los_compliance");
        }

        public NpgsqlConnection GetCustomerConnection()
        {
            return CreateConnection("los_customer");
        }

        private NpgsqlConnection CreateConnection(string dbName)
        {
            string connStr = _config.GetConnectionString(dbName);
            NpgsqlConnection conn = new NpgsqlConnection(connStr);
            try
            {
                conn.Open();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to connect to " + dbName + " database: " + ex.Message, ex);
            }
            return conn;
        }

        #endregion

        #region Query Execution (Core DB)

        /// <summary>
        /// Execute a query on los_core and return results as DataTable.
        /// </summary>
        public DataTable ExecuteCoreQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteQuery(GetCoreConnection(), sql, parameters);
        }

        /// <summary>
        /// Execute a query on los_core and return a single scalar value.
        /// </summary>
        public object ExecuteCoreScalar(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteScalar(GetCoreConnection(), sql, parameters);
        }

        /// <summary>
        /// Execute a non-query (INSERT/UPDATE/DELETE) on los_core. Returns rows affected.
        /// </summary>
        public int ExecuteCoreNonQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteNonQuery(GetCoreConnection(), sql, parameters);
        }

        #endregion

        #region Query Execution (Credit DB)

        // NOTE: These are copy-pasted from the Core methods. Yes, we know.
        // The original plan was to use generics but it was "too complex" for
        // the 2008 team. Now it's too risky to change. - J. Martinez, 2015

        public DataTable ExecuteCreditQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteQuery(GetCreditConnection(), sql, parameters);
        }

        public object ExecuteCreditScalar(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteScalar(GetCreditConnection(), sql, parameters);
        }

        public int ExecuteCreditNonQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteNonQuery(GetCreditConnection(), sql, parameters);
        }

        #endregion

        #region Query Execution (Compliance DB)

        public DataTable ExecuteComplianceQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteQuery(GetComplianceConnection(), sql, parameters);
        }

        public object ExecuteComplianceScalar(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteScalar(GetComplianceConnection(), sql, parameters);
        }

        public int ExecuteComplianceNonQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteNonQuery(GetComplianceConnection(), sql, parameters);
        }

        #endregion

        #region Query Execution (Customer DB)

        public DataTable ExecuteCustomerQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteQuery(GetCustomerConnection(), sql, parameters);
        }

        public object ExecuteCustomerScalar(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteScalar(GetCustomerConnection(), sql, parameters);
        }

        public int ExecuteCustomerNonQuery(string sql, params NpgsqlParameter[] parameters)
        {
            return ExecuteNonQuery(GetCustomerConnection(), sql, parameters);
        }

        #endregion

        #region Internal Execution Methods

        private DataTable ExecuteQuery(NpgsqlConnection conn, string sql, NpgsqlParameter[] parameters)
        {
            DataTable dt = new DataTable();
            try
            {
                using (conn)
                using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    using (NpgsqlDataAdapter adapter = new NpgsqlDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Query execution failed: " + ex.Message + " | SQL: " + sql, ex);
            }
            return dt;
        }

        private object ExecuteScalar(NpgsqlConnection conn, string sql, NpgsqlParameter[] parameters)
        {
            try
            {
                using (conn)
                using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    return cmd.ExecuteScalar();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Scalar execution failed: " + ex.Message + " | SQL: " + sql, ex);
            }
        }

        private int ExecuteNonQuery(NpgsqlConnection conn, string sql, NpgsqlParameter[] parameters)
        {
            try
            {
                using (conn)
                using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    return cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("NonQuery execution failed: " + ex.Message + " | SQL: " + sql, ex);
            }
        }

        #endregion

        #region Transaction Support (single-database only)

        /// <summary>
        /// Execute multiple statements within a transaction on the specified database.
        /// </summary>
        public bool ExecuteInTransaction(string database, List<string> sqlStatements)
        {
            NpgsqlConnection conn = null;
            NpgsqlTransaction trans = null;
            try
            {
                switch (database)
                {
                    case "los_core":
                        conn = GetCoreConnection();
                        break;
                    case "los_credit":
                        conn = GetCreditConnection();
                        break;
                    case "los_compliance":
                        conn = GetComplianceConnection();
                        break;
                    case "los_customer":
                        conn = GetCustomerConnection();
                        break;
                    default:
                        throw new ArgumentException("Unknown database: " + database);
                }

                trans = conn.BeginTransaction();
                foreach (string sql in sqlStatements)
                {
                    using (NpgsqlCommand cmd = new NpgsqlCommand(sql, conn, trans))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                try { trans?.Rollback(); } catch { }
                throw new Exception("Transaction failed on " + database + ": " + ex.Message, ex);
            }
            finally
            {
                try { conn?.Close(); conn?.Dispose(); } catch { }
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Creates a parameter. Added in 2014 because people kept getting the
        /// parameter syntax wrong. This doesn't prevent all mistakes but it helps.
        /// </summary>
        public NpgsqlParameter CreateParam(string name, object value, DbType? dbType = null)
        {
            NpgsqlParameter p = new NpgsqlParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            if (dbType.HasValue)
            {
                p.DbType = dbType.Value;
            }
            return p;
        }

        /// <summary>
        /// Gets the next sequence value for a serial column.
        /// </summary>
        public long GetNextSequenceValue(string database, string sequenceName)
        {
            string sql = "SELECT nextval('" + sequenceName + "')";
            switch (database)
            {
                case "los_core":
                    return Convert.ToInt64(ExecuteCoreScalar(sql));
                case "los_credit":
                    return Convert.ToInt64(ExecuteCreditScalar(sql));
                case "los_compliance":
                    return Convert.ToInt64(ExecuteComplianceScalar(sql));
                case "los_customer":
                    return Convert.ToInt64(ExecuteCustomerScalar(sql));
                default:
                    throw new ArgumentException("Unknown database: " + database);
            }
        }

        #endregion
    }

    // =========================================================================
    // Database names as constants (used throughout the system)
    // =========================================================================

    public static class DatabaseNames
    {
        public const string CORE = "los_core";
        public const string CREDIT = "los_credit";
        public const string COMPLIANCE = "los_compliance";
        public const string CUSTOMER = "los_customer";
    }
}
