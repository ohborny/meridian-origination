-- ============================================================================
-- los_compliance Schema - Regulatory Compliance Database
--
-- ESTIMATED AGE: Original tables from 2012, HEAVILY patched through 2024
-- TEAM: Compliance Engineering (high turnover, 6 different leads since 2012)
-- NAMING: snake_case with abbreviations (govt-style, inconsistent)
--
-- PATCH HISTORY (in lieu of proper documentation):
--   2012: Initial HMDA tables (HMDA 2011 rules)
--   2014: Added TRID tables (TILA-RESPA Integrated Disclosure, eff Oct 2015)
--   2015: Added state disclosure rules (50 states + DC + PR)
--   2017: HMDA 2018 changes (new data points, race/ethnicity expanded)
--   2018: Added fair lending review tables
--   2020: COVID-19 forbearance tracking (temporary, still here)
--   2021: Added HMDA quarterly filing support
--   2023: Added SCRA (Servicemembers Civil Relief Act) checks
--   2024: Added new NMLS unique identifier requirements
--
-- WARNING: The HMDA data model has been patched so many times that the
-- hmda_data table has 47 columns. The 2017 migration added new columns but
-- did NOT remove old ones. Both old and new race/ethnicity fields exist.
-- Use the NEW columns (race_new, ethnicity_new) for 2018+ data.
-- The old columns (race_old, ethnicity_old) are kept for historical data.
-- DO NOT MIX THEM. (This has happened twice. See LOS-3101, LOS-3455.)
-- ============================================================================

\c los_compliance;

-- === COMPLIANCE CHECKS (per-loan, per-check-type) ===
CREATE TABLE compliance_checks (
    chk_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,  -- No FK to los_core
    chk_type VARCHAR(40) NOT NULL,  -- HMDA, TRID, HOEPA, HPML, QM, HOEPA_SCRA, FAIR_LENDING, STATE
    chk_subtype VARCHAR(40),  -- e.g. for STATE: CA_DISCLOSURE, TX_DISCLOSURE
    chk_status VARCHAR(20) NOT NULL,  -- PASS, FAIL, WARNING, MANUAL_REVIEW, NOT_APPLICABLE
    chk_result TEXT,  -- JSON with detailed results
    chk_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    chk_by VARCHAR(50) DEFAULT 'system',
    -- Added 2016 for audit trail
    chk_version VARCHAR(20),  -- Which version of the rule was applied
    rule_set_id VARCHAR(30),
    -- Added 2019 for exception tracking
    has_exception BOOLEAN DEFAULT FALSE,
    exception_id INTEGER,
    -- Added 2021 for quarterly review
    reviewed_by VARCHAR(50),
    reviewed_dt TIMESTAMP
);

CREATE INDEX idx_compliance_loan ON compliance_checks(loan_number);
CREATE INDEX idx_compliance_type ON compliance_checks(chk_type, chk_status);

-- === HMDA DATA (the infamous 47-column table) ===
CREATE TABLE hmda_data (
    hmda_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    reporting_year INTEGER NOT NULL,  -- e.g. 2024
    -- === OLD HMDA FIELDS (pre-2018, kept for historical data) ===
    race_old VARCHAR(10),  -- 1=AmInd, 2=Asian, 3=Black, 4=PI, 5=White, 6=no_info, 7=NA, 8=no_co
    ethnicity_old VARCHAR(10),  -- 1=Hisp, 2=NotHisp, 3=no_info, 4=NA
    sex_old VARCHAR(10),  -- 1=Male, 2=Female, 3=no_info, 4=NA, 6=no_co
    -- === NEW HMDA FIELDS (2018+, use these for current data) ===
    race_new VARCHAR(50),  -- Can be multiple, pipe-separated: ASIAN|WHITE
    ethnicity_new VARCHAR(50),  -- HISPANIC|NOT_HISPANIC|JOINT|FREE_FORM
    sex_new VARCHAR(50),  -- MALE|FEMALE|JOINT|FREE_FORM
    race_observed VARCHAR(30),  -- Added 2018: visual observation
    sex_observed VARCHAR(30),
    -- === COMMON FIELDS (both old and new) ===
    -- Nullable on purpose: the final action is not known when the record is created at
    -- application. HmdaService.UpdateActionTaken and the HMDA sync batch job fill it in,
    -- and ValidateHmdaRecord reports it as a missing required field until then.
    action_taken VARCHAR(10),  -- 1=Originated, 2=Approved not accepted, 3=Denied, 4=Withdrawn, 5=Closed incomplete, 6=Purchased loan, 7=Preapproval denied, 8=Preapproval approved but not accepted
    action_taken_dt DATE,
    preapproval VARCHAR(10) DEFAULT '2',  -- 1=Preapproval requested, 2=Not
    loan_type_code VARCHAR(10),  -- 1=Conv, 2=FHA, 3=VA, 4=USDA
    loan_purpose_code VARCHAR(10),  -- 1=Purchase, 2=Improvement, 3=Refi
    lien_status VARCHAR(10),  -- 1=First, 2=Junior
    loan_amount_hmda NUMERIC(12,2),
    -- === GEO DATA ===
    property_state CHAR(2),
    property_county VARCHAR(10),  -- FIPS code
    property_census_tract VARCHAR(20),
    -- === BORROWER DATA ===
    income_amount INTEGER,  -- Rounded to nearest thousand (HMDA requirement)
    -- === 2018+ ADDITIONS ===
    dwelling_type VARCHAR(10),  -- 1=1-4 family site, 2=Manufactured, 3=Multifamily
    total_units INTEGER DEFAULT 1,
    occupancy_type VARCHAR(10),  -- 1=Primary, 2=Secondary, 3=Investment
    -- === 2018+ RATE/PRICING DATA ===
    rate_spread NUMERIC(8,2),  -- Rate spread above APOR
    hoepa_status VARCHAR(10) DEFAULT '3',  -- 1=HOEPA, 2=Not HOEPA, 3=NA
    -- === 2020+ ADDITIONS ===
    aus_used VARCHAR(50),  -- Pipe-separated AUS systems used
    aus_result VARCHAR(50),  -- Pipe-separated AUS results
    aus_override_reason VARCHAR(200),
    -- === 2023+ ADDITIONS ===
    reverse_mortgage VARCHAR(10) DEFAULT '2',  -- 1=Yes, 2=No
    open_end_credit VARCHAR(10) DEFAULT '2',
    business_purpose VARCHAR(10) DEFAULT '2',
    -- === COVID-19 ADDITIONS (2020, supposed to be temporary, still here) ===
    covid_forbearance VARCHAR(10) DEFAULT '2',  -- 1=Yes, 2=No
    covid_forbearance_dt DATE,
    -- === TRACKING ===
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_by VARCHAR(50) DEFAULT 'system',
    -- === MISC (nobody knows what these are for) ===
    misc_flag_1 VARCHAR(10),  -- Added 2015, purpose unknown
    misc_flag_2 VARCHAR(10),  -- Added 2016, purpose unknown
    misc_ref_id VARCHAR(50)  -- Added 2019, references something external
);

