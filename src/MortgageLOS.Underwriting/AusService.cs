using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;
using MortgageLOS.Credit;

namespace MortgageLOS.Underwriting
{
    // =========================================================================
    // AusService - Automated Underwriting System
    //
    // Simulates integration with Fannie Mae DU, Freddie Mac LP, and USDA GUS.
    // In production, this would call the actual AUS APIs. In this demo, the
    // decision logic is simulated based on credit score, DTI, and LTV.
    //
    // TODO: Add support for LP (Loan Product Advisor) - 2010 (never completed)
    // TODO: Add support for GUS batch mode - 2017
    // =========================================================================

    #region Models

    public class AusResultData
    {
        public int AusId { get; set; }
        public string LoanNumber { get; set; }
        public string Engine { get; set; }
        public string Result { get; set; }
        public List<string> Findings { get; set; } = new List<string>();
        public DateTime RunDt { get; set; }
        public string RunBy { get; set; }
        public int CreditScore { get; set; }
        public decimal Dti { get; set; }
        public decimal Ltv { get; set; }
    }

    public class AusFinding
    {
        public string FindingCode { get; set; }
        public string FindingDesc { get; set; }
        public string Severity { get; set; }
        public bool IsCondition { get; set; }
        public string ConditionText { get; set; }
    }

    #endregion

    public class AusService
    {
        private readonly DatabaseHelper _db;
        private readonly CreditReportService _creditService;
        private readonly DtiCalculator _dtiCalculator;

        public AusService()
        {
            _db = new DatabaseHelper();
            _creditService = new CreditReportService();
            _dtiCalculator = new DtiCalculator();
        }

        public AusService(DatabaseHelper db)
        {
            _db = db;
            _creditService = new CreditReportService();
            _dtiCalculator = new DtiCalculator();
        }

