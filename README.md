# MortgageLOS - Loan Origination System

**Version:** 4.2.17  
**Last Updated:** 2024-03-15  
**Status:** Production (with known issues)

## Overview

MortgageLOS is the loan origination system for First Meridian Home Lending. It handles the complete mortgage loan lifecycle from application through investor purchase, including credit analysis, underwriting, regulatory compliance, document management, and closing.

## Architecture

The system consists of 10 modules across 4 siloed PostgreSQL databases:

| Module | Description | Est. Age | Database |
|--------|-------------|----------|----------|
| Origination | Application intake, loan creation, pipeline management | 2005 | los_core |
| Credit | Credit reports, DTI calculation, AUS integration | 2008 | los_credit |
| Underwriting | Automated underwriting, manual review, conditions | 2010 | los_core |
| Compliance | HMDA, TRID, state disclosures, fair lending | 2012 | los_compliance |
| Documents | Document tracking, verification, checklists | 2015 | los_core |
| Closing | Closing disclosure, funding, investor shipment | 2016 | los_core |
| Reporting | HMDA LAR, investor reports, pipeline reports | 2018 | los_compliance |
| BatchJobs | Overnight batch processing, file handoffs | 2008 | all |
| Web | Web API for loan officers, underwriters, compliance | 2015 | all |
| Shared | Common models, utilities, database access | 2005 | all |

### Siloed Databases

The 4 databases are **intentionally separate** with no cross-database foreign keys:

- **los_core** - Main loan data, products, officers, branches
- **los_credit** - Credit reports, scores, liabilities, DTI
- **los_compliance** - HMDA, TRID, disclosures, state rules
- **los_customer** - Borrower data, addresses, employment, assets

Data synchronization between databases is handled by overnight batch jobs. This is a known architectural limitation (see JIRA LOS-1247).

## Getting Started

### Prerequisites

- .NET 8 SDK
- PostgreSQL 14+
- Docker (optional, for local database)

### Setup

```bash
# Start PostgreSQL
docker-compose up -d

# Build the solution
dotnet build MortgageLOS.sln

# Run the web API
dotnet run --project src/MortgageLOS.Web

# Run batch jobs
dotnet run --project src/MortgageLOS.BatchJobs -- overnight
```

### Configuration

Configuration defaults are in `config/los.config`. To keep local credentials
and environment-specific settings out of the repository, copy the template and
set the values for your environment:

```bash
cp .env.example .env
set -a && source .env && set +a
```

Environment overrides use these names:

- Connection strings: `MORTGAGELOS_CONNECTIONSTRING_<DATABASE_NAME>`
- Application settings: `MORTGAGELOS_SETTING_<SETTING_NAME>`

Names are uppercased, and non-alphanumeric characters become underscores. For
example, `los_core` is `MORTGAGELOS_CONNECTIONSTRING_LOS_CORE`, and
`AusEndpoint` is `MORTGAGELOS_SETTING_AUSENDPOINT`. See `.env.example` for the
full local-development template. Never commit `.env` files or production
credentials.

## Key Concepts

### Loan Pipeline

```
APPLICATION → PROCESSING → UNDERWRITING → CONDITIONAL_APPROVAL 
→ CLEAR_TO_CLOSE → CLOSING → FUNDED → SHIPPED → PURCHASED
```

Terminal states: SUSPENDED, DENIED, WITHDRAWN, CANCELLED

### Regulatory Compliance

- **HMDA** - Home Mortgage Disclosure Act data collection and reporting
- **TRID** - TILA-RESPA Integrated Disclosure (LE and CD timing)
- **HOEPA** - Home Ownership and Equity Protection Act
- **QM** - Qualified Mortgage / Ability to Repay
- **SCRA** - Servicemembers Civil Relief Act
- State-specific disclosures (50 states + DC + PR)

### Batch Processing

Overnight batch jobs run 22:00-04:00 weeknights:

1. Customer data sync (customer → core)
2. Credit pull processing
3. Credit score sync (credit → core)
4. Compliance checks
5. Investor report generation
6. HMDA data update
7. File archival

## Known Issues

- Credit scores in los_core can be stale if batch sync fails (LOS-2289)
- Customer address changes in los_core don't sync back to los_customer (LOS-1502)
- SSN hash matching across databases is fragile (LOS-1873)
- Holiday list is hardcoded and requires annual manual update
- CLTV calculation has a known unit issue (see comment in Origination code)
- HMDA table has 47 columns from multiple regulatory vintages

## Documentation

- [Architecture](docs/ARCHITECTURE.md) (outdated)
- [HMDA Reporting Guide](docs/HMDA_REPORTING_GUIDE.md) (partially updated)
- [Changelog](docs/CHANGELOG.txt) (stops at 2019)

## Team

- Core Platform: disbanded 2019, now maintained by "whoever is available"
- Credit Services: merged with Underwriting 2016, split again 2019
- Compliance Engineering: 6 different leads since 2012
- Customer Experience: rebranded 3 times, currently "Borrower Success"
