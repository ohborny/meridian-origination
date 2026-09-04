-- ============================================================================
-- Document Requirements Seed Data
-- Defines which documents are required for each loan type/purpose combination
-- ============================================================================

\c los_core;

-- Create document_requirements table if it doesn't exist (Documents module also does this)
CREATE TABLE IF NOT EXISTS document_requirements (
    requirement_id SERIAL PRIMARY KEY,
    loan_type VARCHAR(30) NOT NULL,
    loan_purpose VARCHAR(30),
    document_type VARCHAR(40) NOT NULL,
    document_name VARCHAR(200) NOT NULL,
    is_required BOOLEAN DEFAULT TRUE,
    is_conditional BOOLEAN DEFAULT FALSE,
    condition_desc TEXT,
    sort_order INTEGER DEFAULT 0,
    investor_code VARCHAR(20),
    auto_verifiable BOOLEAN DEFAULT FALSE,
    verification_script VARCHAR(100)
);

-- === CONVENTIONAL PURCHASE ===
INSERT INTO document_requirements (loan_type, loan_purpose, document_type, document_name, is_required, is_conditional, condition_desc, sort_order) VALUES
('CONVENTIONAL', 'PURCHASE', '1003', 'Uniform Residential Loan Application (1003)', TRUE, FALSE, NULL, 1),
('CONVENTIONAL', 'PURCHASE', 'CREDIT_REPORT', 'Tri-Merge Credit Report', TRUE, FALSE, NULL, 2),
('CONVENTIONAL', 'PURCHASE', 'PURCHASE_AGREEMENT', 'Purchase Agreement / Contract', TRUE, FALSE, NULL, 3),
('CONVENTIONAL', 'PURCHASE', 'INCOME_DOCS', 'Most Recent Paystub (30 days)', TRUE, FALSE, NULL, 4),
('CONVENTIONAL', 'PURCHASE', 'INCOME_DOCS', 'W-2 Forms (2 years)', TRUE, FALSE, NULL, 5),
('CONVENTIONAL', 'PURCHASE', 'TAX_RETURNS', 'Federal Tax Returns (2 years)', TRUE, FALSE, NULL, 6),
('CONVENTIONAL', 'PURCHASE', 'ASSET_DOCS', 'Bank Statements (2 months)', TRUE, FALSE, NULL, 7),
('CONVENTIONAL', 'PURCHASE', 'APPRAISAL', 'Uniform Residential Appraisal Report', TRUE, FALSE, NULL, 8),
('CONVENTIONAL', 'PURCHASE', 'TITLE_COMMITMENT', 'Title Commitment / Preliminary Title Report', TRUE, FALSE, NULL, 9),
('CONVENTIONAL', 'PURCHASE', 'INSURANCE', 'Homeowners Insurance Policy', TRUE, FALSE, NULL, 10),
('CONVENTIONAL', 'PURCHASE', 'LE', 'Loan Estimate (TRID)', TRUE, FALSE, NULL, 11),
('CONVENTIONAL', 'PURCHASE', 'CD', 'Closing Disclosure (TRID)', TRUE, FALSE, NULL, 12),
('CONVENTIONAL', 'PURCHASE', 'BORROWER_AUTH', 'Borrower Authorization Form', TRUE, FALSE, NULL, 13),
('CONVENTIONAL', 'PURCHASE', 'VOE', 'Verification of Employment', TRUE, FALSE, NULL, 14),
('CONVENTIONAL', 'PURCHASE', 'HOA_DOCS', 'HOA Documents', FALSE, TRUE, 'Required if property is in an HOA', 15),
('CONVENTIONAL', 'PURCHASE', 'DISCLOSURES', 'State-Specific Disclosures', TRUE, FALSE, NULL, 16);

