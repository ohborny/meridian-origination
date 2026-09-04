using System;
using System.Data;
using System.Collections.Generic;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Compliance
{
    // =========================================================================
    // ComplianceExceptionService - Exception management
    //
    // When a compliance check fails, an exception can be requested. Exceptions
    // are reviewed and approved/denied. Some exceptions are "reportable" meaning
    // they must be reported to regulators.
    //
    // HISTORY:
    //   2014-01-20  J. Martinez        Original. Simple request/approve flow.
    //   2015-06-10  K. Thompson        Added risk assessment and risk level.
    //   2017-08-14  Sarah L.           Added regulatory notification flag.
    //                                  Changed error handling to throw.
    //   2020-02-10  M. Patel           Added expiry date for COVID exceptions.
    //   2023-11-20  D. Osei            Added "reportable" flag for exceptions
    //                                  that must be included in regulatory reports.
    //
    // TODO (2015): Add email notification when exception is approved/denied.
    // TODO (2017): Add workflow for multi-level approval (currently single approver).
    // TODO (2020): Auto-expire COVID exceptions. (Never implemented.)
    // =========================================================================

    public class ComplianceExceptionService
    {
        private readonly DatabaseHelper _db;

        public ComplianceExceptionService()
        {
            _db = new DatabaseHelper();
        }

        public ComplianceExceptionService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
        }

        #region Request

        /// <summary>
        /// Requests a compliance exception for a loan.
        /// Returns the exception_id.
        ///
        /// riskLevel should be one of: LOW, MEDIUM, HIGH, CRITICAL.
        /// </summary>
        public int RequestException(string loanNumber, string exceptionType, string reason,
            string requestedBy, string riskLevel)
        {
            FileLogger.Info("ExceptionService", "Requesting exception for loan " + loanNumber + " type " + exceptionType + " risk " + riskLevel);

            // 2017 style: parameterized query
            string sql = @"INSERT INTO compliance_exceptions
                (loan_number, exception_type, exception_reason, requested_by,
                 requested_dt, approval_status, risk_level, is_reportable)
                VALUES
                (@ln, @et, @reason, @by, @now, @status, @risk, @reportable)
                RETURNING exception_id";

            // Determine if this exception type is reportable
            // HACK (2023): This is a crude heuristic. The real determination should
            // come from a rule table. For now, HMDA and TRID exceptions are always
            // reportable. Others are not. - D. Osei
            bool isReportable = exceptionType == ComplianceCheckType.HMDA ||
                                 exceptionType == ComplianceCheckType.TRID;

            int newId = Convert.ToInt32(_db.ExecuteComplianceScalar(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String),
                _db.CreateParam("@et", exceptionType, DbType.String),
                _db.CreateParam("@reason", (object)reason ?? DBNull.Value, DbType.String),
                _db.CreateParam("@by", (object)requestedBy ?? DBNull.Value, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@status", "PENDING", DbType.String),
                _db.CreateParam("@risk", (object)riskLevel ?? "MEDIUM", DbType.String),
                _db.CreateParam("@reportable", isReportable, DbType.Boolean)
            ));

            FileLogger.Info("ExceptionService", "Created exception " + newId + " for loan " + loanNumber);
            return newId;
        }

        #endregion

        #region Approve / Deny

        /// <summary>
        /// Approves a compliance exception.
        /// </summary>
        public bool ApproveException(int exceptionId, string approvedBy, string riskAssessment)
        {
            FileLogger.Info("ExceptionService", "Approving exception " + exceptionId + " by " + approvedBy);

            string sql = @"UPDATE compliance_exceptions
                SET approved_by = @by,
                    approved_dt = @now,
                    approval_status = @status,
                    risk_assessment = @risk
                WHERE exception_id = @id AND approval_status = 'PENDING'";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@by", (object)approvedBy ?? DBNull.Value, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@status", "APPROVED", DbType.String),
                _db.CreateParam("@risk", (object)riskAssessment ?? DBNull.Value, DbType.String),
                _db.CreateParam("@id", exceptionId, DbType.Int32)
            );

            if (rows == 0)
            {
                // 2017 Sarah style: throw if nothing was updated
                FileLogger.Warn("ExceptionService", "Exception " + exceptionId + " not found or not pending");
                throw new Exception("Exception " + exceptionId + " not found or is not in PENDING status.");
            }

            return true;
        }

        /// <summary>
        /// Denies a compliance exception.
        /// </summary>
        public bool DenyException(int exceptionId, string deniedBy, string reason)
        {
            FileLogger.Info("ExceptionService", "Denying exception " + exceptionId + " by " + deniedBy);

            // 2020 M. Patel style: different approach - update with denial reason
            // stored in risk_assessment field (reused, since there's no denial_reason column)
            // HACK (2020): We store the denial reason in risk_assessment because the
            // table doesn't have a denial_reason column. This is ugly but it works.
            string sql = @"UPDATE compliance_exceptions
                SET approved_by = @by,
                    approved_dt = @now,
                    approval_status = @status,
                    risk_assessment = @reason
                WHERE exception_id = @id AND approval_status = 'PENDING'";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@by", (object)deniedBy ?? DBNull.Value, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@status", "DENIED", DbType.String),
                _db.CreateParam("@reason", "DENIED: " + (reason ?? "No reason provided"), DbType.String),
                _db.CreateParam("@id", exceptionId, DbType.Int32)
            );

            if (rows == 0)
            {
                FileLogger.Warn("ExceptionService", "Exception " + exceptionId + " not found or not pending");
                return false;
            }

            return true;
        }

        #endregion

        #region Retrieve

        /// <summary>
        /// Gets all exceptions for a loan.
        /// </summary>
        public List<ComplianceExceptionData> GetExceptions(string loanNumber)
        {
            List<ComplianceExceptionData> results = new List<ComplianceExceptionData>();

            string sql = "SELECT * FROM compliance_exceptions WHERE loan_number = @ln ORDER BY requested_dt DESC";
            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String));

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapExceptionFromRow(row));
            }

            return results;
        }

        /// <summary>
        /// Gets all pending exceptions across all loans.
        /// </summary>
        public List<ComplianceExceptionData> GetPendingExceptions()
        {
            List<ComplianceExceptionData> results = new List<ComplianceExceptionData>();

            string sql = "SELECT * FROM compliance_exceptions WHERE approval_status = 'PENDING' ORDER BY requested_dt";
            DataTable dt = _db.ExecuteComplianceQuery(sql);

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapExceptionFromRow(row));
            }

            return results;
        }

        #endregion

        #region Mapping

        private ComplianceExceptionData MapExceptionFromRow(DataRow row)
        {
            ComplianceExceptionData e = new ComplianceExceptionData();
            e.ExceptionId = Convert.ToInt32(row["exception_id"]);
            e.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            e.ChkId = row["chk_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["chk_id"]);
            e.ExceptionType = row["exception_type"] == DBNull.Value ? null : row["exception_type"].ToString();
            e.ExceptionReason = row["exception_reason"] == DBNull.Value ? null : row["exception_reason"].ToString();
            e.RequestedBy = row["requested_by"] == DBNull.Value ? null : row["requested_by"].ToString();
            e.RequestedDt = row["requested_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["requested_dt"]);
            e.ApprovedBy = row["approved_by"] == DBNull.Value ? null : row["approved_by"].ToString();
            e.ApprovedDt = row["approved_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["approved_dt"]);
            e.ApprovalStatus = row["approval_status"] == DBNull.Value ? null : row["approval_status"].ToString();
            e.RiskAssessment = row["risk_assessment"] == DBNull.Value ? null : row["risk_assessment"].ToString();
            e.RiskLevel = row["risk_level"] == DBNull.Value ? null : row["risk_level"].ToString();
            e.ExpiryDt = row["expiry_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["expiry_dt"]);
            e.RegulatoryNotification = row["regulatory_notification"] == DBNull.Value ? null : row["regulatory_notification"].ToString();
            e.IsReportable = row["is_reportable"] != DBNull.Value && Convert.ToBoolean(row["is_reportable"]);
            return e;
        }

        #endregion
    }
}
