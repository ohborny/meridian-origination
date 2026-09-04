-- ============================================================================
-- los_core Seed Data
-- Initial reference data for loan products, branches, and loan officers
-- ============================================================================

\c los_core;

-- === BRANCHES ===
INSERT INTO branches (branch_code, branch_name, branch_addr1, branch_city, branch_state, branch_zip, branch_phone, nmls_id, region_code, is_active) VALUES
('001', 'First Meridian - Downtown', '100 Commerce Plaza', 'Phoenix', 'AZ', '85001', '602-555-0100', 'BR001234', 'WEST', TRUE),
('002', 'First Meridian - Scottsdale', '4400 N Scottsdale Rd', 'Scottsdale', 'AZ', '85251', '480-555-0200', 'BR001235', 'WEST', TRUE),
('003', 'First Meridian - Austin', '500 W 6th St', 'Austin', 'TX', '78701', '512-555-0300', 'BR001236', 'CENTRAL', TRUE),
('004', 'First Meridian - Denver', '1700 Lincoln St', 'Denver', 'CO', '80203', '303-555-0400', 'BR001237', 'WEST', TRUE),
('005', 'First Meridian - Atlanta', '1150 Peachtree St', 'Atlanta', 'GA', '30309', '404-555-0500', 'BR001238', 'EAST', TRUE),
('006', 'First Meridian - Charlotte', '201 S Tryon St', 'Charlotte', 'NC', '28202', '704-555-0600', 'BR001239', 'EAST', TRUE),
('007', 'First Meridian - San Diego', '600 B St', 'San Diego', 'CA', '92101', '619-555-0700', 'BR001240', 'WEST', TRUE),
('008', 'First Meridian - Tampa', '101 E Kennedy Blvd', 'Tampa', 'FL', '33602', '813-555-0800', 'BR001241', 'EAST', TRUE),
('009', 'First Meridian - Minneapolis', '55 W 5th St', 'Minneapolis', 'MN', '55402', '612-555-0900', 'BR001242', 'CENTRAL', TRUE),
('010', 'First Meridian - Sacramento', '910 K St', 'Sacramento', 'CA', '95814', '916-555-1000', 'BR001243', 'WEST', TRUE),
('011', 'First Meridian - Dallas', '3000 Oak Lawn Ave', 'Dallas', 'TX', '75219', '214-555-1100', 'BR001244', 'CENTRAL', TRUE),
('012', 'First Meridian - Houston', '1400 Smith St', 'Houston', 'TX', '77002', '713-555-1200', 'BR001245', 'CENTRAL', TRUE);

-- === LOAN OFFICERS ===
INSERT INTO loan_officers (lo_code, first_name, last_name, nmls_id, branch_id, email_addr, phone_num, hire_date, is_active, commission_rate) VALUES
('LO001', 'Patricia', 'Chen', 'NMLS-100001', 1, 'pchen@meridianlending.example', '602-555-1001', '2015-03-15', TRUE, 1.00),
('LO002', 'Robert', 'Okafor', 'NMLS-100002', 1, 'rokafor@meridianlending.example', '602-555-1002', '2017-07-01', TRUE, 1.00),
('LO003', 'Susan', 'Walsh', 'NMLS-100003', 2, 'swalsh@meridianlending.example', '480-555-1003', '2013-11-20', TRUE, 1.25),
('LO004', 'Michael', 'Torres', 'NMLS-100004', 3, 'mtorres@meridianlending.example', '512-555-1004', '2018-01-10', TRUE, 0.75),
('LO005', 'Jennifer', 'Kim', 'NMLS-100005', 4, 'jkim@meridianlending.example', '303-555-1005', '2016-05-22', TRUE, 1.00),
('LO006', 'David', 'Patel', 'NMLS-100006', 5, 'dpatel@meridianlending.example', '404-555-1006', '2014-09-15', TRUE, 1.00),
('LO007', 'Maria', 'Gonzalez', 'NMLS-100007', 6, 'mgonzalez@meridianlending.example', '704-555-1007', '2019-02-28', TRUE, 0.85),
('LO008', 'James', 'Thompson', 'NMLS-100008', 7, 'jthompson@meridianlending.example', '619-555-1008', '2012-04-01', TRUE, 1.25),
('LO009', 'Linda', 'Nguyen', 'NMLS-100009', 8, 'lnguyen@meridianlending.example', '813-555-1009', '2017-10-12', TRUE, 1.00),
('LO010', 'Christopher', 'Brooks', 'NMLS-100010', 9, 'cbrooks@meridianlending.example', '612-555-1010', '2015-08-03', TRUE, 1.00),
('LO011', 'Amanda', 'Foster', 'NMLS-100011', 10, 'afoster@meridianlending.example', '916-555-1011', '2020-01-15', TRUE, 0.75),
('LO012', 'Daniel', 'Murray', 'NMLS-100012', 11, 'dmurray@meridianlending.example', '214-555-1012', '2018-06-20', TRUE, 1.00),
('LO013', 'Sarah', 'Anderson', 'NMLS-100013', 12, 'sanderson@meridianlending.example', '713-555-1013', '2016-03-08', TRUE, 1.00),
('LO014', 'Kevin', 'Murphy', 'NMLS-100014', 2, 'kmurphy@meridianlending.example', '480-555-1014', '2021-09-01', TRUE, 0.85),
('LO015', 'Rebecca', 'Singh', 'NMLS-100015', 3, 'rsingh@meridianlending.example', '512-555-1015', '2019-11-25', TRUE, 1.00);