-- === CONVENTIONAL REFINANCE ===
INSERT INTO document_requirements (loan_type, loan_purpose, document_type, document_name, is_required, is_conditional, condition_desc, sort_order) VALUES
('CONVENTIONAL', 'REFINANCE', '1003', 'Uniform Residential Loan Application (1003)', TRUE, FALSE, NULL, 1),
('CONVENTIONAL', 'REFINANCE', 'CREDIT_REPORT', 'Tri-Merge Credit Report', TRUE, FALSE, NULL, 2),
('CONVENTIONAL', 'REFINANCE', 'INCOME_DOCS', 'Most Recent Paystub (30 days)', TRUE, FALSE, NULL, 3),
('CONVENTIONAL', 'REFINANCE', 'INCOME_DOCS', 'W-2 Forms (2 years)', TRUE, FALSE, NULL, 4),
('CONVENTIONAL', 'REFINANCE', 'TAX_RETURNS', 'Federal Tax Returns (2 years)', TRUE, FALSE, NULL, 5),
('CONVENTIONAL', 'REFINANCE', 'ASSET_DOCS', 'Bank Statements (2 months)', TRUE, FALSE, NULL, 6),
('CONVENTIONAL', 'REFINANCE', 'APPRAISAL', 'Uniform Residential Appraisal Report', TRUE, FALSE, NULL, 7),
('CONVENTIONAL', 'REFINANCE', 'TITLE_COMMITMENT', 'Title Commitment', TRUE, FALSE, NULL, 8),
('CONVENTIONAL', 'REFINANCE', 'INSURANCE', 'Homeowners Insurance Policy', TRUE, FALSE, NULL, 9),
('CONVENTIONAL', 'REFINANCE', 'LE', 'Loan Estimate (TRID)', TRUE, FALSE, NULL, 10),
('CONVERTIONAL', 'REFINANCE', 'CD', 'Closing Disclosure (TRID)', TRUE, FALSE, NULL, 11),
('CONVENTIONAL', 'REFINANCE', 'BORROWER_AUTH', 'Borrower Authorization Form', TRUE, FALSE, NULL, 12),
('CONVENTIONAL', 'REFINANCE', 'VOE', 'Verification of Employment', TRUE, FALSE, NULL, 13),
('CONVENTIONAL', 'REFINANCE', 'DISCLOSURES', 'State-Specific Disclosures', TRUE, FALSE, NULL, 14);

-- === FHA PURCHASE ===
INSERT INTO document_requirements (loan_type, loan_purpose, document_type, document_name, is_required, is_conditional, condition_desc, sort_order) VALUES
('FHA', 'PURCHASE', '1003', 'Uniform Residential Loan Application (1003)', TRUE, FALSE, NULL, 1),
('FHA', 'PURCHASE', 'CREDIT_REPORT', 'Tri-Merge Credit Report', TRUE, FALSE, NULL, 2),
('FHA', 'PURCHASE', 'PURCHASE_AGREEMENT', 'Purchase Agreement / Contract', TRUE, FALSE, NULL, 3),
('FHA', 'PURCHASE', 'INCOME_DOCS', 'Most Recent Paystub (30 days)', TRUE, FALSE, NULL, 4),
('FHA', 'PURCHASE', 'INCOME_DOCS', 'W-2 Forms (2 years)', TRUE, FALSE, NULL, 5),
('FHA', 'PURCHASE', 'TAX_RETURNS', 'Federal Tax Returns (2 years)', TRUE, FALSE, NULL, 6),
('FHA', 'PURCHASE', 'ASSET_DOCS', 'Bank Statements (2 months)', TRUE, FALSE, NULL, 7),
('FHA', 'PURCHASE', 'APPRAISAL', 'FHA Appraisal (URAR)', TRUE, FALSE, NULL, 8),
('FHA', 'PURCHASE', 'TITLE_COMMITMENT', 'Title Commitment', TRUE, FALSE, NULL, 9),
('FHA', 'PURCHASE', 'INSURANCE', 'Homeowners Insurance Policy', TRUE, FALSE, NULL, 10),
('FHA', 'PURCHASE', 'LE', 'Loan Estimate (TRID)', TRUE, FALSE, NULL, 11),
('FHA', 'PURCHASE', 'CD', 'Closing Disclosure (TRID)', TRUE, FALSE, NULL, 12),
('FHA', 'PURCHASE', 'BORROWER_AUTH', 'Borrower Authorization Form', TRUE, FALSE, NULL, 13),
('FHA', 'PURCHASE', 'VOE', 'Verification of Employment', TRUE, FALSE, NULL, 14),
('FHA', 'PURCHASE', 'DISCLOSURES', 'FHA Disclosures + State-Specific', TRUE, FALSE, NULL, 15),
('FHA', 'PURCHASE', 'HOA_DOCS', 'HOA Documents', FALSE, TRUE, 'Required if property is in an HOA', 16);

