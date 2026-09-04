## Description

<!-- What changed, why it changed, and the intended user or operational outcome. -->

## Scope

<!-- List the modules, APIs, database schemas, scripts, or documentation affected. -->

- [ ] Web API (`src/MortgageLOS.Web`)
- [ ] Batch processing (`src/MortgageLOS.BatchJobs`)
- [ ] Shared/domain library
- [ ] PostgreSQL schema or seed data
- [ ] Configuration or local environment
- [ ] Documentation only

## Validation

<!-- State the commands run and their results. Explain any checks not run. -->

- [ ] `dotnet build MortgageLOS.sln`
- [ ] Unit or integration tests, if present
- [ ] Manual API check, if API behavior changed
- [ ] Database or batch-job validation, if applicable

Commands and results:

```text
<!-- Example: dotnet build MortgageLOS.sln (passed) -->
```

## Data, Security, and Compliance

<!-- MortgageLOS processes borrower PII and regulated loan data. Complete each item. -->

- [ ] This change does not add, expose, or log credentials, full SSNs, or raw credit data.
- [ ] This change does not modify borrower PII storage or handling.
- [ ] This change does not alter HMDA, TRID, SCRA, fair-lending, or disclosure behavior.
- [ ] This change does not change database schema, migrations, or cross-database synchronization.

Relevant details, risks, or required review:

<!-- Include links to policy, issue, or implementation details where relevant. -->

## Operational Impact

<!-- Document production behavior, rollback plan, and any required deployment steps. -->

- [ ] No deployment or operational impact
- [ ] Requires configuration or environment changes
- [ ] Affects batch scheduling, file handoffs, or data synchronization
- [ ] Requires a database migration or backfill

Rollback and deployment notes:

<!-- Describe how to revert safely. Call out any non-reversible data effects. -->

## Reviewer Context

<!-- Link related issues, explain design decisions, and identify areas needing focused review. -->

Related issue:

<!-- Closes #123 or N/A -->