        /// <summary>
        /// Runs the Automated Underwriting System for a loan.
        /// </summary>
        public AusResultData RunAus(string loanNumber, string engine)
        {
            if (string.IsNullOrEmpty(loanNumber))
                throw new ArgumentException("Loan number is required");

            EnsureAusTablesExist();

            // WORKAROUND: Race condition - two underwriters could submit the same loan
            // for AUS at the same time. We check if AUS was already run today and return
            // the existing result. Not perfect but reduces the window. - J. Martinez, 2013
            string checkSql = "SELECT COUNT(*) FROM aus_results WHERE loan_number = @loanNumber AND run_dt::date = CURRENT_DATE";
            object existing = _db.ExecuteCoreScalar(checkSql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
            if (existing != null && Convert.ToInt32(existing) > 0)
            {
                FileLogger.Info("Underwriting", "AUS already run today for " + loanNumber + ", returning existing result");
                return GetAusResult(loanNumber);
            }

            // Get loan data
            LoanData loan = GetLoanData(loanNumber);
            if (loan == null)
                throw new Exception("Loan not found: " + loanNumber);

            // Get credit score
            int creditScore = 0;
            CreditReportData creditReport = _creditService.GetCreditReport(loanNumber);
            if (creditReport != null && creditReport.RepresentativeScore.HasValue)
            {
                creditScore = creditReport.RepresentativeScore.Value;
            }
            else if (loan.BorrowerCreditScore.HasValue)
            {
                creditScore = loan.BorrowerCreditScore.Value;
            }

            // Get DTI
            decimal dti = 0m;
            DtiResultData dtiResult = _dtiCalculator.GetDtiResult(loanNumber);
            if (dtiResult != null && dtiResult.BackEndDti.HasValue)
            {
                dti = dtiResult.BackEndDti.Value;
            }
            else if (loan.Dti.HasValue)
            {
                dti = loan.Dti.Value;
            }

            // Get LTV
            decimal ltv = loan.Ltv ?? loan.CalculatedLtv;

            // Get product guidelines
            LoanProduct product = GetProduct(loan.ProductId ?? 0);
            int minFico = product?.MinFico ?? 620;
            decimal maxLtv = product?.MaxLtv ?? 97.0m;
            decimal maxDti = product?.MaxDti ?? 50.0m;

            // Evaluate AUS - overly complex nested if-else (this is how the original was written)
            string result;
            List<AusFinding> findings = new List<AusFinding>();

            if (creditScore >= minFico)
            {
                if (dti <= maxDti)
                {
                    if (ltv <= maxLtv)
                    {
                        result = AusResult.APPROVE_ELIGIBLE;
                    }
                    else if (ltv <= maxLtv * 1.05m)
                    {
                        result = AusResult.REFER_ELIGIBLE;
                        findings.Add(CreateFinding("LTV_HIGH", "LTV slightly exceeds guideline", "WARNING", true, "Verify LTV and obtain MI certificate"));
                    }
                    else
                    {
                        result = AusResult.REFER_INELIGIBLE;
                        findings.Add(CreateFinding("LTV_EXCEEDS", "LTV exceeds maximum guideline", "ERROR", true, "Reduce LTV or obtain MI"));
                    }
                }
                else if (dti <= maxDti * 1.10m)
                {
                    if (ltv <= maxLtv)
                    {
                        result = AusResult.REFER_ELIGIBLE;
                        findings.Add(CreateFinding("DTI_HIGH", "DTI slightly exceeds guideline", "WARNING", true, "Verify income and liabilities"));
                    }
                    else
                    {
                        result = AusResult.REFER_WITH_CAUTION;
                        findings.Add(CreateFinding("DTI_LTV_HIGH", "Both DTI and LTV exceed guidelines", "WARNING", true, "Manual underwriting review required"));
                    }
                }
                else
                {
                    result = AusResult.REFER_INELIGIBLE;
                    findings.Add(CreateFinding("DTI_EXCEEDS", "DTI exceeds maximum guideline", "ERROR", true, "DTI must be reduced"));
                }
            }
            else if (creditScore >= minFico - 40)
            {
                // Borderline credit
                if (dti <= maxDti && ltv <= maxLtv)
                {
                    result = AusResult.REFER_ELIGIBLE;
                    findings.Add(CreateFinding("CREDIT_BORDERLINE", "Credit score is below minimum but within tolerance", "WARNING", true, "Manual credit review required"));
                }
                else
                {
                    result = AusResult.REFER_WITH_CAUTION;
                    findings.Add(CreateFinding("CREDIT_RISK", "Credit score below minimum with elevated DTI/LTV", "ERROR", true, "Comprehensive manual review required"));
                }
            }
            else
            {
                if (creditScore < 500)
                {
                    result = AusResult.OUT_OF_SCOPE;
                    findings.Add(CreateFinding("CREDIT_TOO_LOW", "Credit score below AUS minimum (500)", "ERROR", false, null));
                }
                else
                {
                    result = AusResult.REFER_INELIGIBLE;
                    findings.Add(CreateFinding("CREDIT_INELIGIBLE", "Credit score does not meet minimum requirement", "ERROR", true, "Improve credit profile"));
                }
            }

            // Save result
            string insertSql = @"INSERT INTO aus_results (loan_number, engine, result, findings, run_dt, run_by, credit_score, dti, ltv)
                VALUES (@loanNumber, @engine, @result, @findings, @now, @runBy, @creditScore, @dti, @ltv) RETURNING aus_id";

            string findingsText = findings.Count > 0 ? string.Join("\n", findings.ConvertAll(f => f.FindingDesc)) : "";

            object ausIdObj = _db.ExecuteCoreScalar(insertSql,
                _db.CreateParam("@loanNumber", loanNumber, DbType.String),
                _db.CreateParam("@engine", engine, DbType.String),
                _db.CreateParam("@result", result, DbType.String),
                _db.CreateParam("@findings", findingsText, DbType.String),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@runBy", "AUS_SYSTEM", DbType.String),
                _db.CreateParam("@creditScore", creditScore, DbType.Int32),
                _db.CreateParam("@dti", dti, DbType.Decimal),
                _db.CreateParam("@ltv", ltv, DbType.Decimal));

            int ausId = ausIdObj != null ? Convert.ToInt32(ausIdObj) : 0;

            // Save findings
            foreach (AusFinding finding in findings)
            {
                string fSql = @"INSERT INTO aus_findings (aus_id, loan_number, finding_code, finding_desc, severity, is_condition, condition_text)
                    VALUES (@ausId, @loanNumber, @code, @desc, @severity, @isCondition, @conditionText)";
                _db.ExecuteCoreNonQuery(fSql,
                    _db.CreateParam("@ausId", ausId, DbType.Int32),
                    _db.CreateParam("@loanNumber", loanNumber, DbType.String),
                    _db.CreateParam("@code", finding.FindingCode, DbType.String),
                    _db.CreateParam("@desc", finding.FindingDesc, DbType.String),
                    _db.CreateParam("@severity", finding.Severity, DbType.String),
                    _db.CreateParam("@isCondition", finding.IsCondition, DbType.Boolean),
                    _db.CreateParam("@conditionText", (object)finding.ConditionText ?? DBNull.Value, DbType.String));
            }

            FileLogger.Info("Underwriting", "AUS result for " + loanNumber + ": " + result + " (Score: " + creditScore + ", DTI: " + dti + ", LTV: " + ltv + ")");

            return new AusResultData
            {
                AusId = ausId,
                LoanNumber = loanNumber,
                Engine = engine,
                Result = result,
                Findings = findings.ConvertAll(f => f.FindingDesc),
                RunDt = DateTime.Now,
                RunBy = "AUS_SYSTEM",
                CreditScore = creditScore,
                Dti = dti,
                Ltv = ltv
            };
        }

        public AusResultData GetAusResult(string loanNumber)
        {
            try
            {
                string sql = "SELECT * FROM aus_results WHERE loan_number = @loanNumber ORDER BY run_dt DESC LIMIT 1";
                DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
                if (dt.Rows.Count == 0)
                    return null;

                DataRow row = dt.Rows[0];
                AusResultData result = new AusResultData();
                result.AusId = Convert.ToInt32(row["aus_id"]);
                result.LoanNumber = row["loan_number"]?.ToString();
                result.Engine = row["engine"]?.ToString();
                result.Result = row["result"]?.ToString();
                result.RunDt = Convert.ToDateTime(row["run_dt"]);
                result.RunBy = row["run_by"] == DBNull.Value ? null : row["run_by"].ToString();
                result.CreditScore = row["credit_score"] == DBNull.Value ? 0 : Convert.ToInt32(row["credit_score"]);
                result.Dti = row["dti"] == DBNull.Value ? 0m : Convert.ToDecimal(row["dti"]);
                result.Ltv = row["ltv"] == DBNull.Value ? 0m : Convert.ToDecimal(row["ltv"]);

                // Load findings
                result.Findings = new List<string>();
                List<AusFinding> findings = GetAusFindings(loanNumber);
                foreach (AusFinding f in findings)
                {
                    result.Findings.Add(f.FindingDesc);
                }

                return result;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Underwriting", "GetAusResult failed for " + loanNumber, ex);
                return null;
            }
        }

        public List<AusFinding> GetAusFindings(string loanNumber)
        {
            List<AusFinding> findings = new List<AusFinding>();
            try
            {
                string sql = "SELECT * FROM aus_findings WHERE loan_number = @loanNumber ORDER BY finding_id";
                DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));

                foreach (DataRow row in dt.Rows)
                {
                    AusFinding f = new AusFinding();
                    f.FindingCode = row["finding_code"] == DBNull.Value ? null : row["finding_code"].ToString();
                    f.FindingDesc = row["finding_desc"] == DBNull.Value ? null : row["finding_desc"].ToString();
                    f.Severity = row["severity"] == DBNull.Value ? null : row["severity"].ToString();
                    f.IsCondition = row["is_condition"] == DBNull.Value ? false : Convert.ToBoolean(row["is_condition"]);
                    f.ConditionText = row["condition_text"] == DBNull.Value ? null : row["condition_text"].ToString();
                    findings.Add(f);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Underwriting", "GetAusFindings failed for " + loanNumber, ex);
            }
            return findings;
        }