-- === VA PURCHASE ===
INSERT INTO document_requirements (loan_type, loan_purpose, document_type, document_name, is_required, is_conditional, condition_desc, sort_order) VALUES
('VA', 'PURCHASE', '1003', 'Uniform Residential Loan Application (1003)', TRUE, FALSE, NULL, 1),
('VA', 'PURCHASE', 'CREDIT_REPORT', 'Tri-Merge Credit Report', TRUE, FALSE, NULL, 2),
('VA', 'PURCHASE', 'PURCHASE_AGREEMENT', 'Purchase Agreement / Contract', TRUE, FALSE, NULL, 3),
('VA', 'PURCHASE', 'INCOME_DOCS', 'Most Recent Paystub (30 days)', TRUE, FALSE, NULL, 4),
('VA', 'PURCHASE', 'INCOME_DOCS', 'W-2 Forms (2 years)', TRUE, FALSE, NULL, 5),
('VA', 'PURCHASE', 'TAX_RETURNS', 'Federal Tax Returns (2 years)', TRUE, FALSE, NULL, 6),
('VA', 'PURCHASE', 'ASSET_DOCS', 'Bank Statements (2 months)', TRUE, FALSE, NULL, 7),
('VA', 'PURCHASE', 'APPRAISAL', 'VA Appraisal (NOV)', TRUE, FALSE, NULL, 8),
('VA', 'PURCHASE', 'TITLE_COMMITMENT', 'Title Commitment', TRUE, FALSE, NULL, 9),
('VA', 'PURCHASE', 'INSURANCE', 'Homeowners Insurance Policy', TRUE, FALSE, NULL, 10),
('VA', 'PURCHASE', 'LE', 'Loan Estimate (TRID)', TRUE, FALSE, NULL, 11),
('VA', 'PURCHASE', 'CD', 'Closing Disclosure (TRID)', TRUE, FALSE, NULL, 12),
('VA', 'PURCHASE', 'BORROWER_AUTH', 'Borrower Authorization Form', TRUE, FALSE, NULL, 13),
('VA', 'PURCHASE', 'VOE', 'Verification of Employment', TRUE, FALSE, NULL, 14),
('VA', 'PURCHASE', 'DISCLOSURES', 'VA Disclosures + State-Specific', TRUE, FALSE, NULL, 15);

