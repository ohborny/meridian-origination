using System;
using System.Data;
using System.Collections.Generic;
using System.Text;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Compliance
{
    // =========================================================================
    // HmdaService - HMDA data management
    //
    // HISTORY:
    //   2012-03-15  Frank D.          Original. Used OLD race/ethnicity/sex fields.
    //   2014-01-20  J. Martinez        Migrated to Npgsql.
    //   2017-08-14  Sarah L.           Added HMDA 2018 prep (new fields).
    //   2018-01-10  Sarah L.           Full HMDA 2018 rule implementation.
    //                                  New race/ethnicity/sex format ( disaggregated).
    //                                  Added dwelling type, total units, AUS fields.
    //   2020-04-01  M. Patel           Added COVID forbearance fields.
    //   2023-11-20  D. Osei            Added 2023 HMDA fields (reverse mortgage,
    //                                  open-end credit, business purpose).
    //   2024-02-15  D. Osei            Patched LAR generation for 2024 filing.
    //
    // HMDA (Home Mortgage Disclosure Act) - 12 CFR 1003 (Regulation C)
    // The HMDA rule changed dramatically for data collected in 2018+.
    // The old format had 1 race field, 1 ethnicity field, 1 sex field.
    // The new format has disaggregated race/ethnicity (up to 5 each) and
    // separately collected vs observed.
    //
    // THIS IS WHY WE HAVE BOTH OLD AND NEW FIELDS IN THE TABLE.
    // The old fields (race_old, ethnicity_old, sex_old) are kept for historical
    // data. The new fields (race_new, ethnicity_new, sex_new) are used for
    // 2018+ data. DO NOT mix them. DO NOT write to old fields for new data.
    //
    // TODO (2017): Migrate old race/ethnicity/sex data to new format.
    //              (Never done. Old data stays in old fields.)
    // TODO (2020): Remove COVID temporary fields once reporting ends.
    //              (CFPB made it permanent. Still here.)
    // TODO (2023): Add co-applicant race/ethnicity/sex to LAR generation.
    //              (Currently broken. See comment in GenerateHmdaLar.)
    // =========================================================================

    public class HmdaService
    {
        private readonly DatabaseHelper _db;

        public HmdaService()
        {
            _db = new DatabaseHelper();
        }

        public HmdaService(AppConfig config)
        {
            _db = new DatabaseHelper(config);
        }

        #region Create

        /// <summary>
        /// Creates a HMDA record using the NEW (2018+) race/ethnicity/sex fields.
        ///
        /// The race/ethnicity/sex values should be in the new disaggregated format
        /// (pipe-separated codes, e.g. "1|2|3" for multiple races).
        ///
        /// collectionMethod: "VISUAL" or "NONVISUAL" - determines race_observed/sex_observed.
        ///   VISUAL = loan officer observed race/sex (sets observed fields)
        ///   NONVISUAL = borrower self-reported (observed fields are null/empty)
        ///
        /// NOTE: This writes to the NEW fields only. Old fields are left null.
        /// Old data (pre-2018) should stay in old fields. Do not backfill.
        /// </summary>
        public int CreateHmdaRecord(string loanNumber, string race, string ethnicity, string sex,
            string raceObserved, string sexObserved, int? incomeAmount, string collectionMethod,
            string createdBy)
        {
            FileLogger.Info("HmdaService", "Creating HMDA record for loan " + loanNumber);

            // Determine observed values based on collection method
            // 2018 rule: if collected via visual observation, set observed fields.
            // If non-visual (self-reported), observed fields should be "3" (not applicable)
            // or empty. We use "3" per CFPB guidance.
            string observedRace = raceObserved;
            string observedSex = sexObserved;
            if (string.IsNullOrEmpty(collectionMethod))
            {
                collectionMethod = "NONVISUAL";
            }
            if (collectionMethod.ToUpper() == "VISUAL")
            {
                // Loan officer observed - set observed fields to the observed values
                if (string.IsNullOrEmpty(observedRace))
                    observedRace = race;
                if (string.IsNullOrEmpty(observedSex))
                    observedSex = sex;
            }
            else
            {
                // Self-reported - observed should be "3" (not applicable) per 12 CFR 1003.4(a)(10)
                observedRace = "3";
                observedSex = "3";
            }

            int reportingYear = AppConfig.Instance.HmdaReportingYear;

            // 2018+ style: parameterized query
            string sql = @"INSERT INTO hmda_data
                (loan_number, reporting_year, race_new, ethnicity_new, sex_new,
                 race_observed, sex_observed, income_amount, created_dt, updated_dt, created_by)
                VALUES
                (@ln, @yr, @rn, @en, @sn, @ro, @so, @ia, @cd, @ud, @cb)
                RETURNING hmda_id";

            int newId = Convert.ToInt32(_db.ExecuteComplianceScalar(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String),
                _db.CreateParam("@yr", reportingYear, DbType.Int32),
                _db.CreateParam("@rn", (object)race ?? DBNull.Value, DbType.String),
                _db.CreateParam("@en", (object)ethnicity ?? DBNull.Value, DbType.String),
                _db.CreateParam("@sn", (object)sex ?? DBNull.Value, DbType.String),
                _db.CreateParam("@ro", (object)observedRace ?? DBNull.Value, DbType.String),
                _db.CreateParam("@so", (object)observedSex ?? DBNull.Value, DbType.String),
                _db.CreateParam("@ia", (object)incomeAmount ?? DBNull.Value, DbType.Int32),
                _db.CreateParam("@cd", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@ud", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@cb", (object)createdBy ?? DBNull.Value, DbType.String)
            ));

            FileLogger.Info("HmdaService", "Created HMDA record " + newId + " for loan " + loanNumber);
            return newId;
        }

        #endregion

        #region Retrieve

        /// <summary>
        /// Retrieves HMDA data for a loan. Returns null if not found.
        /// </summary>
        public HmdaData GetHmdaData(string loanNumber)
        {
            string sql = "SELECT * FROM hmda_data WHERE loan_number = @ln ORDER BY hmda_id DESC LIMIT 1";
            DataTable dt = _db.ExecuteComplianceQuery(sql,
                _db.CreateParam("@ln", loanNumber, DbType.String));

            if (dt.Rows.Count == 0)
                return null;

            return MapHmdaFromRow(dt.Rows[0]);
        }

        #endregion

        #region Update Action

        /// <summary>
        /// Updates the action_taken and action_taken_dt when a loan status changes.
        /// Maps loan status to HMDA action taken codes.
        ///
        /// Loan Status -> HMDA Action mapping:
        ///   FUNDED / PURCHASED -> 1 (Originated)
        ///   DENIED              -> 3 (Denied)
        ///   WITHDRAWN           -> 4 (Withdrawn)
        ///   CANCELLED           -> 5 (Closed for incompleteness)
        ///
        /// NOTE: APPROVED_NOT_ACCEPTED (2) is not auto-mapped. It requires manual
        /// entry because we can't distinguish "approved but not accepted" from
        /// "approved and originated" in our system. - Sarah, 2018
        /// </summary>
        public bool UpdateHmdaAction(string loanNumber, string actionTaken, DateTime actionDt)
        {
            // 2014 style: parameterized update
            string sql = @"UPDATE hmda_data
                SET action_taken = @at, action_taken_dt = @ad, updated_dt = @now
                WHERE loan_number = @ln";

            int rows = _db.ExecuteComplianceNonQuery(sql,
                _db.CreateParam("@at", actionTaken, DbType.String),
                _db.CreateParam("@ad", actionDt, DbType.DateTime),
                _db.CreateParam("@now", DateTime.Now, DbType.DateTime),
                _db.CreateParam("@ln", loanNumber, DbType.String)
            );

            FileLogger.Info("HmdaService", "Updated HMDA action for loan " + loanNumber + " to " + actionTaken);
            return rows > 0;
        }

        #endregion

        #region LAR Generation

        /// <summary>
        /// Generates HMDA LAR (Loan Application Register) data as CSV rows for filing.
        ///
        /// Each row is a string[] representing one column in the LAR.
        /// The columns follow the 2018+ HMDA LAR format per the CFPB Filing
        /// Instructions Guide.
        ///
        /// KNOWN BUG (2023): This does NOT handle co-applicant race/ethnicity/sex
        /// correctly. The HMDA LAR requires separate co-applicant demographic
        /// fields, but our hmda_data table only stores applicant demographics.
        /// Co-applicant data would need to come from the customer database, which
        /// we don't query here. As a result, co-applicant race/ethnicity/sex are
        /// always reported as "8" (no co-applicant) even when there IS a co-applicant.
        ///
        /// TODO (2023): Fix co-applicant race reporting. - D. Osei
        /// TODO (2020): Handle COVID forbearance column correctly.
        /// </summary>
        public List<string[]> GenerateHmdaLar(int reportingYear)
        {
            FileLogger.Info("HmdaService", "Generating HMDA LAR for reporting year " + reportingYear);

            List<string[]> rows = new List<string[]>();

            // 2012 style: string concatenation for SQL. This is the original code
            // from Frank D. It was never updated to use parameters because the
            // reporting year is an integer (not user input) and nobody felt it
            // was worth the risk to change. - Sarah, 2017
            string sql = "SELECT * FROM hmda_data WHERE reporting_year = " + reportingYear +
                         " ORDER BY loan_number";

            DataTable dt = _db.ExecuteComplianceQuery(sql);

            string institutionId = AppConfig.Instance.HmdaInstitutionId;
            string legalEntityName = AppConfig.Instance.HmdaLegalEntityName;

            foreach (DataRow row in dt.Rows)
            {
                HmdaData h = MapHmdaFromRow(row);

                // Build LAR row per 2018+ format
                // Column order follows the CFPB Filing Instructions Guide
                string[] larRow = new string[] {
                    institutionId ?? "",                          // 1. LEI
                    legalEntityName ?? "",                        // 2. Legal entity name (TSV only)
                    h.LoanNumber ?? "",                           // 3. ULAM: Loan ID (NMLSR)
                    h.LoanYearString(),                           // 4. Application year
                    "",                                          // 5. Application date (reported as year only)
                    "",                                          // 6. Loan term
                    "",                                          // 7. APR
                    h.LoanPurposeCode ?? "",                      // 8. Loan purpose
                    h.Preapproval ?? "2",                         // 9. Preapproval
                    h.LoanTypeCode ?? "",                         // 10. Loan type (conventional/FHA/VA/USDA)
                    "",                                          // 11. Lien status
                    "",                                          // 12. Construction method
                    "",                                          // 13. Occupancy type
                    h.LoanAmountHmda.HasValue ? h.LoanAmountHmda.Value.ToString() : "",  // 14. Loan amount
                    "",                                          // 15. Loan amount (combined)
                    h.ActionTaken ?? "",                          // 16. Action taken
                    h.ActionTakenDt.HasValue ? h.ActionTakenDt.Value.ToString("yyyyMMdd") : "",  // 17. Action taken date
                    "",                                          // 18. Street address
                    "",                                          // 19. City
                    "",                                          // 20. State
                    "",                                          // 21. Zip code
                    h.PropertyCounty ?? "",                       // 22. County
                    h.PropertyCensusTract ?? "",                   // 23. Census tract
                    "",                                          // 24. Tract (legacy)
                    h.RaceNew ?? "",                              // 25. Applicant race (new disaggregated)
                    "",                                          // 26. Applicant race 2
                    "",                                          // 27. Applicant race 3
                    "",                                          // 28. Applicant race 4
                    "",                                          // 29. Applicant race 5
                    "",                                          // 30. Applicant race (free text)
                    h.RaceObserved ?? "",                         // 31. Applicant race observed
                    h.EthnicityNew ?? "",                         // 32. Applicant ethnicity
                    "",                                          // 33. Applicant ethnicity 2
                    "",                                          // 34. Applicant ethnicity 3
                    "",                                          // 35. Applicant ethnicity (free text)
                    "",                                          // 36. Applicant ethnicity observed
                    h.SexNew ?? "",                               // 37. Applicant sex
                    h.SexObserved ?? "",                          // 38. Applicant sex observed
                    // --- KNOWN BUG: co-applicant fields below ---
                    // These should come from the co-applicant's HMDA data, but we
                    // only store applicant demographics. Co-applicant is always
                    // reported as "8" (no co-applicant) here. See method comment.
                    "8",                                          // 39. Co-applicant race
                    "",                                          // 40. Co-applicant race 2
                    "",                                          // 41. Co-applicant race 3
                    "",                                          // 42. Co-applicant race 4
                    "",                                          // 43. Co-applicant race 5
                    "",                                          // 44. Co-applicant race (free text)
                    "3",                                          // 45. Co-applicant race observed
                    "2",                                          // 46. Co-applicant ethnicity
                    "",                                          // 47. Co-applicant ethnicity 2
                    "",                                          // 48. Co-applicant ethnicity 3
                    "",                                          // 49. Co-applicant ethnicity (free text)
                    "3",                                          // 50. Co-applicant ethnicity observed
                    "4",                                          // 51. Co-applicant sex
                    "3",                                          // 52. Co-applicant sex observed
                    // --- end known bug ---
                    h.IncomeAmount.HasValue ? h.IncomeAmount.Value.ToString() : "",  // 53. Income
                    h.DwellingType ?? "",                          // 54. Dwelling type (property type in LAR)
                    h.OccupancyType ?? "",                         // 55. Occupancy type
                    h.TotalUnits > 0 ? h.TotalUnits.ToString() : "",  // 56. Total units
                    "",                                          // 57. Affordable units
                    h.RateSpread.HasValue ? h.RateSpread.Value.ToString("F3") : "",  // 58. Rate spread
                    "",                                          // 59. HOEPA status
                    h.LienStatus ?? "",                           // 60. Lien status
                    "",                                          // 61. Credit score
                    "",                                          // 62. Credit score model
                    "",                                          // 63. Other credit score model
                    "8888",                                      // 64. Co-applicant credit score
                    "9",                                          // 65. Co-applicant credit score model
                    "",                                          // 66. Co-applicant other model
                    h.AusUsed ?? "",                              // 67. AUS used
                    "",                                          // 68. AUS result
                    "",                                          // 69. AUS override reason
                    "16",                                         // 70. AUS result (co-app)
                    "",                                          // 71. Other AUS
                    "",                                          // 72. Other AUS result
                    h.ReverseMortgage ?? "",                      // 73. Reverse mortgage
                    h.OpenEndCredit ?? "",                        // 74. Open-end credit
                    h.BusinessPurpose ?? "",                      // 75. Business purpose
                    "",                                          // 76. Total points and fees
                    "",                                          // 77. Origination charges
                    "",                                          // 78. Discount points
                    "",                                          // 79. Lender credits
                    "",                                          // 80. Interest rate
                    "",                                          // 81. Prepayment penalty term
                    "",                                          // 82. Prepayment penalty
                    "",                                          // 83. Debt-to-income ratio
                    "",                                          // 84. Combined loan-to-value ratio
                    "",                                          // 85. Loan term
                    "",                                          // 86. Introductory rate period
                    "",                                          // 87. Balloon payment
                    "",                                          // 88. Interest-only payments
                    "",                                          // 89. Negative amortization
                    "",                                          // 90. Other non-amort features
                    "",                                          // 91. Property value
                    "",                                          // 92. Manufactured home type
                    "",                                          // 93. Manufactured home secured property
                    "",                                          // 94. Total units (repeated?)
                    "",                                          // 95. Multifamily units
                    h.CovidForbearance ?? "",                     // 96. COVID forbearance (TEMP - should be removed)
                    h.CovidForbearanceDt.HasValue ? h.CovidForbearanceDt.Value.ToString("yyyyMMdd") : "",  // 97. COVID forbearance date
                    "",                                          // 98. Other
                };

                rows.Add(larRow);
            }

            FileLogger.Info("HmdaService", "Generated " + rows.Count + " LAR rows for year " + reportingYear);
            return rows;
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates HMDA data for a loan. Returns a list of validation errors.
        /// Empty list = valid.
        ///
        /// Validates required fields per 12 CFR 1003.4.
        /// NOTE: Not all fields are required at application. Some are required
        /// at final action. This check validates "final" completeness.
        /// </summary>
        public List<string> ValidateHmdaData(string loanNumber)
        {
            List<string> errors = new List<string>();

            HmdaData h = GetHmdaData(loanNumber);
            if (h == null)
            {
                errors.Add("HMDA record not found.");
                return errors;
            }

            // Action taken is required for all reported loans
            if (string.IsNullOrEmpty(h.ActionTaken))
            {
                errors.Add("action_taken is required (12 CFR 1003.4(a)(8)).");
            }

            // Action taken date is required if action taken is set
            if (!string.IsNullOrEmpty(h.ActionTaken) && !h.ActionTakenDt.HasValue)
            {
                errors.Add("action_taken_dt is required when action_taken is set (12 CFR 1003.4(a)(8)).");
            }

            // Loan type code required
            if (string.IsNullOrEmpty(h.LoanTypeCode))
            {
                errors.Add("loan_type is required (12 CFR 1003.4(a)(3)).");
            }

            // Loan purpose code required
            if (string.IsNullOrEmpty(h.LoanPurposeCode))
            {
                errors.Add("loan_purpose is required (12 CFR 1003.4(a)(2)).");
            }

            // Lien status required
            if (string.IsNullOrEmpty(h.LienStatus))
            {
                errors.Add("lien_status is required (12 CFR 1003.4(a)(4)).");
            }

            // Loan amount required
            if (!h.LoanAmountHmda.HasValue || h.LoanAmountHmda.Value <= 0)
            {
                errors.Add("loan_amount is required and must be > 0 (12 CFR 1003.4(a)(5)).");
            }

            // Property state required
            if (string.IsNullOrEmpty(h.PropertyState))
            {
                errors.Add("property_state is required (12 CFR 1003.4(a)(9)).");
            }

            // Race - must have either new or old format
            // 2018+ rule: race_new is required. If still using old format, flag it.
            if (string.IsNullOrEmpty(h.RaceNew) && string.IsNullOrEmpty(h.RaceOld))
            {
                errors.Add("applicant race is required (12 CFR 1003.4(a)(10)). Neither race_new nor race_old is set.");
            }
            else if (!string.IsNullOrEmpty(h.RaceOld) && string.IsNullOrEmpty(h.RaceNew))
            {
                // HACK (2018): Old format data. We allow it for historical records but
                // flag it as a warning. New records should use new format.
                errors.Add("WARNING: HMDA record uses old race format. Should be migrated to new disaggregated format per 2018 HMDA rule.");
            }

            // Ethnicity - same logic as race
            if (string.IsNullOrEmpty(h.EthnicityNew) && string.IsNullOrEmpty(h.EthnicityOld))
            {
                errors.Add("applicant ethnicity is required (12 CFR 1003.4(a)(10)).");
            }

            // Sex
            if (string.IsNullOrEmpty(h.SexNew) && string.IsNullOrEmpty(h.SexOld))
            {
                errors.Add("applicant sex is required (12 CFR 1003.4(a)(10)).");
            }

            // 2018+ fields: dwelling type and total units
            if (h.ReportingYear >= 2018)
            {
                if (string.IsNullOrEmpty(h.DwellingType))
                {
                    errors.Add("dwelling_type is required for 2018+ reporting (12 CFR 1003.4(a)(12)).");
                }
                if (h.TotalUnits <= 0)
                {
                    errors.Add("total_units is required for 2018+ reporting (12 CFR 1003.4(a)(13)).");
                }
            }

            // 2020+ fields: COVID forbearance
            // HACK (2020): This is "temporary" but CFPB made it permanent in 2023.
            // If covid_forbearance is set, the date should also be set.
            if (!string.IsNullOrEmpty(h.CovidForbearance) && h.CovidForbearance == "1")
            {
                if (!h.CovidForbearanceDt.HasValue)
                {
                    errors.Add("covid_forbearance_dt is required when covid_forbearance is flagged.");
                }
            }

            // 2023+ fields
            if (h.ReportingYear >= 2023)
            {
                if (string.IsNullOrEmpty(h.ReverseMortgage))
                {
                    errors.Add("reverse_mortgage is required for 2023+ reporting.");
                }
                if (string.IsNullOrEmpty(h.OpenEndCredit))
                {
                    errors.Add("open_end_credit is required for 2023+ reporting.");
                }
                if (string.IsNullOrEmpty(h.BusinessPurpose))
                {
                    errors.Add("business_purpose is required for 2023+ reporting.");
                }
            }

            return errors;
        }

        #endregion

        #region Mapping

        private HmdaData MapHmdaFromRow(DataRow row)
        {
            HmdaData h = new HmdaData();
            h.HmdaId = Convert.ToInt32(row["hmda_id"]);
            h.LoanNumber = row["loan_number"] == DBNull.Value ? null : row["loan_number"].ToString();
            h.ReportingYear = row["reporting_year"] == DBNull.Value ? 0 : Convert.ToInt32(row["reporting_year"]);

            // OLD fields (pre-2018) - kept for historical data
            h.RaceOld = row["race_old"] == DBNull.Value ? null : row["race_old"].ToString();
            h.EthnicityOld = row["ethnicity_old"] == DBNull.Value ? null : row["ethnicity_old"].ToString();
            h.SexOld = row["sex_old"] == DBNull.Value ? null : row["sex_old"].ToString();

            // NEW fields (2018+) - USE THESE for current data
            h.RaceNew = row["race_new"] == DBNull.Value ? null : row["race_new"].ToString();
            h.EthnicityNew = row["ethnicity_new"] == DBNull.Value ? null : row["ethnicity_new"].ToString();
            h.SexNew = row["sex_new"] == DBNull.Value ? null : row["sex_new"].ToString();
            h.RaceObserved = row["race_observed"] == DBNull.Value ? null : row["race_observed"].ToString();
            h.SexObserved = row["sex_observed"] == DBNull.Value ? null : row["sex_observed"].ToString();

            // Action
            h.ActionTaken = row["action_taken"] == DBNull.Value ? null : row["action_taken"].ToString();
            h.ActionTakenDt = row["action_taken_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["action_taken_dt"]);
            h.Preapproval = row["preapproval"] == DBNull.Value ? null : row["preapproval"].ToString();

            // Loan
            h.LoanTypeCode = row["loan_type_code"] == DBNull.Value ? null : row["loan_type_code"].ToString();
            h.LoanPurposeCode = row["loan_purpose_code"] == DBNull.Value ? null : row["loan_purpose_code"].ToString();
            h.LienStatus = row["lien_status"] == DBNull.Value ? null : row["lien_status"].ToString();
            h.LoanAmountHmda = row["loan_amount_hmda"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["loan_amount_hmda"]);

            // Geo
            h.PropertyState = row["property_state"] == DBNull.Value ? null : row["property_state"].ToString();
            h.PropertyCounty = row["property_county"] == DBNull.Value ? null : row["property_county"].ToString();
            h.PropertyCensusTract = row["property_census_tract"] == DBNull.Value ? null : row["property_census_tract"].ToString();

            // Borrower
            h.IncomeAmount = row["income_amount"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["income_amount"]);

            // 2018+ additions
            h.DwellingType = row["dwelling_type"] == DBNull.Value ? null : row["dwelling_type"].ToString();
            h.TotalUnits = row["total_units"] == DBNull.Value ? 0 : Convert.ToInt32(row["total_units"]);
            h.OccupancyType = row["occupancy_type"] == DBNull.Value ? null : row["occupancy_type"].ToString();

            // Rate/pricing
            h.RateSpread = row["rate_spread"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["rate_spread"]);
            h.HoepaStatus = row["hoepa_status"] == DBNull.Value ? null : row["hoepa_status"].ToString();

            // 2020+ additions
            h.AusUsed = row["aus_used"] == DBNull.Value ? null : row["aus_used"].ToString();
            h.AusResult = row["aus_result"] == DBNull.Value ? null : row["aus_result"].ToString();
            h.AusOverrideReason = row["aus_override_reason"] == DBNull.Value ? null : row["aus_override_reason"].ToString();

            // 2023+ additions
            h.ReverseMortgage = row["reverse_mortgage"] == DBNull.Value ? null : row["reverse_mortgage"].ToString();
            h.OpenEndCredit = row["open_end_credit"] == DBNull.Value ? null : row["open_end_credit"].ToString();
            h.BusinessPurpose = row["business_purpose"] == DBNull.Value ? null : row["business_purpose"].ToString();

            // COVID (supposed to be temporary)
            h.CovidForbearance = row["covid_forbearance"] == DBNull.Value ? null : row["covid_forbearance"].ToString();
            h.CovidForbearanceDt = row["covid_forbearance_dt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(row["covid_forbearance_dt"]);

            // Tracking
            h.CreatedDt = row["created_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["created_dt"]);
            h.UpdatedDt = row["updated_dt"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(row["updated_dt"]);
            h.CreatedBy = row["created_by"] == DBNull.Value ? null : row["created_by"].ToString();

            // Misc (nobody knows what these are)
            h.MiscFlag1 = row["misc_flag_1"] == DBNull.Value ? null : row["misc_flag_1"].ToString();
            h.MiscFlag2 = row["misc_flag_2"] == DBNull.Value ? null : row["misc_flag_2"].ToString();
            h.MiscRefId = row["misc_ref_id"] == DBNull.Value ? null : row["misc_ref_id"].ToString();

            return h;
        }

        #endregion

        #region Dead Code (Old HMDA Format)

        // =========================================================================
        // DEAD CODE - Old HMDA format (pre-2018)
        // These methods were used before the 2018 HMDA rule change. They're kept
        // here commented out because someone might need to reference the old
        // format for historical data. DO NOT uncomment these. - Sarah, 2018
        // =========================================================================

        /*
        /// <summary>
        /// Creates a HMDA record using the OLD (pre-2018) race/ethnicity/sex format.
        /// DEPRECATED: Use CreateHmdaRecord with new format fields instead.
        /// </summary>
        public int CreateHmdaRecordOld(string loanNumber, string race, string ethnicity, string sex,
            int? incomeAmount, string createdBy)
        {
            // OLD FORMAT: single race/ethnicity/sex field (not disaggregated)
            // Pre-2018 HMDA used: 1=American Indian, 2=Asian, 3=Black, 4=Native Hawaiian,
            // 5=White, 6=Not provided, 7=Not applicable, 8=No co-applicant
            string sql = "INSERT INTO hmda_data (loan_number, reporting_year, race_old, ethnicity_old, " +
                "sex_old, income_amount, created_dt, updated_dt, created_by) " +
                "VALUES ('" + loanNumber + "', " + DateTime.Now.Year + ", '" + race + "', '" +
                ethnicity + "', '" + sex + "', " + (incomeAmount.HasValue ? incomeAmount.Value.ToString() : "NULL") +
                ", NOW(), NOW(), '" + createdBy + "') RETURNING hmda_id";

            object result = _db.ExecuteComplianceScalar(sql);
            return Convert.ToInt32(result);
        }
        */

        /*
        /// <summary>
        /// Generates HMDA LAR in the OLD (pre-2018) format.
        /// DEPRECATED: Use GenerateHmdaLar for 2018+ format.
        /// The old LAR had fewer columns and used single-value race/ethnicity/sex.
        /// </summary>
        public List<string[]> GenerateHmdaLarOld(int reportingYear)
        {
            List<string[]> rows = new List<string[]>();
            string sql = "SELECT * FROM hmda_data WHERE reporting_year = " + reportingYear;
            DataTable dt = _db.ExecuteComplianceQuery(sql);

            foreach (DataRow row in dt.Rows)
            {
                // Old format: ~39 columns
                string[] larRow = new string[] {
                    AppConfig.Instance.HmdaInstitutionId ?? "",
                    row["loan_number"] == DBNull.Value ? "" : row["loan_number"].ToString(),
                    reportingYear.ToString(),
                    row["loan_type_code"] == DBNull.Value ? "" : row["loan_type_code"].ToString(),
                    row["loan_purpose_code"] == DBNull.Value ? "" : row["loan_purpose_code"].ToString(),
                    row["race_old"] == DBNull.Value ? "" : row["race_old"].ToString(),
                    row["ethnicity_old"] == DBNull.Value ? "" : row["ethnicity_old"].ToString(),
                    row["sex_old"] == DBNull.Value ? "" : row["sex_old"].ToString(),
                    row["income_amount"] == DBNull.Value ? "" : row["income_amount"].ToString(),
                    row["loan_amount_hmda"] == DBNull.Value ? "" : row["loan_amount_hmda"].ToString(),
                    row["action_taken"] == DBNull.Value ? "" : row["action_taken"].ToString(),
                    row["property_state"] == DBNull.Value ? "" : row["property_state"].ToString(),
                    // ... truncated. The old format is documented in the 2004 HMDA guide.
                };
                rows.Add(larRow);
            }
            return rows;
        }
        */

        #endregion
    }

    /// <summary>
    /// Extension methods for HmdaData used by the LAR generator.
    /// Added 2018 because the LAR generation needed formatted values that
    /// didn't belong on the model itself.
    /// </summary>
    public static class HmdaDataExtensions
    {
        /// <summary>
        /// Returns the loan year string for LAR reporting.
        /// </summary>
        public static string LoanYearString(this HmdaData h)
        {
            return h.ReportingYear.ToString();
        }
    }
}
