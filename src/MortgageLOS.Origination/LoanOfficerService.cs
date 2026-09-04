using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Origination
{
    // =========================================================================
    // LoanOfficerService - Loan officer and branch lookup
    // Created in 2010 when NMLS tracking was added.
    // =========================================================================

    public class LoanOfficerService
    {
        private DatabaseHelper _db;

        public LoanOfficerService()
        {
            _db = new DatabaseHelper();
        }

        public LoanOfficer GetLoanOfficer(string loCode)
        {
            if (string.IsNullOrEmpty(loCode))
                return null;

            string sql = "SELECT * FROM loan_officers WHERE lo_code = @code";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@code", loCode, DbType.String));

            if (dt.Rows.Count == 0)
                return null;

            return MapOfficerFromRow(dt.Rows[0]);
        }

        public LoanOfficer GetLoanOfficerById(int loId)
        {
            string sql = "SELECT * FROM loan_officers WHERE lo_id = @id";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@id", loId, DbType.Int32));

            if (dt.Rows.Count == 0)
                return null;

            return MapOfficerFromRow(dt.Rows[0]);
        }

        public List<LoanOfficer> ListLoanOfficers(int? branchId)
        {
            string sql;
            NpgsqlParameter[] parameters;

            if (branchId.HasValue)
            {
                sql = "SELECT * FROM loan_officers WHERE is_active = true AND branch_id = @branchId ORDER BY last_name, first_name";
                parameters = new[] { _db.CreateParam("@branchId", branchId.Value, DbType.Int32) };
            }
            else
            {
                sql = "SELECT * FROM loan_officers WHERE is_active = true ORDER BY last_name, first_name";
                parameters = new NpgsqlParameter[0];
            }

            DataTable dt = _db.ExecuteCoreQuery(sql, parameters);
            List<LoanOfficer> officers = new List<LoanOfficer>();
            foreach (DataRow row in dt.Rows)
            {
                officers.Add(MapOfficerFromRow(row));
            }
            return officers;
        }

        public Branch GetBranch(string branchCode)
        {
            if (string.IsNullOrEmpty(branchCode))
                return null;

            string sql = "SELECT * FROM branches WHERE branch_code = @code";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@code", branchCode, DbType.String));

            if (dt.Rows.Count == 0)
                return null;

            return MapBranchFromRow(dt.Rows[0]);
        }

        public Branch GetBranchById(int branchId)
        {
            string sql = "SELECT * FROM branches WHERE branch_id = @id";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@id", branchId, DbType.Int32));

            if (dt.Rows.Count == 0)
                return null;

            return MapBranchFromRow(dt.Rows[0]);
        }

        public List<Branch> ListBranches()
        {
            string sql = "SELECT * FROM branches WHERE is_active = true ORDER BY branch_code";
            DataTable dt = _db.ExecuteCoreQuery(sql);

            List<Branch> branches = new List<Branch>();
            foreach (DataRow row in dt.Rows)
            {
                branches.Add(MapBranchFromRow(row));
            }
            return branches;
        }

        private LoanOfficer MapOfficerFromRow(DataRow row)
        {
            LoanOfficer officer = new LoanOfficer();
            officer.LoId = Convert.ToInt32(row["lo_id"]);
            officer.LoCode = row["lo_code"]?.ToString();
            officer.FirstName = row["first_name"]?.ToString();
            officer.LastName = row["last_name"]?.ToString();
            officer.NmlsId = row["nmls_id"] == DBNull.Value ? null : row["nmls_id"].ToString();
            officer.BranchId = row["branch_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["branch_id"]);
            officer.EmailAddr = row["email_addr"] == DBNull.Value ? null : row["email_addr"].ToString();
            officer.PhoneNum = row["phone_num"] == DBNull.Value ? null : row["phone_num"].ToString();
            officer.IsActive = Convert.ToBoolean(row["is_active"]);
            // hire_date and term_date may not exist in all schema versions
            if (row.Table.Columns.Contains("hire_date") && row["hire_date"] != DBNull.Value)
                officer.HireDate = Convert.ToDateTime(row["hire_date"]);
            if (row.Table.Columns.Contains("term_date") && row["term_date"] != DBNull.Value)
                officer.TermDate = Convert.ToDateTime(row["term_date"]);
            return officer;
        }

        private Branch MapBranchFromRow(DataRow row)
        {
            Branch branch = new Branch();
            branch.BranchId = Convert.ToInt32(row["branch_id"]);
            branch.BranchCode = row["branch_code"]?.ToString();
            branch.BranchName = row["branch_name"]?.ToString();
            if (row.Table.Columns.Contains("branch_addr1"))
                branch.BranchAddr1 = row["branch_addr1"] == DBNull.Value ? null : row["branch_addr1"].ToString();
            if (row.Table.Columns.Contains("branch_city"))
                branch.BranchCity = row["branch_city"] == DBNull.Value ? null : row["branch_city"].ToString();
            if (row.Table.Columns.Contains("branch_state"))
                branch.BranchState = row["branch_state"] == DBNull.Value ? null : row["branch_state"].ToString();
            if (row.Table.Columns.Contains("branch_zip"))
                branch.BranchZip = row["branch_zip"] == DBNull.Value ? null : row["branch_zip"].ToString();
            if (row.Table.Columns.Contains("branch_phone"))
                branch.BranchPhone = row["branch_phone"] == DBNull.Value ? null : row["branch_phone"].ToString();
            branch.NmlsId = row["nmls_id"] == DBNull.Value ? null : row["nmls_id"].ToString();
            branch.RegionCode = row["region_code"] == DBNull.Value ? null : row["region_code"].ToString();
            branch.IsActive = Convert.ToBoolean(row["is_active"]);
            return branch;
        }
    }
}