CREATE INDEX idx_hmda_loan ON hmda_data(loan_number);
CREATE INDEX idx_hmda_year ON hmda_data(reporting_year);
CREATE INDEX idx_hmda_action ON hmda_data(action_taken);

-- === DISCLOSURES (tracking which disclosures are required and sent) ===
CREATE TABLE disclosures (
    disclosure_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    disclosure_type VARCHAR(40) NOT NULL,  -- LE (Loan Estimate), CD (Closing Disclosure), 
                                           -- Initial, Revised, Final, Affiliated Business,
                                           -- Servicing, Appraisal, Special
    disclosure_subtype VARCHAR(40),  -- INITIAL, REVISED, FINAL, REISSUE
    -- Timing
    required_dt DATE NOT NULL,
    sent_dt DATE,
    received_dt DATE,  -- When borrower acknowledges receipt
    -- Delivery method
    delivery_method VARCHAR(20),  -- EMAIL, MAIL, E_SIGN, IN_PERSON
    delivery_addr VARCHAR(200),
    -- TRID specific timing (added 2015)
    trid_business_days INTEGER,  -- Days after application for LE
    trid_waiting_period_met BOOLEAN,
    trid_cd_waiting_met BOOLEAN,
    -- Content tracking
    version_number VARCHAR(10),  -- LE v1, LE v2, etc.
    pdf_doc_id VARCHAR(50),  -- Reference to document management system
    -- Tracking
    prepared_by VARCHAR(50),
    sent_by VARCHAR(50),
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Added 2020 for e-disclosure tracking
    esign_consent_dt TIMESTAMP,
    esign_consent_ip VARCHAR(45)
);

CREATE INDEX idx_disclosures_loan ON disclosures(loan_number);
CREATE INDEX idx_disclosures_type ON disclosures(disclosure_type);

-- === STATE DISCLOSURE RULES (50 states + DC + PR, complex matrix) ===
CREATE TABLE state_disclosure_rules (
    rule_id SERIAL PRIMARY KEY,
    -- Not CHAR(2): federal rules are stored under the 3-character sentinel 'FED'.
    state_code VARCHAR(3) NOT NULL,
    rule_name VARCHAR(100) NOT NULL,
    rule_type VARCHAR(30) NOT NULL,  -- DISCLOSURE, FEE_CAP, WAITING_PERIOD, DOCUMENT
    -- Applicability
    loan_types VARCHAR(100),  -- Pipe-separated: CONVENTIONAL|FHA|VA
    loan_purposes VARCHAR(100),  -- Pipe-separated: PURCHASE|REFINANCE|CASHOUT
    -- Rule details
    rule_desc TEXT,
    required_document VARCHAR(100),
    -- Timing
    timing_requirement VARCHAR(50),  -- AT_APPLICATION, WITHIN_3_DAYS, AT_CLOSING, N/A
    timing_days INTEGER,
    -- Fee caps (where applicable)
    max_fee_amount NUMERIC(12,2),
    max_fee_pct NUMERIC(8,2),
    -- Status
    is_active BOOLEAN DEFAULT TRUE,
    effective_dt DATE,
    expiry_dt DATE,
    -- Tracking
    last_updated_by VARCHAR(50),
    last_updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Added 2017 for regulatory citations
    regulation_citation VARCHAR(200),
    -- Added 2020 for automated checking
    automation_script VARCHAR(100),
    -- Added 2023 for compliance
    review_required BOOLEAN DEFAULT FALSE,
    review_frequency VARCHAR(20)  -- ANNUAL, SEMI_ANNUAL, QUARTERLY
);

