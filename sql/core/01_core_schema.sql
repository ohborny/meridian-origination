-- ============================================================================
-- los_core Schema - Main Loan Origination Database
-- 
-- ESTIMATED AGE: Original tables from 2005, additions through 2024
-- TEAM: Core Platform Team ( disbanded 2019, now maintained by "whoever is available")
-- NAMING: snake_case (original convention, mostly maintained)
--
-- WARNING: Several tables have columns that were added ad-hoc over the years.
-- The 'ext_data' and 'misc_fields' columns are JSON dumps of stuff nobody
-- wanted to model properly. Do NOT rely on their structure.
-- ============================================================================

\c los_core;

-- === LOAN PRODUCTS (reference data, rarely changes) ===
CREATE TABLE loan_products (
    product_id SERIAL PRIMARY KEY,
    product_code VARCHAR(20) NOT NULL UNIQUE,
    product_name VARCHAR(100) NOT NULL,
    product_type VARCHAR(30) NOT NULL,  -- CONVENTIONAL, FHA, VA, USDA, JUMBO
    term_months INTEGER NOT NULL,
    amortization_type VARCHAR(20) NOT NULL DEFAULT 'FIXED',  -- FIXED, ARM, IO, BALLOON
    min_loan_amt NUMERIC(12,2),
    max_loan_amt NUMERIC(12,2),
    min_fico INTEGER,
    max_ltv NUMERIC(5,2),
    max_dti NUMERIC(5,2),
    is_active BOOLEAN DEFAULT TRUE,
    effective_date DATE DEFAULT CURRENT_DATE,
    expiry_date DATE,
    -- Added 2017 for investor tracking
    investor_code VARCHAR(20),
    -- Added 2019 for LLPA (loan-level price adjustments)
    llpa_table_id VARCHAR(30),
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    ext_data TEXT  -- JSON blob, don't ask
);