-- === LOAN PRODUCTS ===
INSERT INTO loan_products (product_code, product_name, product_type, term_months, amortization_type, min_loan_amt, max_loan_amt, min_fico, max_ltv, max_dti, is_active, investor_code) VALUES
('CONF30', 'Conventional 30-Year Fixed', 'CONVENTIONAL', 360, 'FIXED', 50000.00, 766550.00, 620, 97.00, 50.00, TRUE, 'FNM'),
('CONF15', 'Conventional 15-Year Fixed', 'CONVENTIONAL', 180, 'FIXED', 50000.00, 766550.00, 620, 95.00, 50.00, TRUE, 'FNM'),
('CONF20', 'Conventional 20-Year Fixed', 'CONVENTIONAL', 240, 'FIXED', 50000.00, 766550.00, 620, 95.00, 50.00, TRUE, 'FNM'),
('CONF_ARM_5_1', 'Conventional 5/1 ARM', 'CONVENTIONAL', 360, 'ARM', 50000.00, 766550.00, 640, 95.00, 45.00, TRUE, 'FNM'),
('CONF_ARM_7_1', 'Conventional 7/1 ARM', 'CONVENTIONAL', 360, 'ARM', 50000.00, 766550.00, 640, 95.00, 45.00, TRUE, 'FNM'),
('CONF_CASHOUT', 'Conventional Cash-Out Refi', 'CONVENTIONAL', 360, 'FIXED', 50000.00, 766550.00, 640, 80.00, 45.00, TRUE, 'FNM'),
('JUMBO30', 'Jumbo 30-Year Fixed', 'JUMBO', 360, 'FIXED', 766551.00, 3000000.00, 700, 90.00, 43.00, TRUE, 'JPM'),
('JUMBO15', 'Jumbo 15-Year Fixed', 'JUMBO', 180, 'FIXED', 766551.00, 3000000.00, 700, 90.00, 43.00, TRUE, 'JPM'),
('JUMBO_ARM_7_1', 'Jumbo 7/1 ARM', 'JUMBO', 360, 'ARM', 766551.00, 3000000.00, 720, 85.00, 43.00, TRUE, 'JPM'),
('FHA30', 'FHA 30-Year Fixed', 'FHA', 360, 'FIXED', 50000.00, 766550.00, 580, 96.50, 56.99, TRUE, 'GNMA'),
('FHA15', 'FHA 15-Year Fixed', 'FHA', 180, 'FIXED', 50000.00, 766550.00, 580, 96.50, 56.99, TRUE, 'GNMA'),
('FHA_ARM_5_1', 'FHA 5/1 ARM', 'FHA', 360, 'ARM', 50000.00, 766550.00, 580, 96.50, 56.99, TRUE, 'GNMA'),
('FHA_STREAMLINE', 'FHA Streamline Refinance', 'FHA', 360, 'FIXED', 50000.00, 766550.00, 580, 97.75, 56.99, TRUE, 'GNMA'),
('VA30', 'VA 30-Year Fixed', 'VA', 360, 'FIXED', 50000.00, 766550.00, 580, 100.00, 41.00, TRUE, 'GNMA'),
('VA15', 'VA 15-Year Fixed', 'VA', 180, 'FIXED', 50000.00, 766550.00, 580, 100.00, 41.00, TRUE, 'GNMA'),
('VA_CASHOUT', 'VA Cash-Out Refinance', 'VA', 360, 'FIXED', 50000.00, 766550.00, 580, 100.00, 41.00, TRUE, 'GNMA'),
('USDA30', 'USDA Rural Development 30-Year', 'USDA', 360, 'FIXED', 50000.00, 766550.00, 640, 100.00, 44.00, TRUE, 'GNMA'),
('NONQM_BANK', 'Non-QM Bank Statement', 'NON_QM', 360, 'FIXED', 100000.00, 2000000.00, 660, 80.00, 55.00, TRUE, 'ANG'),
('NONQM_DSCR', 'Non-QM DSCR Investment', 'NON_QM', 360, 'FIXED', 100000.00, 1500000.00, 660, 80.00, 55.00, TRUE, 'ANG');