        public string SelectEngine(string loanType)
        {
            if (string.IsNullOrEmpty(loanType))
                return AusEngine.DU;

            switch (loanType)
            {
                case "USDA":
                    return AusEngine.GUS;
                case "JUMBO":
                case "NON_QM":
                    return AusEngine.MANUAL;
                default:
                    return AusEngine.DU;
            }
        }

        #region Private Helpers

        private void EnsureAusTablesExist()
        {
            _db.ExecuteCoreNonQuery(@"CREATE TABLE IF NOT EXISTS aus_results (
                aus_id SERIAL PRIMARY KEY,
                loan_number VARCHAR(20) NOT NULL,
                engine VARCHAR(20) NOT NULL,
                result VARCHAR(40) NOT NULL,
                findings TEXT,
                run_dt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                run_by VARCHAR(50),
                credit_score INT,
                dti NUMERIC(8,2),
                ltv NUMERIC(8,2))");

            _db.ExecuteCoreNonQuery(@"CREATE TABLE IF NOT EXISTS aus_findings (
                finding_id SERIAL PRIMARY KEY,
                aus_id INT NOT NULL,
                loan_number VARCHAR(20) NOT NULL,
                finding_code VARCHAR(30),
                finding_desc TEXT,
                severity VARCHAR(10),
                is_condition BOOLEAN DEFAULT FALSE,
                condition_text TEXT,
                created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP)");
        }