-- === BRANCHES ===
CREATE TABLE branches (
    branch_id SERIAL PRIMARY KEY,
    branch_code VARCHAR(10) NOT NULL UNIQUE,
    branch_name VARCHAR(100) NOT NULL,
    branch_addr1 VARCHAR(100),
    branch_addr2 VARCHAR(100),
    branch_city VARCHAR(50),
    branch_state CHAR(2),
    branch_zip VARCHAR(10),
    branch_phone VARCHAR(20),
    nmls_id VARCHAR(20),  -- Added 2010 when NMLS became mandatory
    region_code VARCHAR(10),
    is_active BOOLEAN DEFAULT TRUE,
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- === LOAN OFFICERS ===
CREATE TABLE loan_officers (
    lo_id SERIAL PRIMARY KEY,
    lo_code VARCHAR(20) NOT NULL UNIQUE,
    first_name VARCHAR(50) NOT NULL,
    last_name VARCHAR(50) NOT NULL,
    nmls_id VARCHAR(20),  -- Added 2010
    branch_id INTEGER REFERENCES branches(branch_id),
    email_addr VARCHAR(100),
    phone_num VARCHAR(20),
    hire_date DATE,
    term_date DATE,  -- NULL if still active
    is_active BOOLEAN DEFAULT TRUE,
    commission_rate NUMERIC(5,2) DEFAULT 1.00,
    -- Added 2015 for compliance
    e_o_insurance_exp DATE,  -- E&O insurance expiration
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- === LOANS (the main table, the god table of this database) ===
CREATE TABLE loans (
    loan_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL UNIQUE,  -- Format: BRANCH-YYYY-NNNNN
    loan_guid VARCHAR(36),  -- Added 2012 for external system sync, sometimes NULL
    -- Borrower info (denormalized from customer DB for "performance" - see LOS-892)
    borrower_firstname VARCHAR(50),
    borrower_lastname VARCHAR(50),
    borrower_ssn_last4 CHAR(4),  -- Only last 4 stored here, full SSN in customer DB
    borrower_credit_score INTEGER,  -- Cached, may be stale (see sync issues)
    -- Co-borrower
    coborrower_firstname VARCHAR(50),
    coborrower_lastname VARCHAR(50),
    coborrower_credit_score INTEGER,
    -- Loan details
    product_id INTEGER REFERENCES loan_products(product_id),
    loan_purpose VARCHAR(20) NOT NULL,  -- PURCHASE, REFINANCE, CASHOUT_REFI, STREAMLINE
    loan_amount NUMERIC(12,2) NOT NULL,
    interest_rate NUMERIC(6,3),
    term_months INTEGER,
    amortization_type VARCHAR(20),
    -- Property
    property_addr1 VARCHAR(100),
    property_addr2 VARCHAR(100),
    property_city VARCHAR(50),
    property_state CHAR(2),
    property_zip VARCHAR(10),
    property_type VARCHAR(20),  -- SFR, CONDO, TOWNHOUSE, MULTI, MANUFACTURED
    property_occupancy VARCHAR(20),  -- PRIMARY, SECONDARY, INVESTMENT
    property_value NUMERIC(12,2),
    appraised_value NUMERIC(12,2),
    -- LTV/DTI (calculated, cached, often stale)
    ltv NUMERIC(8,2),
    cltv NUMERIC(8,2),
    dti NUMERIC(8,2),
    htdti NUMERIC(8,2),  -- Housing-to-income (front-end DTI)
    -- Status
    loan_status VARCHAR(30) NOT NULL DEFAULT 'APPLICATION',
    -- APPLICATION -> PROCESSING -> UNDERWRITING -> CONDITIONAL_APPROVAL 
    -- -> CLEAR_TO_CLOSE -> CLOSING -> FUNDED -> SHIPPED -> PURCHASED
    -- (or) -> SUSPENDED -> DENIED -> WITHDRAWN -> CANCELLED
    loan_substatus VARCHAR(30),
    -- People
    loan_officer_id INTEGER REFERENCES loan_officers(lo_id),
    processor_id INTEGER,  -- No FK, processors are in a different system
    underwriter_id INTEGER,  -- No FK, underwriters tracked in compliance DB
    branch_id INTEGER REFERENCES branches(branch_id),
    -- Dates (many are NULL until the pipeline reaches that stage)
    application_dt DATE,
    processing_dt DATE,
    underwriting_dt DATE,
    approval_dt DATE,
    ctc_dt DATE,  -- Clear to close
    closing_dt DATE,
    funded_dt DATE,
    shipped_dt DATE,
    purchased_dt DATE,  -- Investor purchase
    -- Lock info
    lock_id VARCHAR(30),
    lock_exp_dt DATE,
    lock_rate NUMERIC(6,3),
    -- Pricing
    base_rate NUMERIC(6,3),
    total_points NUMERIC(6,3),
    lender_credit NUMERIC(12,2),
    -- Misc
    loan_officer_notes TEXT,
    ext_data TEXT,  -- JSON blob, added 2016, contains "stuff"
    misc_fields TEXT,  -- Another JSON blob, added 2018, different "stuff"
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_by VARCHAR(50) DEFAULT 'system',
    updated_by VARCHAR(50) DEFAULT 'system'
);

-- Index on loan_number (already unique but explicit for the DBAs)
CREATE INDEX idx_loans_loan_number ON loans(loan_number);
CREATE INDEX idx_loans_status ON loans(loan_status);
CREATE INDEX idx_loans_branch ON loans(branch_id);
CREATE INDEX idx_loans_lo ON loans(loan_officer_id);
CREATE INDEX idx_loans_app_dt ON loans(application_dt);

-- === LOAN STATUS HISTORY ===
CREATE TABLE loan_status_history (
    history_id SERIAL PRIMARY KEY,
    loan_id INTEGER NOT NULL REFERENCES loans(loan_id),
    from_status VARCHAR(30),
    to_status VARCHAR(30) NOT NULL,
    changed_by VARCHAR(50) NOT NULL,
    changed_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    change_reason VARCHAR(200),
    notes TEXT
);

CREATE INDEX idx_status_hist_loan ON loan_status_history(loan_id);

-- === LOAN CONDITIONS (underwriting conditions) ===
CREATE TABLE loan_conditions (
    condition_id SERIAL PRIMARY KEY,
    loan_id INTEGER NOT NULL REFERENCES loans(loan_id),
    condition_type VARCHAR(30) NOT NULL,  -- PRIOR_TO_FINAL, PRIOR_TO_DOCS, PRIOR_TO_FUNDING, PRIOR_TO_PURCHASE
    condition_category VARCHAR(50) NOT NULL,  -- INCOME, ASSET, CREDIT, PROPERTY, TITLE, INSURANCE, MISC
    condition_desc TEXT NOT NULL,
    is_satisfied BOOLEAN DEFAULT FALSE,
    satisfied_by VARCHAR(50),
    satisfied_dt TIMESTAMP,
    added_by VARCHAR(50) NOT NULL,
    added_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Added 2020 for condition grouping
    condition_group VARCHAR(30),
    is_waivable BOOLEAN DEFAULT FALSE,
    waiver_approved_by VARCHAR(50),
    waiver_approved_dt TIMESTAMP
);

CREATE INDEX idx_conditions_loan ON loan_conditions(loan_id);
CREATE INDEX idx_conditions_satisfied ON loan_conditions(is_satisfied);

-- === LOAN PRICING DETAIL ===
CREATE TABLE loan_pricing_detail (
    pricing_id SERIAL PRIMARY KEY,
    loan_id INTEGER NOT NULL REFERENCES loans(loan_id),
    fee_code VARCHAR(30) NOT NULL,  -- ORIGINATION, DISCOUNT, UNDERWRITING, PROCESSING, etc.
    fee_desc VARCHAR(100),
    fee_amount NUMERIC(12,2) NOT NULL,
    fee_paid_by VARCHAR(20) DEFAULT 'BORROWER',  -- BORROWER, LENDER, SELLER
    is_apr_fee BOOLEAN DEFAULT TRUE,  -- Does this fee affect APR?
    section VARCHAR(10),  -- A, B, C for GCD/CD sections
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_pricing_loan ON loan_pricing_detail(loan_id);

-- === LOAN DOCUMENTS ===
-- The Documents module also bootstraps this table in
-- DocumentService.EnsureDocumentTablesExist(), but nothing calls that method on
-- startup, so reads failed with 'relation "loan_documents" does not exist' until
-- some other code path happened to create it. Kept here so it exists from init.
CREATE TABLE loan_documents (
    document_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    document_type VARCHAR(40) NOT NULL,
    document_name VARCHAR(200),
    document_desc TEXT,
    file_path VARCHAR(500),
    file_extension VARCHAR(10),
    file_size_bytes BIGINT,
    file_hash VARCHAR(64),
    status VARCHAR(20) NOT NULL DEFAULT 'REQUIRED',
    is_required BOOLEAN DEFAULT TRUE,
    is_verified BOOLEAN DEFAULT FALSE,
    required_dt DATE,
    requested_dt TIMESTAMP,
    received_dt TIMESTAMP,
    reviewed_dt TIMESTAMP,
    expiration_dt DATE,
    requested_by VARCHAR(50),
    received_from VARCHAR(100),
    reviewed_by VARCHAR(50),
    rejection_reason TEXT,
    rejected_dt TIMESTAMP,
    rejected_by VARCHAR(50),
    created_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_by VARCHAR(50) DEFAULT 'system'
);

CREATE INDEX idx_loan_documents_loan ON loan_documents(loan_number);
CREATE INDEX idx_loan_documents_status ON loan_documents(status);

-- === AUDIT LOG ===
CREATE TABLE audit_log (
    audit_id SERIAL PRIMARY KEY,
    loan_id INTEGER,
    table_name VARCHAR(50) NOT NULL,
    action_type VARCHAR(10) NOT NULL,  -- INSERT, UPDATE, DELETE
    old_values TEXT,  -- JSON
    new_values TEXT,  -- JSON
    changed_by VARCHAR(50) NOT NULL,
    changed_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    ip_addr VARCHAR(45)
);

CREATE INDEX idx_audit_loan ON audit_log(loan_id);
CREATE INDEX idx_audit_dt ON audit_log(changed_dt);

-- === PIPELINE VIEW (for reporting, materialized nightly) ===
CREATE VIEW v_pipeline AS
SELECT 
    l.loan_id,
    l.loan_number,
    l.loan_status,
    l.loan_substatus,
    l.borrower_lastname,
    l.borrower_firstname,
    l.loan_amount,
    l.interest_rate,
    l.property_state,
    l.loan_purpose,
    l.loan_officer_id,
    lo.last_name AS lo_name,
    l.branch_id,
    l.application_dt,
    l.underwriting_dt,
    l.closing_dt,
    l.funded_dt,
    l.lock_exp_dt,
    CASE 
        WHEN l.loan_status IN ('DENIED', 'WITHDRAWN', 'CANCELLED') THEN 'CLOSED'
        WHEN l.loan_status = 'FUNDED' THEN 'FUNDED'
        WHEN l.loan_status = 'CLOSING' THEN 'CLOSING'
        WHEN l.loan_status = 'CLEAR_TO_CLOSE' THEN 'CTC'
        WHEN l.loan_status = 'CONDITIONAL_APPROVAL' THEN 'COND_APPROVAL'
        WHEN l.loan_status = 'UNDERWRITING' THEN 'IN_UW'
        WHEN l.loan_status = 'PROCESSING' THEN 'PROCESSING'
        WHEN l.loan_status = 'APPLICATION' THEN 'APPLICATION'
        ELSE 'OTHER'
    END AS pipeline_stage,
    -- application_dt is a DATE, so this subtraction is already an integer day count.
    -- Wrapping it in EXTRACT(DAY FROM ...) fails: there is no extract(text, integer).
    (CURRENT_DATE - COALESCE(l.application_dt, CURRENT_DATE))::INT AS days_in_pipeline
FROM loans l
LEFT JOIN loan_officers lo ON l.loan_officer_id = lo.lo_id
WHERE l.loan_status NOT IN ('SHIPPED', 'PURCHASED');

-- === LOCK DESK RATES (rate sheet data) ===
CREATE TABLE rate_sheet (
    rate_sheet_id SERIAL PRIMARY KEY,
    product_id INTEGER REFERENCES loan_products(product_id),
    rate_date DATE NOT NULL,
    base_rate NUMERIC(6,3) NOT NULL,
    point_0_rate NUMERIC(6,3),  -- Rate at 0 points
    point_0_125 NUMERIC(6,3),
    point_0_25 NUMERIC(6,3),
    point_0_375 NUMERIC(6,3),
    point_0_50 NUMERIC(6,3),
    point_0_625 NUMERIC(6,3),
    point_0_75 NUMERIC(6,3),
    point_0_875 NUMERIC(6,3),
    point_1_00 NUMERIC(6,3),
    point_1_125 NUMERIC(6,3),
    point_1_25 NUMERIC(6,3),
    point_1_50 NUMERIC(6,3),
    point_1_75 NUMERIC(6,3),
    point_2_00 NUMERIC(6,3),
    lender_credit_0_50 NUMERIC(6,3),
    lender_credit_1_00 NUMERIC(6,3),
    lender_credit_1_50 NUMERIC(6,3),
    lender_credit_2_00 NUMERIC(6,3),
    is_active BOOLEAN DEFAULT TRUE,
    effective_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expiry_dt TIMESTAMP
);

CREATE INDEX idx_rate_sheet_product ON rate_sheet(product_id, rate_date);
