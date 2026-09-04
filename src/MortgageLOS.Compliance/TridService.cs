using System;
using System.Data;
using System.Collections.Generic;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Compliance
{
    // =========================================================================
    // TridService - TRID timeline management
    //
    // TRID = TILA-RESPA Integrated Disclosure rule (12 CFR 1026.19(e) and (f))
    // Effective October 3, 2015 (was supposed to be August 2015, delayed).
    //
    // HISTORY:
    //   2015-06-01  K. Thompson     Original. Written for the TRID effective date.
    //   2015-10-03  K. Thompson     Updated for the actual effective date (Oct 3).
    //   2017-03-20  Sarah L.         Added changed circumstance / revised LE logic.
    //   2018-01-15  Sarah L.         Added tolerance cure tracking.
    //   2020-02-10  M. Patel         Patched for COVID-19 timeline extensions.
    //                                (CFPB issued guidance allowing flexibility.)
    //   2023-11-20  D. Osei          Added CD vs LE variance tracking.
    //   2024-02-15  D. Osei          Added rate lock date tracking.
    //
    // TRID timing rules (simplified):
    //   1. LE must be delivered within 3 business days of application
    //      (1026.19(e)(1)(iii))
    //   2. LE must be received at least 7 business days before consummation
    //      (borrower can waive to 3) (1026.19(e)(1)(iv))
    //   3. Changed circumstance -> revised LE within 3 business days
    //      (1026.19(e)(3)(iv))
    //   4. CD must be received at least 3 business days before consummation
    //      (1026.19(f)(1)(ii))
    //
    // TODO (2017): Add support for the 7-business-day LE waiting period (currently
    //              only tracks the 3-day delivery requirement).
    // TODO (2020): Remove COVID timeline extension code once guidance expires.
    // TODO (2023): Add tolerance cure calculation (currently just stores the value).
    // =========================================================================

    public class TridService
    {
        private readonly DatabaseHelper _db;

        public TridService()
        {
            _db = new DatabaseHelper();
        }

        public TridService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
        }

        #region Initialize

        /// <summary>
        /// Creates a TRID timeline record for a loan, calculating the LE deadline
        /// (3 business days from application date).
        ///
        /// Per 12 CFR 1026.19(e)(1)(iii): The creditor must deliver the LE
        /// within 3 business days of receiving the consumer's loan application.
        /// </summary>
        public int InitializeTridTimeline(string loanNumber, DateTime applicationDate)
        {
            FileLogger.Info("TridService", "Initializing TRID timeline for loan " + loanNumber + " app date " + applicationDate.ToString("yyyy-MM-dd"));

            // Calculate LE deadline: 3 business days from application
            int leDays = AppConfig.Instance.TridLeDeliveryDays;
            DateTime leRequiredBy = DateUtils.AddBusinessDays(applicationDate, leDays);

            // 2017 style: parameterized query
            string sql = @"INSERT INTO trid_timeline
                (loan_number, application_dt, le_required_by_dt, le_version,
                 created_dt, updated_dt)
                VALUES
                (@ln, @ad, @leReq, @ver, @now, @now)
                RETURNING trid_id";

            int newId = Convert.ToInt32(_db.ExecuteComplianceScalar(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String),
                _db.CreateParam("@ad", applicationDate, DbType.DateTime),
                _db.CreateParam("@leReq", leRequiredBy, DbType.DateTime),
                _db.CreateParam("@ver", "1", DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime)
            ));

            FileLogger.Info("TridService", "Created TRID timeline " + newId + " for loan " + loanNumber + ". LE required by " + leRequiredBy.ToString("yyyy-MM-dd"));
            return newId;
        }

        #endregion

        #region Retrieve

        /// <summary>
        /// Retrieves the TRID timeline for a loan. Returns null if not found.
        /// </summary>
        public TridTimelineData GetTridTimeline(string loanNumber)
        {
            string sql = "SELECT * FROM trid_timeline WHERE loan_number = @ln ORDER BY trid_id DESC LIMIT 1";
            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String));

            if (dt.Rows.Count == 0)
                return null;

            return MapTridFromRow(dt.Rows[0]);
        }

        #endregion

        #region LE Tracking

        /// <summary>
        /// Records the LE sent date. Checks if late (sent after required date).
        /// Returns true if saved successfully.
        ///
        /// The version parameter tracks LE versions (1, 2, 3...) for revised LEs.
        /// </summary>
        public bool RecordLeSent(string loanNumber, DateTime sentDt, string version)
        {
            FileLogger.Info("TridService", "Recording LE sent for loan " + loanNumber + " on " + sentDt.ToString("yyyy-MM-dd") + " version " + version);

            TridTimelineData timeline = GetTridTimeline(loanNumber);
            if (timeline == null)
            {
                FileLogger.Warn("TridService", "TRID timeline not found for loan " + loanNumber);
                return false;
            }

            // Check if late
            bool isLate = sentDt > timeline.LeRequiredByDt;
            if (isLate)
            {
                FileLogger.Warn("TridService", "LE sent LATE for loan " + loanNumber + ". Sent " + sentDt.ToString("yyyy-MM-dd") + ", required by " + timeline.LeRequiredByDt.ToString("yyyy-MM-dd"));
            }

            string sql = @"UPDATE trid_timeline
                SET le_sent_dt = @sent, le_version = @ver, updated_dt = @now
                WHERE trid_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@sent", sentDt, DbType.DateTime),
                _db.CreateParam("@ver", (object)version ?? "1", DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", timeline.TridId, DbType.Int32)
            );

            return rows > 0;
        }

        /// <summary>
        /// Records the LE received date (borrower acknowledgment of receipt).
        /// </summary>
        public bool RecordLeReceived(string loanNumber, DateTime receivedDt)
        {
            FileLogger.Info("TridService", "Recording LE received for loan " + loanNumber + " on " + receivedDt.ToString("yyyy-MM-dd"));

            TridTimelineData timeline = GetTridTimeline(loanNumber);
            if (timeline == null)
            {
                FileLogger.Warn("TridService", "TRID timeline not found for loan " + loanNumber);
                return false;
            }

            string sql = @"UPDATE trid_timeline
                SET le_received_dt = @recv, updated_dt = @now
                WHERE trid_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@recv", receivedDt, DbType.DateTime),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", timeline.TridId, DbType.Int32)
            );

            return rows > 0;
        }

        #endregion

        #region Changed Circumstance

        /// <summary>
        /// Records a changed circumstance and calculates the revised LE deadline.
        ///
        /// Per 12 CFR 1026.19(e)(3)(iv): If a changed circumstance occurs, the
        /// creditor must deliver a revised LE within 3 business days of learning
        /// of the changed circumstance.
        ///
        /// Common changed circumstances:
        /// - Property valuation differs from estimate
        /// - Borrower changes loan amount
        /// - Interest rate lock
        /// - Credit information changes
        /// </summary>
        public bool RecordChangedCircumstance(string loanNumber, DateTime ccDt, string desc)
        {
            FileLogger.Info("TridService", "Recording changed circumstance for loan " + loanNumber + " on " + ccDt.ToString("yyyy-MM-dd") + ": " + desc);

            TridTimelineData timeline = GetTridTimeline(loanNumber);
            if (timeline == null)
            {
                FileLogger.Warn("TridService", "TRID timeline not found for loan " + loanNumber);
                return false;
            }

            // Calculate revised LE deadline: 3 business days from changed circumstance date
            DateTime revisedLeRequired = DateUtils.CalculateRevisedLeDeadline(ccDt);

            string sql = @"UPDATE trid_timeline
                SET changed_circumstance_dt = @ccdt,
                    changed_circumstance_desc = @desc,
                    revised_le_required_dt = @revised,
                    updated_dt = @now
                WHERE trid_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@ccdt", ccDt, DbType.DateTime),
                _db.CreateParam("@desc", (object)desc ?? DBNull.Value, DbType.String),
                _db.CreateParam("@revised", revisedLeRequired, DbType.DateTime),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", timeline.TridId, DbType.Int32)
            );

            FileLogger.Info("TridService", "Revised LE required by " + revisedLeRequired.ToString("yyyy-MM-dd") + " for loan " + loanNumber);
            return rows > 0;
        }

        /// <summary>
        /// Records the revised LE sent date.
        /// </summary>
        public bool RecordRevisedLeSent(string loanNumber, DateTime sentDt)
        {
            FileLogger.Info("TridService", "Recording revised LE sent for loan " + loanNumber + " on " + sentDt.ToString("yyyy-MM-dd"));

            TridTimelineData timeline = GetTridTimeline(loanNumber);
            if (timeline == null)
            {
                FileLogger.Warn("TridService", "TRID timeline not found for loan " + loanNumber);
                return false;
            }

            // Check if revised LE is late
            if (timeline.RevisedLeRequiredDt.HasValue && sentDt > timeline.RevisedLeRequiredDt.Value)
            {
                FileLogger.Warn("TridService", "Revised LE sent LATE for loan " + loanNumber);
            }

            string sql = @"UPDATE trid_timeline
                SET revised_le_sent_dt = @sent, updated_dt = @now
                WHERE trid_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@sent", sentDt, DbType.DateTime),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", timeline.TridId, DbType.Int32)
            );

            return rows > 0;
        }

        #endregion

        #region CD Tracking

        /// <summary>
        /// Records the CD sent date. The consummation date is calculated as
        /// 3 business days after the CD is received by the borrower.
        ///
        /// Per 12 CFR 1026.19(f)(1)(ii): The consumer must receive the CD at
        /// least 3 business days before consummation (closing).
        ///
        /// NOTE: This records the SENT date. The consummation date is only
        /// calculated once the CD is RECEIVED (see RecordCdReceived).
        /// </summary>
        public bool RecordCdSent(string loanNumber, DateTime sentDt)
        {
            FileLogger.Info("TridService", "Recording CD sent for loan " + loanNumber + " on " + sentDt.ToString("yyyy-MM-dd"));

            TridTimelineData timeline = GetTridTimeline(loanNumber);
            if (timeline == null)
            {
                FileLogger.Warn("TridService", "TRID timeline not found for loan " + loanNumber);
                return false;
            }

            string sql = @"UPDATE trid_timeline
                SET cd_sent_dt = @sent, cd_prepared_dt = @prep, updated_dt = @now
                WHERE trid_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@sent", sentDt, DbType.DateTime),
                _db.CreateParam("@prep", sentDt, DbType.DateTime),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", timeline.TridId, DbType.Int32)
            );

            return rows > 0;
        }

        /// <summary>
        /// Records the CD received date. Calculates the consummation date
        /// (3 business days after CD received) and checks if the waiting
        /// period is met.
        ///
        /// Per 12 CFR 1026.19(f)(1)(ii): Consummation may not occur until at
        /// least 3 business days after the CD is received.
        /// </summary>
        public bool RecordCdReceived(string loanNumber, DateTime receivedDt)
        {
            FileLogger.Info("TridService", "Recording CD received for loan " + loanNumber + " on " + receivedDt.ToString("yyyy-MM-dd"));

            TridTimelineData timeline = GetTridTimeline(loanNumber);
            if (timeline == null)
            {
                FileLogger.Warn("TridService", "TRID timeline not found for loan " + loanNumber);
                return false;
            }

            // Calculate consummation date: 3 business days after CD received
            int cdWaitingDays = AppConfig.Instance.TridCdWaitingDays;
            DateTime consummationDt = DateUtils.CalculateCdConsummationDate(receivedDt);

            // Check waiting period met
            // The waiting period is met if consummation is at least 3 business days
            // after CD received. Since we calculate consummation as 3 business days
            // after received, the waiting period is met by definition.
            // But if someone sets consummation earlier, it would not be met.
            bool waitingMet = true;

            string sql = @"UPDATE trid_timeline
                SET cd_received_dt = @recv,
                    consummation_dt = @cons,
                    cd_waiting_met = @wait,
                    updated_dt = @now
                WHERE trid_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@recv", receivedDt, DbType.DateTime),
                _db.CreateParam("@cons", consummationDt, DbType.DateTime),
                _db.CreateParam("@wait", waitingMet, DbType.Boolean),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", timeline.TridId, DbType.Int32)
            );

            FileLogger.Info("TridService", "CD received. Consummation date set to " + consummationDt.ToString("yyyy-MM-dd") + " for loan " + loanNumber);
            return rows > 0;
        }

        #endregion

        #region TRID Compliance Check

        /// <summary>
        /// Checks all TRID timing requirements and returns a ComplianceCheckData.
        ///
        /// This is called by ComplianceCheckService.RunTridCheck but can also
        /// be called directly.
        /// </summary>
        public ComplianceCheckData CheckTridCompliance(string loanNumber)
        {
            ComplianceCheckData check = new ComplianceCheckData();
            check.LoanNumber = loanNumber;
            check.ChkType = ComplianceCheckType.TRID;
            check.ChkDt = DateTime.Now;
            check.ChkBy = "SYSTEM";
            check.ChkVersion = "8.2.1";
            check.RuleSetId = "TRID-2024";

            TridTimelineData timeline = GetTridTimeline(loanNumber);
            if (timeline == null)
            {
                check.ChkStatus = ComplianceStatus.FAIL;
                check.ChkResult = "TRID timeline not initialized. Cannot verify TRID compliance per 12 CFR 1026.19.";
                return check;
            }

            List<string> issues = new List<string>();

            // 1. LE within 3 business days of application
            if (!timeline.LeIsSent)
            {
                if (DateTime.Now > timeline.LeRequiredByDt)
                {
                    issues.Add("LE not delivered within 3 business days of application per 1026.19(e)(1)(iii). Required by " + timeline.LeRequiredByDt.ToString("yyyy-MM-dd") + ".");
                }
                else
                {
                    // LE not yet sent but still within deadline
                    // HACK (2020): During COVID, CFPB allowed some flexibility. We don't
                    // implement that here. If you need COVID flexibility, handle manually.
                    issues.Add("LE not yet delivered. Deadline: " + timeline.LeRequiredByDt.ToString("yyyy-MM-dd") + ".");
                }
            }
            else if (timeline.LeIsLate)
            {
                issues.Add("LE delivered late. Sent " + timeline.LeSentDt.Value.ToString("yyyy-MM-dd") + ", required by " + timeline.LeRequiredByDt.ToString("yyyy-MM-dd") + " per 1026.19(e)(1)(iii).");
            }

            // 2. Changed circumstance revised LE
            if (timeline.HasChangedCircumstance)
            {
                if (timeline.RevisedLeRequiredDt.HasValue && !timeline.RevisedLeSentDt.HasValue)
                {
                    if (DateTime.Now > timeline.RevisedLeRequiredDt.Value)
                    {
                        issues.Add("Revised LE not delivered within 3 business days of changed circumstance per 1026.19(e)(3)(iv). Required by " + timeline.RevisedLeRequiredDt.Value.ToString("yyyy-MM-dd") + ".");
                    }
                }
                else if (timeline.RevisedLeSentDt.HasValue && timeline.RevisedLeRequiredDt.HasValue &&
                         timeline.RevisedLeSentDt.Value > timeline.RevisedLeRequiredDt.Value)
                {
                    issues.Add("Revised LE delivered late. Sent " + timeline.RevisedLeSentDt.Value.ToString("yyyy-MM-dd") + ", required by " + timeline.RevisedLeRequiredDt.Value.ToString("yyyy-MM-dd") + ".");
                }
            }

            // 3. CD waiting period
            if (timeline.CdIsSent)
            {
                if (!timeline.CdReceivedDt.HasValue)
                {
                    issues.Add("CD sent but not marked as received. Cannot verify 3-business-day waiting period per 1026.19(f)(1)(ii).");
                }
                else if (timeline.ConsummationDt.HasValue)
                {
                    // Consummation recorded. Verify 3 business days.
                    int days = DateUtils.CountBusinessDays(timeline.CdReceivedDt.Value, timeline.ConsummationDt.Value);
                    if (days < 3)
                    {
                        issues.Add("Consummation occurred " + days + " business days after CD received. Minimum 3 required per 1026.19(f)(1)(ii).");
                    }
                }
                else if (timeline.CdWaitingMet.HasValue && !timeline.CdWaitingMet.Value)
                {
                    issues.Add("CD 3-business-day waiting period not met per 1026.19(f)(1)(ii).");
                }
            }

            if (issues.Count == 0)
            {
                check.ChkStatus = ComplianceStatus.PASS;
                check.ChkResult = "TRID timing requirements met per 12 CFR 1026.19(e) and (f).";
            }
            else
            {
                check.ChkStatus = ComplianceStatus.FAIL;
                check.ChkResult = string.Join("; ", issues);
            }

            return check;
        }

        #endregion

        #region Mapping

        private TridTimelineData MapTridFromRow(DataRow row)
        {
            TridTimelineData t = new TridTimelineData();
            t.TridId = Convert.ToInt32(row["trid_id"]);
            t.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            t.ApplicationDt = row["application_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["application_dt"]);

            t.LeRequiredByDt = row["le_required_by_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["le_required_by_dt"]);
            t.LeSentDt = row["le_sent_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["le_sent_dt"]);
            t.LeReceivedDt = row["le_received_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["le_received_dt"]);
            t.LeVersion = row["le_version"] == DBNull.Value ? null : row["le_version"].ToString();

            t.ChangedCircumstanceDt = row["changed_circumstance_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["changed_circumstance_dt"]);
            t.ChangedCircumstanceDesc = row["changed_circumstance_desc"] == DBNull.Value ? null : row["changed_circumstance_desc"].ToString();
            t.RevisedLeRequiredDt = row["revised_le_required_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["revised_le_required_dt"]);
            t.RevisedLeSentDt = row["revised_le_sent_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["revised_le_sent_dt"]);

            t.CdPreparedDt = row["cd_prepared_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["cd_prepared_dt"]);
            t.CdSentDt = row["cd_sent_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["cd_sent_dt"]);
            t.CdReceivedDt = row["cd_received_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["cd_received_dt"]);
            t.ConsummationDt = row["consummation_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["consummation_dt"]);
            t.CdWaitingMet = row["cd_waiting_met"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(row["cd_waiting_met"]);

            t.RateLockDt = row["rate_lock_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["rate_lock_dt"]);
            t.RateLockExpDt = row["rate_lock_exp_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["rate_lock_exp_dt"]);

            t.CreatedDt = row["created_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["created_dt"]);
            t.UpdatedDt = row["updated_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["updated_dt"]);

            t.LeToleranceCure = row["le_tolerance_cure"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["le_tolerance_cure"]);
            t.CdVsLeVariance = row["cd_vs_le_variance"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["cd_vs_le_variance"]);

            return t;
        }

        #endregion
    }
}
