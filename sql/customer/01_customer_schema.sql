-- ============================================================================
-- los_customer Schema - Customer/Borrower Database
--
-- ESTIMATED AGE: Original tables from 2015 (migrated from legacy CRM)
-- TEAM: Customer Experience Team (rebranded 3 times, currently "Borrower Success")
-- NAMING: PascalCase columns (team came from a .NET/Entity Framework background)
--
-- NOTE: This database is SEPARATE from los_core. Borrower data is denormalized
-- into los_core.loans for "performance reasons" (see comment in core schema).
-- The overnight sync job copies customer data INTO los_core, but does NOT
-- sync changes back. If a borrower's address changes in los_core, it will
-- NOT be reflected here. This is a known issue. See LOS-1502.
--
-- ALSO NOTE: The CustomerId here is NOT the same as any ID in los_core.
-- The mapping is: los_customer.customers.CustomerId <-> los_core.loans (no FK).
-- The join is done on SSN hash, which is fragile. See LOS-1873.
-- ============================================================================

\c los_customer;

-- === CUSTOMERS (borrower master) ===
CREATE TABLE customers (
    CustomerId SERIAL PRIMARY KEY,
    CustomerGuid VARCHAR(36) UNIQUE,  -- For external sync
    -- Name
    FirstName VARCHAR(50) NOT NULL,
    MiddleName VARCHAR(50),
    LastName VARCHAR(50) NOT NULL,
    Suffix VARCHAR(10),  -- Jr, Sr, III, etc.
    -- PII (encrypted at rest, but stored as plaintext here for "legacy compatibility")
    SsnHash VARCHAR(64),  -- SHA256 of full SSN
    SsnLast4 CHAR(4),
    DateOfBirth DATE,
    -- Contact
    EmailAddress VARCHAR(100),
    PhoneNumber VARCHAR(20),
    CellPhone VARCHAR(20),
    PreferredContact VARCHAR(10) DEFAULT 'EMAIL',  -- EMAIL, PHONE, MAIL
    -- Demographics (HMDA collected here, synced to compliance DB)
    Gender VARCHAR(10),
    -- Status
    CustomerStatus VARCHAR(20) DEFAULT 'ACTIVE',  -- ACTIVE, INACTIVE, DO_NOT_CONTACT, DECEASED
    -- Tracking
    CreatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CreatedBy VARCHAR(50) DEFAULT 'system',
    UpdatedBy VARCHAR(50) DEFAULT 'system',
    -- Added 2018 for marketing (shouldn't be in this DB but here we are)
    MarketingOptIn BOOLEAN DEFAULT FALSE,
    MarketingOptInDt TIMESTAMP,
    -- Added 2020 for e-sign
    EsignConsent BOOLEAN DEFAULT FALSE,
    EsignConsentDt TIMESTAMP,
    EsignConsentIp VARCHAR(45)
);

CREATE INDEX idx_customers_guid ON customers(CustomerGuid);
CREATE INDEX idx_customers_name ON customers(LastName, FirstName);
CREATE INDEX idx_customers_ssn ON customers(SsnHash);

-- === CUSTOMER ADDRESSES ===
CREATE TABLE customer_addresses (
    AddressId SERIAL PRIMARY KEY,
    CustomerId INTEGER NOT NULL REFERENCES customers(CustomerId),
    AddressType VARCHAR(20) NOT NULL,  -- CURRENT, PREVIOUS, MAILING, PROPERTY
    AddressLine1 VARCHAR(100) NOT NULL,
    AddressLine2 VARCHAR(100),
    City VARCHAR(50) NOT NULL,
    StateCode CHAR(2) NOT NULL,
    ZipCode VARCHAR(10) NOT NULL,
    County VARCHAR(50),
    -- Added 2016 for geocoding
    Latitude NUMERIC(10,7),
    Longitude NUMERIC(10,7),
    CensusTract VARCHAR(20),  -- For HMDA
    -- Residency
    ResidencyStatus VARCHAR(20),  -- OWN, RENT, LIVE_WITH_FAMILY
    MonthlyHousingPayment NUMERIC(10,2),
    YearsAtAddress DECIMAL(4,1),
    -- Tracking
    IsPrimary BOOLEAN DEFAULT FALSE,
    EffectiveDt DATE,
    EndDt DATE,
    CreatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_addresses_customer ON customer_addresses(CustomerId);
CREATE INDEX idx_addresses_type ON customer_addresses(AddressType);

-- === CUSTOMER EMPLOYERS (employment history) ===
CREATE TABLE customer_employers (
    EmployerId SERIAL PRIMARY KEY,
    CustomerId INTEGER NOT NULL REFERENCES customers(CustomerId),
    EmployerName VARCHAR(100) NOT NULL,
    EmployerType VARCHAR(20),  -- W2, SELF_EMPLOYED, 1099, RETIRED, MILITARY
    Position VARCHAR(100),
    -- Contact
    EmployerAddress1 VARCHAR(100),
    EmployerAddress2 VARCHAR(100),
    EmployerCity VARCHAR(50),
    EmployerState CHAR(2),
    EmployerZip VARCHAR(10),
    EmployerPhone VARCHAR(20),
    -- Employment dates
    StartDate DATE,
    EndDate DATE,
    IsCurrent BOOLEAN DEFAULT TRUE,
    -- Income
    AnnualIncome NUMERIC(12,2),
    MonthlyIncome NUMERIC(12,2),
    PayFrequency VARCHAR(20),  -- WEEKLY, BI_WEEKLY, SEMI_MONTHLY, MONTHLY
    -- Verification
    IsVerified BOOLEAN DEFAULT FALSE,
    VerifiedBy VARCHAR(50),
    VerifiedDt TIMESTAMP,
    VerificationMethod VARCHAR(30),  -- VOE, W2, PAYSTUB, TAX_RETURN
    -- Added 2017 for self-employed
    BusinessType VARCHAR(30),  -- SOLE_PROP, LLC, S_CORP, C_CORP, PARTNERSHIP
    BusinessOwnershipPct NUMERIC(5,2),
    -- Tracking
    YearsInProfession DECIMAL(4,1),
    CreatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_employers_customer ON customer_employers(CustomerId);

-- === CUSTOMER INCOME (detailed income sources) ===
CREATE TABLE customer_income (
    IncomeId SERIAL PRIMARY KEY,
    CustomerId INTEGER NOT NULL REFERENCES customers(CustomerId),
    IncomeType VARCHAR(40) NOT NULL,  -- BASE_SALARY, OVERTIME, BONUS, COMMISSION, 
                                      -- SELF_EMPLOYMENT, RENTAL, INVESTMENT, 
                                      -- RETIREMENT, SOCIAL_SECURITY, ALIMONY, OTHER
    IncomeDesc VARCHAR(200),
    MonthlyAmount NUMERIC(12,2) NOT NULL,
    AnnualAmount NUMERIC(12,2),
    -- Verification
    IsVerified BOOLEAN DEFAULT FALSE,
    VerifiedBy VARCHAR(50),
    VerifiedDt TIMESTAMP,
    VerificationDocId VARCHAR(50),
    -- For AUS
    IsStable BOOLEAN DEFAULT TRUE,  -- AUS may flag variable income as unstable
    -- Added 2019 for gig economy
    Is1099Income BOOLEAN DEFAULT FALSE,
    -- Tracking
    EffectiveDt DATE,
    EndDt DATE,
    CreatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_income_customer ON customer_income(CustomerId);
CREATE INDEX idx_income_type ON customer_income(IncomeType);

-- === CUSTOMER ASSETS (bank accounts, investments, etc.) ===
CREATE TABLE customer_assets (
    AssetId SERIAL PRIMARY KEY,
    CustomerId INTEGER NOT NULL REFERENCES customers(CustomerId),
    AssetType VARCHAR(30) NOT NULL,  -- CHECKING, SAVINGS, MONEY_MARKET, CD, 
                                     -- INVESTMENT, RETIREMENT_401K, RETIREMENT_IRA,
                                     -- REAL_ESTATE, AUTO, OTHER
    InstitutionName VARCHAR(100),
    AccountNumber VARCHAR(30),  -- Partially masked
    AccountHolderName VARCHAR(100),
    -- Values
    CurrentBalance NUMERIC(14,2),
    AvailableBalance NUMERIC(14,2),  -- May differ from current (holds, etc.)
    -- For funds-to-close
    IsSourceOfFunds BOOLEAN DEFAULT FALSE,  -- Will this account be used for closing?
    FundsToCloseAmount NUMERIC(14,2),  -- Amount being used from this account
    -- Verification
    IsVerified BOOLEAN DEFAULT FALSE,
    VerifiedBy VARCHAR(50),
    VerifiedDt TIMESTAMP,
    VerificationDocId VARCHAR(50),
    -- Added 2016 for VOM (verification of mortgage)
    SeasoningMonths INTEGER,  -- How long has the money been in this account?
    LargeDepositFlag BOOLEAN DEFAULT FALSE,
    LargeDepositAmount NUMERIC(14,2),
    -- Tracking
    CreatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_assets_customer ON customer_assets(CustomerId);
CREATE INDEX idx_assets_type ON customer_assets(AssetType);

-- === CUSTOMER DEMOGRAPHICS (HMDA-specific, collected separately for compliance) ===
CREATE TABLE customer_demographics (
    DemographicsId SERIAL PRIMARY KEY,
    CustomerId INTEGER NOT NULL REFERENCES customers(CustomerId),
    LoanNumber VARCHAR(20),  -- Which loan these demographics were collected for
    -- Race (2018+ format, can be multiple, stored as pipe-separated)
    Race VARCHAR(100),  -- AMERICAN_INDIAN|ASIAN|ASIAN_INDIAN|CHINESE|FILIPINO|
                         -- JAPANESE|KOREAN|VIETNAMESE|OTHER_ASIAN|BLACK|PACIFIC_ISLANDER|
                         -- NATIVE_HAWAIIAN|GUAMANIAN|SAMOAN|OTHER_PI|WHITE|OTHER
    RaceObserved VARCHAR(20),  -- VISUAL_SURVEY|NOT_OBSERVED|NOT_APPLICABLE
    -- Ethnicity
    Ethnicity VARCHAR(50),  -- HISPANIC|MEXICAN|PUERTO_RICAN|CUBAN|OTHER_HISPANIC|NOT_HISPANIC|JOINT
    EthnicityObserved VARCHAR(20),
    -- Sex
    Sex VARCHAR(20),  -- MALE|FEMALE|JOINT|FREE_FORM
    SexObserved VARCHAR(20),
    -- Age
    AgeAtApplication INTEGER,
    -- Collection method
    CollectionMethod VARCHAR(20),  -- BORROWER_PROVIDED|VISUAL_SURVEY|NOT_COLLECTED
    CollectionDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Tracking
    CollectedBy VARCHAR(50),
    CreatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    -- Added 2020 for data quality
    DataQualityScore INTEGER,  -- 0-100
    DataQualityIssues TEXT
);

CREATE INDEX idx_demographics_customer ON customer_demographics(CustomerId);
CREATE INDEX idx_demographics_loan ON customer_demographics(LoanNumber);

-- === CUSTOMER RELATIONSHIPS (co-borrowers, etc.) ===
CREATE TABLE customer_relationships (
    RelationshipId SERIAL PRIMARY KEY,
    PrimaryCustomerId INTEGER NOT NULL REFERENCES customers(CustomerId),
    RelatedCustomerId INTEGER NOT NULL REFERENCES customers(CustomerId),
    RelationshipType VARCHAR(30) NOT NULL,  -- SPOUSE, CO_BORROWER, CO_SIGNER, POWER_OF_ATTORNEY, GUARANTOR
    -- For co-borrower
    IsCoBorrower BOOLEAN DEFAULT FALSE,
    LoanNumber VARCHAR(20),  -- Which loan they're co-borrowing on
    -- Tracking
    CreatedDt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CreatedBy VARCHAR(50),
    -- Added 2018 for HMDA
    JointApplication BOOLEAN DEFAULT FALSE
);

CREATE INDEX idx_relationships_primary ON customer_relationships(PrimaryCustomerId);
CREATE INDEX idx_relationships_related ON customer_relationships(RelatedCustomerId);