-- === JUMBO PURCHASE ===
INSERT INTO document_requirements (loan_type, loan_purpose, document_type, document_name, is_required, is_conditional, condition_desc, sort_order) VALUES
('JUMBO', 'PURCHASE', '1003', 'Uniform Residential Loan Application (1003)', TRUE, FALSE, NULL, 1),
('JUMBO', 'PURCHASE', 'CREDIT_REPORT', 'Tri-Merge Credit Report', TRUE, FALSE, NULL, 2),
('JUMBO', 'PURCHASE', 'PURCHASE_AGREEMENT', 'Purchase Agreement / Contract', TRUE, FALSE, NULL, 3),
('JUMBO', 'PURCHASE', 'INCOME_DOCS', 'Most Recent Paystub (30 days)', TRUE, FALSE, NULL, 4),
('JUMBO', 'PURCHASE', 'INCOME_DOCS', 'W-2 Forms (2 years)', TRUE, FALSE, NULL, 5),
('JUMBO', 'PURCHASE', 'TAX_RETURNS', 'Federal Tax Returns (2 years)', TRUE, FALSE, NULL, 6),
('JUMBO', 'PURCHASE', 'ASSET_DOCS', 'Bank Statements (3 months)', TRUE, FALSE, NULL, 7),
('JUMBO', 'PURCHASE', 'APPRAISAL', 'Jumbo Appraisal (may require 2 appraisals)', TRUE, FALSE, NULL, 8),
('JUMBO', 'PURCHASE', 'TITLE_COMMITMENT', 'Title Commitment', TRUE, FALSE, NULL, 9),
('JUMBO', 'PURCHASE', 'INSURANCE', 'Homeowners Insurance Policy', TRUE, FALSE, NULL, 10),
('JUMBO', 'PURCHASE', 'LE', 'Loan Estimate (TRID)', TRUE, FALSE, NULL, 11),
('JUMBO', 'PURCHASE', 'CD', 'Closing Disclosure (TRID)', TRUE, FALSE, NULL, 12),
('JUMBO', 'PURCHASE', 'BORROWER_AUTH', 'Borrower Authorization Form', TRUE, FALSE, NULL, 13),
('JUMBO', 'PURCHASE', 'VOE', 'Verification of Employment', TRUE, FALSE, NULL, 14),
('JUMBO', 'PURCHASE', 'DISCLOSURES', 'State-Specific Disclosures', TRUE, FALSE, NULL, 15);

-- === CASH-OUT REFINANCE (all types) ===
INSERT INTO document_requirements (loan_type, loan_purpose, document_type, document_name, is_required, is_conditional, condition_desc, sort_order) VALUES
('CONVENTIONAL', 'CASHOUT_REFI', '1003', 'Uniform Residential Loan Application (1003)', TRUE, FALSE, NULL, 1),
('CONVENTIONAL', 'CASHOUT_REFI', 'CREDIT_REPORT', 'Tri-Merge Credit Report', TRUE, FALSE, NULL, 2),
('CONVENTIONAL', 'CASHOUT_REFI', 'INCOME_DOCS', 'Most Recent Paystub (30 days)', TRUE, FALSE, NULL, 3),
('CONVENTIONAL', 'CASHOUT_REFI', 'INCOME_DOCS', 'W-2 Forms (2 years)', TRUE, FALSE, NULL, 4),
('CONVENTIONAL', 'CASHOUT_REFI', 'TAX_RETURNS', 'Federal Tax Returns (2 years)', TRUE, FALSE, NULL, 5),
('CONVENTIONAL', 'CASHOUT_REFI', 'ASSET_DOCS', 'Bank Statements (2 months)', TRUE, FALSE, NULL, 6),
('CONVENTIONAL', 'CASHOUT_REFI', 'APPRAISAL', 'Uniform Residential Appraisal Report', TRUE, FALSE, NULL, 7),
('CONVENTIONAL', 'CASHOUT_REFI', 'TITLE_COMMITMENT', 'Title Commitment', TRUE, FALSE, NULL, 8),
('CONVENTIONAL', 'CASHOUT_REFI', 'INSURANCE', 'Homeowners Insurance Policy', TRUE, FALSE, NULL, 9),
('CONVENTIONAL', 'CASHOUT_REFI', 'LE', 'Loan Estimate (TRID)', TRUE, FALSE, NULL, 10),
('CONVENTIONAL', 'CASHOUT_REFI', 'CD', 'Closing Disclosure (TRID)', TRUE, FALSE, NULL, 11),
('CONVENTIONAL', 'CASHOUT_REFI', 'BORROWER_AUTH', 'Borrower Authorization Form', TRUE, FALSE, NULL, 12),
('CONVENTIONAL', 'CASHOUT_REFI', 'DISCLOSURES', 'State-Specific Disclosures (incl. TX A6 if TX)', TRUE, FALSE, NULL, 13);