CREATE INDEX idx_state_rules_state ON state_disclosure_rules(state_code);
CREATE INDEX idx_state_rules_type ON state_disclosure_rules(rule_type);

-- === TRID TIMELINE (tracks the 3-day waiting period requirements) ===
CREATE TABLE trid_timeline (
    trid_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    -- Application
    application_dt DATE NOT NULL,
    -- Loan Estimate (LE)
    le_required_by_dt DATE NOT NULL,  -- Application + 3 business days
    le_sent_dt DATE,
    le_received_dt DATE,
    le_version VARCHAR(10),
    -- Changed circumstances (trigger revised LE)
    changed_circumstance_dt DATE,
    changed_circumstance_desc TEXT,
    revised_le_required_dt DATE,
    revised_le_sent_dt DATE,
    -- Closing Disclosure (CD)
    cd_prepared_dt DATE,
    cd_sent_dt DATE,
    cd_received_dt DATE,  -- Must be received 3 business days before consummation
    consummation_dt DATE,  -- Closing date
    cd_waiting_met BOOLEAN,
    -- Lock
    rate_lock_dt DATE,
    rate_lock_exp_dt DATE,
    -- Tracking
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Added 2018 for tolerance tracking
    le_tolerance_cure NUMERIC(12,2),  -- Amount of tolerance cure if applicable
    cd_vs_le_variance NUMERIC(12,2)  -- Total variance between LE and CD
);

CREATE INDEX idx_trid_loan ON trid_timeline(loan_number);

-- === COMPLIANCE EXCEPTIONS (when a rule is overridden) ===
CREATE TABLE compliance_exceptions (
    exception_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    chk_id INTEGER REFERENCES compliance_checks(chk_id),
    exception_type VARCHAR(40) NOT NULL,
    exception_reason TEXT NOT NULL,
    -- Approval
    requested_by VARCHAR(50) NOT NULL,
    requested_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    approved_by VARCHAR(50),
    approved_dt TIMESTAMP,
    approval_status VARCHAR(20) DEFAULT 'PENDING',  -- PENDING, APPROVED, DENIED, EXPIRED
    -- Risk
    risk_assessment TEXT,
    risk_level VARCHAR(10),  -- LOW, MEDIUM, HIGH, CRITICAL
    -- Expiry
    expiry_dt DATE,
    -- Added 2019 for regulatory tracking
    regulatory_notification VARCHAR(200),
    is_reportable BOOLEAN DEFAULT FALSE
);

CREATE INDEX idx_exceptions_loan ON compliance_exceptions(loan_number);
CREATE INDEX idx_exceptions_status ON compliance_exceptions(approval_status);

-- === FAIR LENDING REVIEW ===
CREATE TABLE fair_lending_review (
    review_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    review_dt DATE NOT NULL,
    review_type VARCHAR(30),  -- PRE_FUNDING, POST_FUNDING, COMPLAINT_DRIVEN, RANDOM
    reviewer VARCHAR(50) NOT NULL,
    -- Findings
    pricing_disparity NUMERIC(8,2),  -- Rate spread vs comparable loans
    pricing_disparity_threshold NUMERIC(8,2),
    has_disparity BOOLEAN DEFAULT FALSE,
    -- Overrides
    override_factors TEXT,  -- Legitimate factors explaining any disparity
    -- Status
    review_status VARCHAR(20) DEFAULT 'PENDING',  -- PENDING, CLEARED, FLAGGED, ESCALATED
    review_notes TEXT,
    -- Added 2020 for HMDA data quality
    hmda_data_quality_score INTEGER,  -- 0-100
    hmda_data_issues TEXT,
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_fair_lending_loan ON fair_lending_review(loan_number);
CREATE INDEX idx_fair_lending_status ON fair_lending_review(review_status);

-- === SCRA CHECKS (Servicemembers Civil Relief Act, added 2023) ===
CREATE TABLE scra_checks (
    scra_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    borrower_name VARCHAR(100) NOT NULL,
    borrower_ssn_hash VARCHAR(64),
    check_dt DATE NOT NULL,
    is_active_military BOOLEAN DEFAULT FALSE,
    service_branch VARCHAR(20),
    active_duty_start_dt DATE,
    active_duty_end_dt DATE,
    -- Protections applied
    rate_cap_applied BOOLEAN DEFAULT FALSE,
    rate_cap_pct NUMERIC(6,3),  -- 6% cap
    foreclosure_protection BOOLEAN DEFAULT FALSE,
    -- Tracking
    verified_by VARCHAR(50),
    verification_method VARCHAR(50),  -- DMO, DMDC, MANUAL
    verification_reference VARCHAR(50),
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_scra_loan ON scra_checks(loan_number);
