using System;
using System.Data;
using System.Collections.Generic;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Compliance
{
    // =========================================================================
    // DisclosureService - Disclosure tracking
    //
    // HISTORY:
    //   2012-03-15  Frank D.          Original. Only tracked LE and CD.
    //   2014-06-20  J. Martinez        Added state disclosure rules support.
    //   2015-10-03  K. Thompson        Updated for TRID (was GFE/TIL before).
    //   2017-02-14  Sarah L.           Added e-sign consent tracking.
    //   2018-05-10  Sarah L.           Added TRID business day tracking on
    //                                  disclosures (denormalized from trid_timeline).
    //   2020-03-01  M. Patel           Added COVID-19 disclosure handling.
    //   2023-11-20  D. Osei            Added state disclosure rule checking.
    //
    // NOTE: This service tracks BOTH TRID disclosures (LE/CD) and state-specific
    // disclosures. The TRID disclosures are also tracked in trid_timeline via
    // TridService, which creates a data duplication issue. The disclosures table
    // has trid_business_days and trid_waiting_period_met columns that duplicate
    // data from trid_timeline. This was supposed to be fixed in 2018 but wasn't.
    // - Sarah, 2018
    //
    // TODO (2014): Add support for e-delivery vs mail delivery timing differences.
    // TODO (2018): De-duplicate TRID data between disclosures and trid_timeline.
    // TODO (2020): Remove COVID disclosure handling once guidance expires.
    // =========================================================================

    public class DisclosureService
    {
        private readonly DatabaseHelper _db;

        public DisclosureService()
        {
            _db = new DatabaseHelper();
        }

        public DisclosureService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
        }

        #region Create

        /// <summary>
        /// Creates a disclosure record for a loan.
        /// </summary>
        public int CreateDisclosure(string loanNumber, string disclosureType, string subtype,
            DateTime requiredDt, string preparedBy)
        {
            FileLogger.Info("DisclosureService", "Creating disclosure " + disclosureType + "/" + subtype + " for loan " + loanNumber);

            // 2014 style: parameterized query
            string sql = @"INSERT INTO disclosures
                (loan_number, disclosure_type, disclosure_subtype, required_dt,
                 prepared_by, created_dt)
                VALUES
                (@ln, @dt, @sub, @req, @prep, @now)
                RETURNING disclosure_id";

            int newId = Convert.ToInt32(_db.ExecuteComplianceScalar(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String),
                _db.CreateParam("@dt", disclosureType, DbType.String),
                _db.CreateParam("@sub", (object)subtype ?? DBNull.Value, DbType.String),
                _db.CreateParam("@req", requiredDt, DbType.DateTime),
                _db.CreateParam("@prep", (object)preparedBy ?? DBNull.Value, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime)
            ));

            FileLogger.Info("DisclosureService", "Created disclosure " + newId + " for loan " + loanNumber);
            return newId;
        }

        #endregion

        #region Send / Receive

        /// <summary>
        /// Marks a disclosure as sent.
        /// </summary>
        public bool SendDisclosure(int disclosureId, string sentBy, string deliveryMethod, string deliveryAddr)
        {
            FileLogger.Info("DisclosureService", "Sending disclosure " + disclosureId + " via " + deliveryMethod);

            string sql = @"UPDATE disclosures
                SET sent_dt = @now, sent_by = @by, delivery_method = @method,
                    delivery_addr = @addr
                WHERE disclosure_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@by", (object)sentBy ?? DBNull.Value, DbType.String),
                _db.CreateParam("@method", (object)deliveryMethod ?? DBNull.Value, DbType.String),
                _db.CreateParam("@addr", (object)deliveryAddr ?? DBNull.Value, DbType.String),
                _db.CreateParam("@id", disclosureId, DbType.Int32)
            );

            return rows > 0;
        }

        /// <summary>
        /// Marks a disclosure as received.
        /// </summary>
        public bool ReceiveDisclosure(int disclosureId)
        {
            FileLogger.Info("DisclosureService", "Marking disclosure " + disclosureId + " as received");

            string sql = @"UPDATE disclosures
                SET received_dt = @now
                WHERE disclosure_id = @id";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@id", disclosureId, DbType.Int32)
            );

            return rows > 0;
        }

        #endregion

        #region Retrieve

        /// <summary>
        /// Gets all disclosures for a loan.
        /// </summary>
        public List<DisclosureData> GetDisclosures(string loanNumber)
        {
            List<DisclosureData> results = new List<DisclosureData>();

            string sql = "SELECT * FROM disclosures WHERE loan_number = @ln ORDER BY required_dt";
            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String));

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapDisclosureFromRow(row));
            }

            return results;
        }

        /// <summary>
        /// Gets disclosures that have not yet been sent (pending).
        /// </summary>
        public List<DisclosureData> GetPendingDisclosures(string loanNumber)
        {
            List<DisclosureData> results = new List<DisclosureData>();

            string sql = "SELECT * FROM disclosures WHERE loan_number = @ln AND sent_dt IS NULL ORDER BY required_dt";
            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String));

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapDisclosureFromRow(row));
            }

            return results;
        }

        /// <summary>
        /// Gets disclosures that were sent after their required date (late).
        /// </summary>
        public List<DisclosureData> GetLateDisclosures(string loanNumber)
        {
            List<DisclosureData> results = new List<DisclosureData>();

            // 2012 style: string concatenation. This was the original code.
            // Frank D. wrote this. It was never updated to use parameters
            // because the loan number is validated upstream. Not ideal.
            // - Sarah, 2017
            string sql = "SELECT * FROM disclosures WHERE loan_number = '" + loanNumber.Replace("'", "''") +
                         "' AND sent_dt IS NOT NULL AND sent_dt > required_dt ORDER BY required_dt";

            DataTable dt = _db.ExecuteComplianceQuery(sql);

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapDisclosureFromRow(row));
            }

            return results;
        }

        #endregion

        #region State Disclosure Rules

        /// <summary>
        /// Retrieves state-specific disclosure rules from the database.
        /// Only returns active rules.
        /// </summary>
        public List<StateDisclosureRule> GetStateDisclosureRules(string stateCode)
        {
            List<StateDisclosureRule> results = new List<StateDisclosureRule>();

            string sql = "SELECT * FROM state_disclosure_rules WHERE state_code = @sc AND is_active = true ORDER BY rule_type";
            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@sc", stateCode, DbType.String));

            foreach (DataRow row in dt.Rows)
            {
                results.Add(MapStateRuleFromRow(row));
            }

            return results;
        }

        /// <summary>
        /// Checks which state disclosures are required for a loan and whether
        /// they have been sent. Returns a list of ComplianceCheckData, one per
        /// applicable state rule.
        ///
        /// 2012: This only checked CA, TX, NY (hardcoded list).
        /// 2015: Rewritten to use state_disclosure_rules table.
        /// 2023: Added loan type and loan purpose matching.
        /// </summary>
        public List<ComplianceCheckData> CheckStateDisclosures(string loanNumber, string stateCode,
            string loanType, string loanPurpose)
        {
            List<ComplianceCheckData> results = new List<ComplianceCheckData>();

            List<StateDisclosureRule> rules = GetStateDisclosureRules(stateCode);
            if (rules.Count == 0)
            {
                // No state-specific rules for this state. That's fine.
                FileLogger.Info("DisclosureService", "No state disclosure rules for state " + stateCode);
                return results;
            }

            List<DisclosureData> disclosures = GetDisclosures(loanNumber);

            foreach (StateDisclosureRule rule in rules)
            {
                // Check if this rule applies to the loan type
                bool appliesToLoanType = rule.AppliesToAllLoanTypes ||
                    (!string.IsNullOrEmpty(rule.LoanTypes) && rule.LoanTypes.Contains(loanType));
                if (!appliesToLoanType)
                    continue;

                // Check if this rule applies to the loan purpose
                bool appliesToPurpose = rule.AppliesToAllPurposes ||
                    (!string.IsNullOrEmpty(rule.LoanPurposes) && rule.LoanPurposes.Contains(loanPurpose));
                if (!appliesToPurpose)
                    continue;

                // HACK (2015): The LoanTypes/LoanPurposes fields are comma-separated
                // strings like "FHA,VA,USDA". Using Contains() is a crude match that
                // could false-positive (e.g. "FHA" would match "FHAINANCE"). This is
                // a known issue. The proper fix is to split and compare exactly.
                // K. Thompson acknowledged this but never fixed it. - Sarah, 2017
                // TODO (2015): Fix the loan type/purpose matching to use exact split.

                ComplianceCheckData check = new ComplianceCheckData();
                check.LoanNumber = loanNumber;
                check.ChkType = ComplianceCheckType.STATE;
                check.ChkSubtype = rule.RuleName;
                check.ChkDt = DateTime.Now;
                check.ChkBy = "SYSTEM";
                check.ChkVersion = "8.2.1";
                check.RuleSetId = "STATE-2024";

                // Check if the required disclosure has been sent
                // Match by required_document or rule_name against disclosure subtype
                bool found = false;
                bool isLate = false;
                foreach (DisclosureData disc in disclosures)
                {
                    // HACK (2023): Matching is fuzzy. We match on disclosure subtype
                    // containing the rule name or required document. This is imperfect.
                    if (!string.IsNullOrEmpty(rule.RequiredDocument) &&
                        !string.IsNullOrEmpty(disc.DisclosureSubtype) &&
                        disc.DisclosureSubtype.IndexOf(rule.RequiredDocument, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = true;
                        if (disc.IsLate)
                            isLate = true;
                        break;
                    }
                    if (!string.IsNullOrEmpty(rule.RuleName) &&
                        !string.IsNullOrEmpty(disc.DisclosureSubtype) &&
                        disc.DisclosureSubtype.IndexOf(rule.RuleName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = true;
                        if (disc.IsLate)
                            isLate = true;
                        break;
                    }
                }

                if (!found)
                {
                    check.ChkStatus = ComplianceStatus.FAIL;
                    check.ChkResult = "State disclosure '" + rule.RuleName + "' (" + rule.RegulationCitation +
                                      ") not found for loan. Required document: " + (rule.RequiredDocument ?? "N/A") +
                                      ". Timing: " + (rule.TimingRequirement ?? "N/A");
                }
                else if (isLate)
                {
                    check.ChkStatus = ComplianceStatus.WARNING;
                    check.ChkResult = "State disclosure '" + rule.RuleName + "' was sent late. Timing requirement: " +
                                      (rule.TimingRequirement ?? "N/A") + " (" + rule.RegulationCitation + ")";
                }
                else
                {
                    check.ChkStatus = ComplianceStatus.PASS;
                    check.ChkResult = "State disclosure '" + rule.RuleName + "' sent on time. " + rule.RegulationCitation;
                }

                results.Add(check);
            }

            return results;
        }

        #endregion

        #region Mapping

        private DisclosureData MapDisclosureFromRow(DataRow row)
        {
            DisclosureData d = new DisclosureData();
            d.DisclosureId = Convert.ToInt32(row["disclosure_id"]);
            d.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            d.DisclosureType = row["disclosure_type"] == DBNull.Value ? null : row["disclosure_type"].ToString();
            d.DisclosureSubtype = row["disclosure_subtype"] == DBNull.Value ? null : row["disclosure_subtype"].ToString();

            d.RequiredDt = row["required_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["required_dt"]);
            d.SentDt = row["sent_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["sent_dt"]);
            d.ReceivedDt = row["received_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["received_dt"]);

            d.DeliveryMethod = row["delivery_method"] == DBNull.Value ? null : row["delivery_method"].ToString();
            d.DeliveryAddr = row["delivery_addr"] == DBNull.Value ? null : row["delivery_addr"].ToString();

            d.TridBusinessDays = row["trid_business_days"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["trid_business_days"]);
            d.TridWaitingPeriodMet = row["trid_waiting_period_met"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(row["trid_waiting_period_met"]);
            d.TridCdWaitingMet = row["trid_cd_waiting_met"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(row["trid_cd_waiting_met"]);

            d.VersionNumber = row["version_number"] == DBNull.Value ? null : row["version_number"].ToString();
            d.PdfDocId = row["pdf_doc_id"] == DBNull.Value ? null : row["pdf_doc_id"].ToString();

            d.PreparedBy = row["prepared_by"] == DBNull.Value ? null : row["prepared_by"].ToString();
            d.SentBy = row["sent_by"] == DBNull.Value ? null : row["sent_by"].ToString();
            d.CreatedDt = row["created_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["created_dt"]);
            d.EsignConsentDt = row["esign_consent_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["esign_consent_dt"]);
            d.EsignConsentIp = row["esign_consent_ip"] == DBNull.Value ? null : row["esign_consent_ip"].ToString();

            return d;
        }

        private StateDisclosureRule MapStateRuleFromRow(DataRow row)
        {
            StateDisclosureRule r = new StateDisclosureRule();
            r.RuleId = Convert.ToInt32(row["rule_id"]);
            r.StateCode = row["state_code"] == DBNull.Value ? null : row["state_code"].ToString();
            r.RuleName = row["rule_name"] == DBNull.Value ? null : row["rule_name"].ToString();
            r.RuleType = row["rule_type"] == DBNull.Value ? null : row["rule_type"].ToString();
            r.LoanTypes = row["loan_types"] == DBNull.Value ? null : row["loan_types"].ToString();
            r.LoanPurposes = row["loan_purposes"] == DBNull.Value ? null : row["loan_purposes"].ToString();
            r.RuleDesc = row["rule_desc"] == DBNull.Value ? null : row["rule_desc"].ToString();
            r.RequiredDocument = row["required_document"] == DBNull.Value ? null : row["required_document"].ToString();
            r.TimingRequirement = row["timing_requirement"] == DBNull.Value ? null : row["timing_requirement"].ToString();
            r.TimingDays = row["timing_days"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["timing_days"]);
            r.MaxFeeAmount = row["max_fee_amount"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_fee_amount"]);
            r.MaxFeePct = row["max_fee_pct"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_fee_pct"]);
            r.IsActive = row["is_active"] != DBNull.Value && Convert.ToBoolean(row["is_active"]);
            r.EffectiveDt = row["effective_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["effective_dt"]);
            r.ExpiryDt = row["expiry_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["expiry_dt"]);
            r.RegulationCitation = row["regulation_citation"] == DBNull.Value ? null : row["regulation_citation"].ToString();
            r.AutomationScript = row["automation_script"] == DBNull.Value ? null : row["automation_script"].ToString();
            r.ReviewRequired = row["review_required"] != DBNull.Value && Convert.ToBoolean(row["review_required"]);
            r.ReviewFrequency = row["review_frequency"] == DBNull.Value ? null : row["review_frequency"].ToString();
            return r;
        }

        #endregion
    }
}