        private LoanData GetLoanData(string loanNumber)
        {
            string sql = "SELECT * FROM loans WHERE loan_number = @loanNumber";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@loanNumber", loanNumber, DbType.String));
            if (dt.Rows.Count == 0)
                return null;

            DataRow row = dt.Rows[0];
            return new LoanData
            {
                LoanId = Convert.ToInt32(row["loan_id"]),
                LoanNumber = row["loan_number"]?.ToString(),
                BorrowerCreditScore = row["borrower_credit_score"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["borrower_credit_score"]),
                ProductId = row["product_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["product_id"]),
                LoanAmount = row["loan_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(row["loan_amount"]),
                PropertyValue = row["property_value"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["property_value"]),
                Ltv = row["ltv"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["ltv"]),
                Dti = row["dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["dti"]),
                LoanStatus = row["loan_status"]?.ToString(),
                LoanPurpose = row["loan_purpose"] == DBNull.Value ? null : row["loan_purpose"].ToString()
            };
        }

        private LoanProduct GetProduct(int productId)
        {
            if (productId <= 0)
                return null;

            string sql = "SELECT * FROM loan_products WHERE product_id = @id";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@id", productId, DbType.Int32));
            if (dt.Rows.Count == 0)
                return null;

            DataRow row = dt.Rows[0];
            return new LoanProduct
            {
                ProductId = Convert.ToInt32(row["product_id"]),
                ProductType = row["product_type"]?.ToString(),
                MinFico = row["min_fico"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["min_fico"]),
                MaxLtv = row["max_ltv"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_ltv"]),
                MaxDti = row["max_dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_dti"])
            };
        }

        private AusFinding CreateFinding(string code, string desc, string severity, bool isCondition, string conditionText)
        {
            return new AusFinding
            {
                FindingCode = code,
                FindingDesc = desc,
                Severity = severity,
                IsCondition = isCondition,
                ConditionText = conditionText
            };
        }

        #endregion

        #region DEPRECATED METHODS

        // [Obsolete("Use RunAus with engine parameter")]
        // public AusResultData RunDu(string loanNumber)
        // {
        //     // Old method that only ran DU. Removed in 2015 when LP support was added.
        //     // Well, LP support was never actually added, but the method was renamed.
        // }

        // [Obsolete("LP integration was never completed. Use RunAus with MANUAL engine.")]
        // public AusResultData RunLp(string loanNumber)
        // {
        //     // TODO: Implement LP integration - 2010
        //     throw new NotSupportedException("LP integration not implemented");
        // }

        #endregion
    }
}
