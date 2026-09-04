-- ============================================================================
-- MortgageLOS Database Initialization
-- Creates the 4 siloed databases used by the system
-- 
-- NOTE: These databases are intentionally SEPARATE. There are no foreign keys
-- across database boundaries. Data sync is handled by overnight batch jobs.
-- This is a known issue. See JIRA ticket LOS-1247 (opened 2011, still open).
-- ============================================================================

-- los_core is created by the container bootstrap (POSTGRES_DB in docker-compose.yml).
-- Creating it again here aborts this whole script under ON_ERROR_STOP, which silently
-- left the other three databases missing.
CREATE DATABASE los_credit;
CREATE DATABASE los_compliance;
CREATE DATABASE los_customer;

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE los_core TO los_admin;
GRANT ALL PRIVILEGES ON DATABASE los_credit TO los_admin;
GRANT ALL PRIVILEGES ON DATABASE los_compliance TO los_admin;
GRANT ALL PRIVILEGES ON DATABASE los_customer TO los_admin;
