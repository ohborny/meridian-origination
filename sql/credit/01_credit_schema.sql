-- ============================================================================
-- los_credit Schema - Credit Analysis Database
--
-- ESTIMATED AGE: Original tables from 2008, restructured 2013 (partially)
-- TEAM: Credit Services Team (merged with Underwriting 2016, split again 2019)
-- NAMING: Mixed snake_case and CamelCase (team couldn't agree on convention)
--
-- NOTE: This database is COMPLETELY SEPARATE from los_core. Credit scores are
-- synced to los_core.loans.borrower_credit_score by the overnight batch job.
-- If the batch job fails, the cached score in los_core will be STALE.
-- This has caused 3 denied loans to be approved in the past. See LOS-2289.
-- ============================================================================

\c los_credit;

-- === CREDIT REPORTS (header per pull) ===
CREATE TABLE credit_reports (
    ReportId SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,  -- No FK! Points to los_core.loans.loan_number
    borrower_ssn_hash VARCHAR(64),  -- SHA256 of full SSN, we don't store plaintext
    report_type VARCHAR(20) NOT NULL,  -- MERGED, INDIVIDUAL, JOINT
    pull_type VARCHAR(20) NOT NULL,  -- SOFT, HARD, REISSUE
    pulled_by VARCHAR(50) NOT NULL,
    pulled_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Triple merge info
    experian_report_id VARCHAR(30),
    equifax_report_id VARCHAR(30),
    transunion_report_id VARCHAR(30),
    -- Summary
    experian_score INTEGER,
    equifax_score INTEGER,
    transunion_score INTEGER,
    representative_score INTEGER,  -- Middle score (or lowest for non-joint)
    -- File status
    file_status VARCHAR(20),  -- CLEAR, FROZEN, THIN_FILE, NO_HIT
    fraud_alert BOOLEAN DEFAULT FALSE,
    active_alert_count INTEGER DEFAULT 0,
    -- Raw data
    raw_report_data TEXT,  -- XML from credit bureau, stored as-is
    -- Tracking
    vendor_name VARCHAR(50),  -- CREDITINFO, CORELOGIC, etc.
    vendor_reference VARCHAR(50),
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_credit_reports_loan ON credit_reports(loan_number);
CREATE INDEX idx_credit_reports_dt ON credit_reports(pulled_dt);

-- === CREDIT SCORES (individual bureau scores, history) ===
CREATE TABLE credit_scores (
    score_id SERIAL PRIMARY KEY,
    report_id INTEGER NOT NULL REFERENCES credit_reports(ReportId),
    loan_number VARCHAR(20) NOT NULL,
    bureau VARCHAR(20) NOT NULL,  -- EXPERIAN, EQUIFAX, TRANSUNION
    score_model VARCHAR(20) DEFAULT 'FICO_8',  -- FICO_8, FICO_9, VANTAGE_3, VANTAGE_4
    score_value INTEGER NOT NULL,
    score_reason_1 VARCHAR(100),  -- Adverse action reason codes
    score_reason_2 VARCHAR(100),
    score_reason_3 VARCHAR(100),
    score_reason_4 VARCHAR(100),
    -- Added 2015 for trended data
    trended_data_available BOOLEAN DEFAULT FALSE,
    trended_data XML,  -- 24 months of balance/payment history
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_credit_scores_report ON credit_scores(report_id);
CREATE INDEX idx_credit_scores_loan ON credit_scores(loan_number);

-- === CREDIT LIABILITIES (individual trades/accounts) ===
CREATE TABLE credit_liabilities (
    liability_id SERIAL PRIMARY KEY,
    report_id INTEGER NOT NULL REFERENCES credit_reports(ReportId),
    loan_number VARCHAR(20) NOT NULL,
    -- Account info
    creditor_name VARCHAR(100) NOT NULL,
    account_number VARCHAR(30),  -- Partially masked
    account_type VARCHAR(30) NOT NULL,  -- REVOLVING, INSTALLMENT, MORTGAGE, AUTO, STUDENT, OTHER
    account_status VARCHAR(20),  -- OPEN, CLOSED, PAID, COLLECTION, CHARGEOFF
    -- Financials
    monthly_payment NUMERIC(10,2),
    current_balance NUMERIC(12,2),
    high_credit NUMERIC(12,2),  -- Original loan amount or credit limit
    past_due_amount NUMERIC(12,2),
    -- History
    months_reviewed INTEGER,
    late_30_count INTEGER DEFAULT 0,
    late_60_count INTEGER DEFAULT 0,
    late_90_count INTEGER DEFAULT 0,
    -- For DTI calculation
    is_included_in_dti BOOLEAN DEFAULT TRUE,  -- Can be excluded if being paid off at closing
    exclude_reason VARCHAR(100),
    -- Added 2017 for AUS
    aus_responsible_party VARCHAR(20),  -- BORROWER, COBORROWER, JOINT
    -- Misc
    date_opened DATE,
    date_reported DATE,
    remarks TEXT,
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_liabilities_report ON credit_liabilities(report_id);
CREATE INDEX idx_liabilities_loan ON credit_liabilities(loan_number);

-- === CREDIT INQUIRIES ===
CREATE TABLE credit_inquiries (
    inquiry_id SERIAL PRIMARY KEY,
    report_id INTEGER NOT NULL REFERENCES credit_reports(ReportId),
    loan_number VARCHAR(20) NOT NULL,
    inquiring_company VARCHAR(100) NOT NULL,
    inquiry_date DATE NOT NULL,
    inquiry_type VARCHAR(20),  -- MORTGAGE, AUTO, REVOLVING, COLLECTION
    -- Added 2014 for inquiry analysis
    is_rate_shopping BOOLEAN DEFAULT FALSE,
    shopping_window_days INTEGER DEFAULT 14,
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_inquiries_report ON credit_inquiries(report_id);

-- === CREDIT PUBLIC RECORDS (bankruptcies, judgments, etc.) ===
CREATE TABLE credit_public_records (
    public_record_id SERIAL PRIMARY KEY,
    report_id INTEGER NOT NULL REFERENCES credit_reports(ReportId),
    loan_number VARCHAR(20) NOT NULL,
    record_type VARCHAR(30) NOT NULL,  -- BANKRUPTCY_CH7, BANKRUPTCY_CH13, JUDGEMENT, TAX_LIEN, FORECLOSURE
    court_name VARCHAR(100),
    case_number VARCHAR(50),
    filed_date DATE,
    disposition VARCHAR(50),  -- DISCHARGED, DISMISSED, SATISFIED, ACTIVE
    disposition_date DATE,
    amount NUMERIC(12,2),
    -- Added 2018 for HUD requirements
    meets_waiting_period BOOLEAN,
    waiting_period_years INTEGER,
    ext_notes TEXT,
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_public_records_report ON credit_public_records(report_id);

-- === DTI CALCULATIONS (cached results, may be stale) ===
CREATE TABLE dti_calculations (
    dti_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL UNIQUE,  -- One active calculation per loan
    report_id INTEGER REFERENCES credit_reports(ReportId),
    -- Income (from application, not credit report)
    monthly_income NUMERIC(12,2) NOT NULL,
    coborrower_income NUMERIC(12,2) DEFAULT 0,
    total_monthly_income NUMERIC(12,2) NOT NULL,
    -- Debts
    total_monthly_debts NUMERIC(12,2) NOT NULL,
    proposed_housing_payment NUMERIC(12,2) NOT NULL,  -- PITIA
    -- Ratios
    front_end_dti NUMERIC(8,2),  -- Housing / Income
    back_end_dti NUMERIC(8,2),  -- (Housing + Debts) / Income
    -- Flags
    exceeds_guideline BOOLEAN DEFAULT FALSE,
    dti_guideline NUMERIC(5,2) DEFAULT 43.00,  -- QM rule 43% DTI
    -- Tracking
    calculated_by VARCHAR(50) NOT NULL,
    calculated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Added 2020 for manual overrides
    manual_override_dti NUMERIC(8,2),
    override_reason VARCHAR(200),
    override_approved_by VARCHAR(50)
);

CREATE INDEX idx_dti_loan ON dti_calculations(loan_number);

-- === CREDIT PULL LOG (audit trail for FCRA compliance) ===
CREATE TABLE credit_pull_log (
    log_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    borrower_identifier_hash VARCHAR(64) NOT NULL,
    pull_purpose VARCHAR(50) NOT NULL,  -- ORIGINATION, REVIEW, ADVERSE_ACTION, REISSUE
    pulled_by VARCHAR(50) NOT NULL,
    pulled_by_ip VARCHAR(45),
    pulled_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    vendor_name VARCHAR(50),
    vendor_reference VARCHAR(50),
    -- FCRA required fields
    permissible_purpose_code VARCHAR(10),  -- Per FCRA Section 604
    result_code VARCHAR(10),
    -- Added 2019 for audit
    authorized_by_borrower BOOLEAN DEFAULT TRUE,
    authorization_doc_id VARCHAR(50)
);

CREATE INDEX idx_pull_log_loan ON credit_pull_log(loan_number);
CREATE INDEX idx_pull_log_dt ON credit_pull_log(pulled_dt);
