-- ============================================================================
-- los_compliance Seed Data
-- State-specific disclosure rules for all 50 states + DC + PR
-- This is NOT exhaustive. It's representative of the types of rules that exist.
-- The real system has 300+ rules. This seed has ~60 key ones.
-- ============================================================================

\c los_compliance;

-- === STATE DISCLOSURE RULES ===
-- Format: state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation

-- California
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('CA', 'CA Mortgage Loan Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'California requires a Mortgage Loan Disclosure Statement for all residential mortgage loans', 'CA_MLDS', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Cal. Fin. Code § 50204'),
('CA', 'CA Prepayment Penalty Disclosure', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE', 'Disclosure required if loan has prepayment penalty. CA limits prepayment penalties on owner-occupied 1-4 unit properties', 'CA_PREPAY_DISCLOSURE', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Cal. Fin. Code § 4970'),
('CA', 'CA Fee Cap - Origination', 'FEE_CAP', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'CA caps total loan origination fees. Points and fees cannot exceed 6% of total loan amount for high-cost loans', NULL, 'N/A', NULL, NULL, 6.00, TRUE, '2010-01-01', 'Cal. Fin. Code § 4970.5'),
('CA', 'CA High Cost Loan Notice', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'Required for high-cost loans per CA law. Must be delivered at least 3 business days before consummation', 'CA_HIGH_COST_NOTICE', 'WITHIN_3_DAYS', 3, NULL, NULL, TRUE, '2010-01-01', 'Cal. Fin. Code § 4973'),
('CA', 'CA SB 1152 - Fair Lending', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'Fair lending disclosure required for all residential mortgage loan applications', 'CA_FAIR_LENDING', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2020-01-01', 'Cal. Fin. Code § 22100');

-- Texas
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('TX', 'TX Home Equity Disclosure (A6)', 'DISCLOSURE', 'CONVENTIONAL', 'CASHOUT_REFI', 'Texas Constitution Article XVI Section 50(a)(6) home equity loan disclosure. Must be delivered at least 12 days before closing', 'TX_A6_DISCLOSURE', 'WITHIN_3_DAYS', 12, NULL, NULL, TRUE, '2018-01-01', 'Tex. Const. Art. XVI § 50(a)(6)'),
('TX', 'TX Home Equity Fee Cap', 'FEE_CAP', 'CONVENTIONAL', 'CASHOUT_REFI', 'TX caps all fees on home equity loans at 2% of loan amount (was 3%, changed 2018)', NULL, 'N/A', NULL, NULL, 2.00, TRUE, '2018-01-01', 'Tex. Const. Art. XVI § 50(a)(6)(E)'),
('TX', 'TX Home Equity LTV Cap', 'DISCLOSURE', 'CONVENTIONAL', 'CASHOUT_REFI', 'TX limits total debt on homestead to 80% of fair market value', 'TX_LTV_CERT', 'AT_CLOSING', 0, NULL, NULL, TRUE, '2018-01-01', 'Tex. Const. Art. XVI § 50(a)(6)(B)'),
('TX', 'TX Cash-Out Waiting Period', 'WAITING_PERIOD', 'CONVENTIONAL', 'CASHOUT_REFI', 'TX requires a 12-day waiting period between loan disclosure and closing for home equity loans. Also requires 1-year cooling off before refinance', NULL, 'N/A', 12, NULL, NULL, TRUE, '2018-01-01', 'Tex. Const. Art. XVI § 50(a)(6)(M)'),
('TX', 'TX Mortgage Broker Fee Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'TX requires mortgage broker fee disclosure at application', 'TX_BROKER_FEE_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Tex. Fin. Code § 156.003');

-- New York
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('NY', 'NY High Cost Loan Disclosure', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'NY requires special disclosure for high-cost home loans per NY Banking Law Section 6-l', 'NY_HIGH_COST_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'NY Banking Law § 6-l'),
('NY', 'NY Fee Cap - High Cost', 'FEE_CAP', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'NY caps points and fees at 5% for high-cost loans (threshold: APR exceeds APOR by 6.5%+ on first liens)', NULL, 'N/A', NULL, NULL, 5.00, TRUE, '2010-01-01', 'NY Banking Law § 6-l(1)(c)'),
('NY', 'NY Mortgage Broker Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'NY requires mortgage broker disclosure of compensation and affiliations', 'NY_BROKER_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'NY Banking Law § 595-a'),
('NY', 'NY CEMA Disclosure', 'DISCLOSURE', 'CONVENTIONAL|JUMBO', 'REFINANCE', 'NY CEMA (Consolidation Extension Modification Agreement) disclosure for refinance transactions', 'NY_CEMA_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2015-01-01', 'NY Real Property Law § 258-a');

-- Florida
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('FL', 'FL Mortgage Broker Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'FL requires mortgage broker disclosure of fees and compensation', 'FL_BROKER_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Fla. Stat. § 494.00367'),
('FL', 'FL High Cost Loan Notice', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'FL requires special notice for high-cost home loans', 'FL_HIGH_COST_NOTICE', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Fla. Stat. § 494.00792'),
('FL', 'FL Fee Cap - High Cost', 'FEE_CAP', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'FL caps points and fees at 5% for high-cost loans', NULL, 'N/A', NULL, NULL, 5.00, TRUE, '2010-01-01', 'Fla. Stat. § 494.00792');

-- Minnesota
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('MN', 'MN Mortgage Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'MN requires a specific mortgage disclosure statement', 'MN_MORTGAGE_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Minn. Stat. § 58.13'),
('MN', 'MN Prepayment Penalty Notice', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE', 'MN requires prepayment penalty disclosure. Penalties prohibited after 36 months', 'MN_PREPAY_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Minn. Stat. § 58.13(1)(c)'),
('MN', 'MN Fee Cap', 'FEE_CAP', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'MN caps total points and fees at 5% for high-cost loans', NULL, 'N/A', NULL, NULL, 5.00, TRUE, '2010-01-01', 'Minn. Stat. § 58.13(1)(d)');

-- Maryland
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('MD', 'MD Mortgage Lender Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'MD requires mortgage lender disclosure of fees and compensation', 'MD_LENDER_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Md. Code, Fin. Reg. § 11-505'),
('MD', 'MD High Cost Loan Disclosure', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'MD requires special disclosure for high-cost home loans', 'MD_HIGH_COST_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Md. Code, Fin. Reg. § 12-922'),
('MD', 'MD Fee Cap - High Cost', 'FEE_CAP', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'MD caps points and fees at 5% for high-cost loans', NULL, 'N/A', NULL, NULL, 5.00, TRUE, '2010-01-01', 'Md. Code, Fin. Reg. § 12-922'),
('MD', 'MD Prepayment Penalty Restriction', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE', 'MD restricts prepayment penalties on certain loans', 'MD_PREPAY_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Md. Code, Fin. Reg. § 12-921');

-- Massachusetts
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('MA', 'MA Mortgage Broker Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'MA requires mortgage broker disclosure', 'MA_BROKER_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Mass. Gen. Laws ch. 255E § 7'),
('MA', 'MA High Cost Mortgage Disclosure', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'MA requires disclosure for high-cost home mortgage loans', 'MA_HIGH_COST_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Mass. Gen. Laws ch. 183C § 4'),
('MA', 'MA Fee Cap - High Cost', 'FEE_CAP', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'MA caps points and fees at 5% for high-cost loans', NULL, 'N/A', NULL, NULL, 5.00, TRUE, '2010-01-01', 'Mass. Gen. Laws ch. 183C § 4');

-- North Carolina
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('NC', 'NC High Cost Loan Disclosure', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'NC requires special disclosure for high-cost home loans', 'NC_HIGH_COST_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'N.C. Gen. Stat. § 24-1.1E'),
('NC', 'NC Fee Cap - High Cost', 'FEE_CAP', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'NC caps points and fees at 5% for high-cost loans', NULL, 'N/A', NULL, NULL, 5.00, TRUE, '2010-01-01', 'N.C. Gen. Stat. § 24-1.1E'),
('NC', 'NC Prepayment Penalty Restriction', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE', 'NC restricts prepayment penalties on loans under $150,000', 'NC_PREPAY_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'N.C. Gen. Stat. § 24-1.1F');

-- === FEDERAL DISCLOSURE RULES (stored as state_code = 'FED') ===
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('FED', 'TRID - Loan Estimate', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO|NON_QM', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'TILA-RESPA Integrated Disclosure - Loan Estimate must be delivered within 3 business days of application', 'LE', 'WITHIN_3_DAYS', 3, NULL, NULL, TRUE, '2015-10-03', '12 CFR § 1026.19(e)'),
('FED', 'TRID - Closing Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO|NON_QM', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'TILA-RESPA Integrated Disclosure - Closing Disclosure must be received by borrower at least 3 business days before consummation', 'CD', 'N/A', 3, NULL, NULL, TRUE, '2015-10-03', '12 CFR § 1026.19(f)'),
('FED', 'HOEPA - High Cost Mortgage', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'Home Ownership and Equity Protection Act disclosure for high-cost mortgages. Triggered if APR exceeds APOR by 6.5%+ (first lien)', 'HOEPA_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2014-01-01', '12 CFR § 1026.32'),
('FED', 'HPML - Higher Priced Mortgage Loan', 'DISCLOSURE', 'CONVENTIONAL|FHA', 'PURCHASE|REFINANCE', 'Higher-Priced Mortgage Loan disclosure. Triggered if APR exceeds APOR by 1.5%+ (first lien). Requires escrow for 5 years', 'HPML_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2014-01-01', '12 CFR § 1026.35'),
('FED', 'QM - Ability to Repay', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'Ability to Repay / Qualified Mortgage verification required. DTI must be verified (43% for general QM, but GSE QM allows higher with AUS)', 'ATR_QM_CERT', 'AT_CLOSING', 0, NULL, NULL, TRUE, '2014-01-01', '12 CFR § 1026.43'),
('FED', 'HMDA - Data Collection', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'HMDA data collection required for all applications. Includes race, ethnicity, sex, income, and loan pricing data', 'HMDA_DATA', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2018-01-01', '12 CFR § 1003.4'),
('FED', 'ECOA - Equal Credit Opportunity', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO|NON_QM', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'Equal Credit Opportunity Act notice required. Prohibits discrimination on basis of race, color, religion, national origin, sex, marital status, age, or receipt of public assistance', 'ECOA_NOTICE', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', '15 U.S.C. § 1691'),
('FED', 'SCRA - Servicemembers Civil Relief Act', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'SCRA check required for all borrowers. Provides interest rate cap (6%) and foreclosure protection for active-duty military', 'SCRA_CHECK', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2023-01-01', '50 U.S.C. § 3901'),
('FED', 'Appraisal Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'Appraisal disclosure required. Borrower must receive copy of appraisal at least 3 business days before consummation', 'APPRAISAL_DISC', 'WITHIN_3_DAYS', 3, NULL, NULL, TRUE, '2014-01-01', '12 CFR § 1002.14'),
('FED', 'Affiliated Business Arrangement', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA|USDA|JUMBO', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'Required when lender has affiliated business arrangement for settlement services. Must be delivered at application', 'ABA_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', '12 CFR § 1024.15');

-- === ADDITIONAL STATES (abbreviated for demo) ===
INSERT INTO state_disclosure_rules (state_code, rule_name, rule_type, loan_types, loan_purposes, rule_desc, required_document, timing_requirement, timing_days, max_fee_amount, max_fee_pct, is_active, effective_dt, regulation_citation) VALUES
('CO', 'CO Mortgage Broker Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'CO requires mortgage broker disclosure', 'CO_BROKER_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Colo. Rev. Stat. § 12-61-904'),
('AZ', 'AZ Mortgage Broker Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'AZ requires mortgage broker disclosure', 'AZ_BROKER_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Ariz. Rev. Stat. § 6-913'),
('GA', 'GA High Cost Loan Disclosure', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'GA requires special disclosure for high-cost home loans', 'GA_HIGH_COST_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Ga. Code § 7-6A-2'),
('IL', 'IL High Risk Home Loan Act', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'IL requires disclosure for high-risk home loans', 'IL_HIGH_RISK_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', '815 ILCS 137/15'),
('NJ', 'NJ Home Ownership Security Act', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'NJ requires disclosure for home loans under the Home Ownership Security Act', 'NJ_HOSA_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'N.J. Stat. § 46:10B-22'),
('PA', 'PA Mortgage Licensing Act Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'PA requires mortgage licensing act disclosure', 'PA_MLA_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', '63 P.S. § 46.221'),
('OH', 'OH Mortgage Broker Act Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'OH requires mortgage broker act disclosure', 'OH_MBA_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Ohio Rev. Code § 1322.06'),
('WA', 'WA Consumer Loan Act Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'WA requires consumer loan act disclosure', 'WA_CLA_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Wash. Rev. Code § 31.04.027'),
('OR', 'OR Mortgage Lender Law Disclosure', 'DISCLOSURE', 'CONVENTIONAL|FHA|VA', 'PURCHASE|REFINANCE', 'OR requires mortgage lender law disclosure', 'OR_MLL_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Or. Rev. Stat. § 86A.225'),
('CT', 'CT High Cost Mortgage Disclosure', 'DISCLOSURE', 'CONVENTIONAL', 'PURCHASE|REFINANCE|CASHOUT_REFI', 'CT requires disclosure for high-cost mortgage loans', 'CT_HIGH_COST_DISC', 'AT_APPLICATION', 0, NULL, NULL, TRUE, '2010-01-01', 'Conn. Gen. Stat. § 36a-740a');