-- === RATE SHEET (sample) ===
INSERT INTO rate_sheet (product_id, rate_date, base_rate, point_0_rate, point_0_25, point_0_50, point_0_75, point_1_00, point_1_25, point_1_50, point_1_75, point_2_00, lender_credit_0_50, lender_credit_1_00, lender_credit_1_50, lender_credit_2_00, is_active, effective_dt) VALUES
(1, CURRENT_DATE, 6.875, 6.875, 6.750, 6.625, 6.500, 6.375, 6.250, 6.125, 6.000, 5.875, 7.000, 7.125, 7.250, 7.375, TRUE, CURRENT_TIMESTAMP),
(2, CURRENT_DATE, 6.250, 6.250, 6.125, 6.000, 5.875, 5.750, 5.625, 5.500, 5.375, 5.250, 6.375, 6.500, 6.625, 6.750, TRUE, CURRENT_TIMESTAMP),
(4, CURRENT_DATE, 6.500, 6.500, 6.375, 6.250, 6.125, 6.000, 5.875, 5.750, 5.625, 5.500, 6.625, 6.750, 6.875, 7.000, TRUE, CURRENT_TIMESTAMP),
(7, CURRENT_DATE, 7.000, 7.000, 6.875, 6.750, 6.625, 6.500, 6.375, 6.250, 6.125, 6.000, 7.125, 7.250, 7.375, 7.500, TRUE, CURRENT_TIMESTAMP),
(10, CURRENT_DATE, 6.500, 6.500, 6.375, 6.250, 6.125, 6.000, 5.875, 5.750, 5.625, 5.500, 6.625, 6.750, 6.875, 7.000, TRUE, CURRENT_TIMESTAMP),
(14, CURRENT_DATE, 6.250, 6.250, 6.125, 6.000, 5.875, 5.750, 5.625, 5.500, 5.375, 5.250, 6.375, 6.500, 6.625, 6.750, TRUE, CURRENT_TIMESTAMP),
(17, CURRENT_DATE, 6.750, 6.750, 6.625, 6.500, 6.375, 6.250, 6.125, 6.000, 5.875, 5.750, 6.875, 7.000, 7.125, 7.250, TRUE, CURRENT_TIMESTAMP),
(18, CURRENT_DATE, 7.500, 7.500, 7.375, 7.250, 7.125, 7.000, 6.875, 6.750, 6.625, 6.500, 7.625, 7.750, 7.875, 8.000, TRUE, CURRENT_TIMESTAMP);

-- === LOAN NUMBER SEQUENCE ===
-- Used by Origination module to generate unique loan numbers
-- Starts at 10000 because someone decided the first loans should be 5-digit numbers
CREATE SEQUENCE IF NOT EXISTS loan_number_seq START 10000;

-- === AUS RESULTS TABLE (created by Underwriting module at runtime) ===
-- This is here for reference. The Underwriting module creates it with IF NOT EXISTS.
CREATE TABLE IF NOT EXISTS aus_results (
    aus_id SERIAL PRIMARY KEY,
    loan_number VARCHAR(20) NOT NULL,
    engine VARCHAR(20) NOT NULL,
    result VARCHAR(40) NOT NULL,
    findings TEXT,
    run_dt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    run_by VARCHAR(50),
    credit_score INTEGER,
    dti NUMERIC(8,2),
    ltv NUMERIC(8,2)
);
