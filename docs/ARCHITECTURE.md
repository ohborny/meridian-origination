# MortgageLOS Architecture

**Last Updated:** 2019-03-15  
**Author:** J. Martinez (no longer with company)  
**Status:** OUTDATED - references components that have been renamed or removed

## System Overview

MortgageLOS is a multi-module .NET application for mortgage loan origination. The system was originally built in 2005 as a .NET Framework 2.0 application and has been through multiple partial migrations.

### Current State (as of 2019)

The system currently runs on .NET Framework 4.7.2 with PostgreSQL 11. A migration to .NET Core was started in 2019 but was paused due to budget constraints. The migration was approximately 30% complete when it was stopped.

> **NOTE (2024):** The .NET Core migration was eventually completed in 2023. The system now runs on .NET 8. However, the code still uses many patterns from the .NET Framework era.

### Module Dependencies

```
                    ┌─────────────┐
                    │   Shared     │
                    │  (Models,   │
                    │   Utils)     │
                    └──────┬───────┘
                           │
          ┌────────────────┼────────────────┐
          │                │                │
    ┌─────┴─────┐  ┌──────┴──────┐  ┌──────┴──────┐
    │Origination│  │   Credit    │  │  Documents  │
    └─────┬─────┘  └──────┬──────┘  └─────────────┘
          │                │
    ┌─────┴─────┐  ┌──────┴──────┐
    │Compliance │  │Underwriting │
    └─────┬─────┘  └──────┬──────┘
          │                │
          └────────┬───────┘
                   │
            ┌──────┴──────┐
            │   Closing   │
            └─────────────┘
```

> **NOTE:** The Compliance module also depends on Origination (not shown). The Reporting module depends on Compliance. The BatchJobs and Web modules depend on everything.

### Database Architecture

The system uses 4 separate PostgreSQL databases:

1. **los_core** - Main loan data (loans, products, officers, branches, conditions, pricing)
2. **los_credit** - Credit data (reports, scores, liabilities, DTI)
3. **los_compliance** - Compliance data (HMDA, disclosures, state rules, TRID)
4. **los_customer** - Customer data (borrowers, addresses, employment, assets)

> **WARNING:** These databases are SEPARATE. There are NO foreign keys across database boundaries. Data sync is handled by the overnight batch job. If the batch job fails, data will be inconsistent.

### Batch Processing

Batch jobs run overnight (22:00-04:00) and handle:

- Credit pull processing (reads CSV files from data/batch/credit-pulls/)
- Data synchronization between databases
- Compliance check execution
- Report generation
- File archival

> **NOTE:** The batch jobs use file-based handoffs (CSV files). This is fragile - if a file is partially written when the batch job picks it up, it will process a truncated file. The convention is to write to .tmp files and rename when done.

### Integration Points

- **AUS (Automated Underwriting System)** - Integrates with Fannie Mae DU and Freddie Mac LP
- **Credit Bureaus** - Integrates with Experian, Equifax, TransUnion via credit vendor
- **Investor Systems** - Generates investor reports for loan sales
- **HMDA Filing** - Generates HMDA LAR file for annual regulatory filing

### Configuration

Configuration is in `config/los.config` (XML format). The config includes:

- Connection strings for all 4 databases
- File paths for batch handoffs
- Batch schedule settings
- AUS parameters
- HMDA reporting settings
- TRID timing settings

> **WARNING:** The config file has settings from multiple eras. Some settings are no longer used but haven't been removed because something might depend on them.

## Migration History

| Year | Change | Status |
|------|--------|--------|
| 2005 | Initial .NET Framework 2.0 build | Complete |
| 2008 | Credit module added | Complete |
| 2010 | Underwriting module added | Complete |
| 2012 | Compliance module added (HMDA 2011 rules) | Complete |
| 2014 | HOEPA/HPML/QM compliance added | Complete |
| 2015 | TRID compliance added, Documents module added | Complete |
| 2017 | HMDA 2018 changes (new data points) | Complete |
| 2018 | Fair lending review added | Complete |
| 2019 | .NET Core migration started | PAUSED (later completed 2023) |
| 2020 | COVID-19 forbearance tracking | Complete (temporary, still here) |
| 2023 | SCRA checks added, .NET 8 migration | Complete |
| 2024 | NMLS unique identifier updates | Complete |

## Known Technical Debt

1. **Siloed databases** - 4 databases with no cross-DB transactions (LOS-1247)
2. **Stale cached data** - Credit scores and borrower data cached in los_core can be stale (LOS-2289)
3. **Hardcoded holidays** - Holiday list requires annual manual update
4. **HMDA table bloat** - 47 columns from multiple regulatory vintages
5. **File-based batch handoffs** - Fragile, no file locking
6. **Copy-pasted code** - DatabaseHelper methods are copy-pasted per database
7. **God classes** - Several service classes are too large and do too many things
8. **Inconsistent naming** - Different modules use different naming conventions
9. **Dead code** - Multiple deprecated methods still in the codebase
10. **Missing tests** - Test coverage is minimal and outdated
