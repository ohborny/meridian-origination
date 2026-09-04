-- ============================================================================
-- los_customer Seed Data
-- Sample borrowers for demo and testing
-- ============================================================================

\c los_customer;

-- === SAMPLE CUSTOMERS ===
INSERT INTO customers (CustomerGuid, FirstName, LastName, SsnHash, SsnLast4, DateOfBirth, EmailAddress, PhoneNumber, CustomerStatus, CreatedBy) VALUES
('CUST-001-AAAA-BBBB-CCCC', 'William', 'Hartwell', 'a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2', '4321', '1982-05-14', 'whartwell@example.com', '602-555-2001', 'ACTIVE', 'system'),
('CUST-002-AAAA-BBBB-CCCC', 'Patricia', 'Delgado', 'b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3', '8765', '1979-11-22', 'pdelgado@example.com', '512-555-2002', 'ACTIVE', 'system'),
('CUST-003-AAAA-BBBB-CCCC', 'Robert', 'Kowalski', 'c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4', '1290', '1985-03-08', 'rkowalski@example.com', '303-555-2003', 'ACTIVE', 'system'),
('CUST-004-AAAA-BBBB-CCCC', 'Jennifer', 'Matsuda', 'd4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5', '5678', '1988-07-30', 'jmatsuda@example.com', '619-555-2004', 'ACTIVE', 'system'),
('CUST-005-AAAA-BBBB-CCCC', 'Christopher', 'OBrien', 'e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6', '9012', '1975-01-15', 'cobrien@example.com', '404-555-2005', 'ACTIVE', 'system'),
('CUST-006-AAAA-BBBB-CCCC', 'Maria', 'Santos', 'f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1', '3456', '1990-09-12', 'msantos@example.com', '813-555-2006', 'ACTIVE', 'system'),
('CUST-007-AAAA-BBBB-CCCC', 'David', 'Goldberg', 'a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2', '7890', '1972-12-03', 'dgoldberg@example.com', '704-555-2007', 'ACTIVE', 'system'),
('CUST-008-AAAA-BBBB-CCCC', 'Linda', 'Carter', 'b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3', '2345', '1983-06-18', 'lcarter@example.com', '612-555-2008', 'ACTIVE', 'system');

-- === ADDRESSES ===
INSERT INTO customer_addresses (CustomerId, AddressType, AddressLine1, City, StateCode, ZipCode, County, ResidencyStatus, MonthlyHousingPayment, YearsAtAddress, IsPrimary) VALUES
(1, 'CURRENT', '1428 W Desert Willow Dr', 'Phoenix', 'AZ', '85045', 'Maricopa', 'OWN', 1850.00, 4.0, TRUE),
(2, 'CURRENT', '3307 E Cesar Chavez St', 'Austin', 'TX', '78702', 'Travis', 'RENT', 1650.00, 2.5, TRUE),
(3, 'CURRENT', '2155 Larimer St Apt 12', 'Denver', 'CO', '80205', 'Denver', 'RENT', 1450.00, 1.5, TRUE),
(4, 'CURRENT', '4827 Voltaire St', 'San Diego', 'CA', '92107', 'San Diego', 'OWN', 2200.00, 6.0, TRUE),
(5, 'CURRENT', '1847 Peachtree Walk Dr', 'Atlanta', 'GA', '30309', 'Fulton', 'OWN', 1950.00, 3.5, TRUE),
(6, 'CURRENT', '912 Bayshore Blvd', 'Tampa', 'FL', '33606', 'Hillsborough', 'RENT', 1200.00, 1.0, TRUE),
(7, 'CURRENT', '4401 Sharon Rd', 'Charlotte', 'NC', '28211', 'Mecklenburg', 'OWN', 2100.00, 8.0, TRUE),
(8, 'CURRENT', '875 Marquette Ave', 'Minneapolis', 'MN', '55402', 'Hennepin', 'RENT', 1350.00, 2.0, TRUE);

-- === EMPLOYERS ===
INSERT INTO customer_employers (CustomerId, EmployerName, EmployerType, Position, EmployerCity, EmployerState, EmployerPhone, StartDate, IsCurrent, AnnualIncome, MonthlyIncome, PayFrequency, IsVerified, YearsInProfession) VALUES
(1, 'Honeywell Aerospace', 'W2', 'Senior Systems Engineer', 'Phoenix', 'AZ', '602-555-3001', '2018-06-01', TRUE, 125000.00, 10416.67, 'SEMI_MONTHLY', TRUE, 12.0),
(2, 'Dell Technologies', 'W2', 'Product Manager', 'Austin', 'TX', '512-555-3002', '2019-03-15', TRUE, 110000.00, 9166.67, 'BI_WEEKLY', TRUE, 8.0),
(3, 'Lockheed Martin', 'W2', 'Mechanical Engineer', 'Denver', 'CO', '303-555-3003', '2017-01-20', TRUE, 95000.00, 7916.67, 'BI_WEEKLY', FALSE, 10.0),
(4, 'Qualcomm Inc', 'W2', 'Software Developer', 'San Diego', 'CA', '619-555-3004', '2016-09-10', TRUE, 135000.00, 11250.00, 'SEMI_MONTHLY', TRUE, 9.0),
(5, 'Delta Airlines', 'W2', 'Operations Manager', 'Atlanta', 'GA', '404-555-3005', '2010-04-01', TRUE, 98000.00, 8166.67, 'BI_WEEKLY', TRUE, 15.0),
(6, 'Bayfront Health', 'W2', 'Registered Nurse', 'Tampa', 'FL', '813-555-3006', '2021-05-01', TRUE, 72000.00, 6000.00, 'BI_WEEKLY', FALSE, 4.0),
(7, 'Bank of America', 'W2', 'VP Finance', 'Charlotte', 'NC', '704-555-3007', '2008-07-15', TRUE, 165000.00, 13750.00, 'SEMI_MONTHLY', TRUE, 18.0),
(8, 'Target Corp', 'W2', 'Supply Chain Analyst', 'Minneapolis', 'MN', '612-555-3008', '2018-02-01', TRUE, 82000.00, 6833.33, 'BI_WEEKLY', TRUE, 7.0);

-- === INCOME ===
INSERT INTO customer_income (CustomerId, IncomeType, MonthlyAmount, AnnualAmount, IsVerified, IsStable) VALUES
(1, 'BASE_SALARY', 10416.67, 125000.00, TRUE, TRUE),
(1, 'BONUS', 2000.00, 24000.00, FALSE, TRUE),
(2, 'BASE_SALARY', 9166.67, 110000.00, TRUE, TRUE),
(3, 'BASE_SALARY', 7916.67, 95000.00, FALSE, TRUE),
(3, 'OVERTIME', 800.00, 9600.00, FALSE, FALSE),
(4, 'BASE_SALARY', 11250.00, 135000.00, TRUE, TRUE),
(5, 'BASE_SALARY', 8166.67, 98000.00, TRUE, TRUE),
(6, 'BASE_SALARY', 6000.00, 72000.00, FALSE, TRUE),
(7, 'BASE_SALARY', 13750.00, 165000.00, TRUE, TRUE),
(7, 'BONUS', 3000.00, 36000.00, TRUE, TRUE),
(8, 'BASE_SALARY', 6833.33, 82000.00, TRUE, TRUE);

-- === ASSETS ===
INSERT INTO customer_assets (CustomerId, AssetType, InstitutionName, AccountNumber, CurrentBalance, AvailableBalance, IsSourceOfFunds, IsVerified, SeasoningMonths) VALUES
(1, 'CHECKING', 'Chase Bank', '****1234', 45000.00, 45000.00, TRUE, TRUE, 36),
(1, 'SAVINGS', 'Chase Bank', '****5678', 85000.00, 85000.00, FALSE, TRUE, 48),
(2, 'CHECKING', 'Bank of America', '****9876', 32000.00, 32000.00, TRUE, TRUE, 24),
(3, 'CHECKING', 'Wells Fargo', '****2468', 18000.00, 18000.00, TRUE, FALSE, 12),
(4, 'CHECKING', 'USAA', '****1357', 67000.00, 67000.00, TRUE, TRUE, 60),
(4, 'INVESTMENT', 'Vanguard', '****8080', 120000.00, 120000.00, FALSE, TRUE, 48),
(5, 'CHECKING', 'SunTrust', '****3690', 28000.00, 28000.00, TRUE, TRUE, 18),
(6, 'CHECKING', 'Wells Fargo', '****7531', 15000.00, 15000.00, TRUE, FALSE, 6),
(7, 'CHECKING', 'Bank of America', '****1590', 95000.00, 95000.00, TRUE, TRUE, 72),
(7, 'RETIREMENT_401K', 'Fidelity', '****2640', 380000.00, 380000.00, FALSE, TRUE, 120),
(8, 'CHECKING', 'US Bank', '****8420', 22000.00, 22000.00, TRUE, TRUE, 15);

-- === DEMOGRAPHICS (HMDA) ===
INSERT INTO customer_demographics (CustomerId, LoanNumber, Race, RaceObserved, Ethnicity, EthnicityObserved, Sex, SexObserved, CollectionMethod, CollectedBy) VALUES
(1, NULL, 'WHITE', 'NOT_OBSERVED', 'NOT_HISPANIC', 'NOT_OBSERVED', 'MALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system'),
(2, NULL, 'WHITE|ASIAN', 'NOT_OBSERVED', 'NOT_HISPANIC', 'NOT_OBSERVED', 'FEMALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system'),
(3, NULL, 'WHITE', 'NOT_OBSERVED', 'NOT_HISPANIC', 'NOT_OBSERVED', 'MALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system'),
(4, NULL, 'ASIAN', 'NOT_OBSERVED', 'NOT_HISPANIC', 'NOT_OBSERVED', 'FEMALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system'),
(5, NULL, 'WHITE', 'NOT_OBSERVED', 'NOT_HISPANIC', 'NOT_OBSERVED', 'MALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system'),
(6, NULL, 'WHITE', 'NOT_OBSERVED', 'HISPANIC', 'NOT_OBSERVED', 'FEMALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system'),
(7, NULL, 'WHITE', 'NOT_OBSERVED', 'NOT_HISPANIC', 'NOT_OBSERVED', 'MALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system'),
(8, NULL, 'WHITE', 'NOT_OBSERVED', 'NOT_HISPANIC', 'NOT_OBSERVED', 'FEMALE', 'NOT_OBSERVED', 'BORROWER_PROVIDED', 'system');
