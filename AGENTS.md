# Agent Instructions

## Project Overview

MortgageLOS is a .NET 8 loan origination system. The solution contains shared
domain libraries, an ASP.NET Core Web API, and a console application for batch
processing. PostgreSQL data is split across four databases:

- `los_core`: loans, products, officers, branches, and pipeline data
- `los_credit`: credit reports, scores, liabilities, and DTI data
- `los_compliance`: HMDA, TRID, disclosures, and compliance rules
- `los_customer`: borrowers, addresses, employment, and assets

The main deployable applications are:

- `src/MortgageLOS.Web`: Web API and static web UI
- `src/MortgageLOS.BatchJobs`: overnight and on-demand batch jobs

The other projects under `src/` are shared or domain libraries referenced by
those applications.

## Prerequisites and Setup

Install:

- .NET 8 SDK
- Docker with Compose

Start the local PostgreSQL dependency from the repository root:

```bash
docker-compose up -d
```

The Compose setup initializes the four local databases from `sql/`. The
application defaults settings from `config/los.config`, with environment
variables taking precedence. Copy the template and load local overrides before
running the applications:

```bash
cp .env.example .env
set -a && source .env && set +a
```

Use `MORTGAGELOS_CONNECTIONSTRING_<DATABASE_NAME>` for connection strings and
`MORTGAGELOS_SETTING_<SETTING_NAME>` for other application settings. Names are
uppercased, and non-alphanumeric characters become underscores. For example,
use `MORTGAGELOS_CONNECTIONSTRING_LOS_CORE` and
`MORTGAGELOS_SETTING_AUSENDPOINT`. Treat `.env` as local development
configuration and do not copy its values into commits, logs, issues, or
responses.

Build the complete solution:

```bash
dotnet build MortgageLOS.sln
```

## Running the Applications

Start the Web API:

```bash
dotnet run --project src/MortgageLOS.Web
```

The API normally listens on `http://localhost:7001`. Verify it is running:

```bash
curl -f http://localhost:7001/health
```

Swagger UI is available at `/swagger`. The local demo flow creates sample
loans and exercises credit, underwriting, documents, and compliance endpoints:

```bash
./scripts/seed_demo_loans.sh
```

Run an individual batch job:

```bash
dotnet run --project src/MortgageLOS.BatchJobs -- <jobname>
```

Supported job names include `overnight`, `creditpull`, `creditsync`,
`compliancecheck`, `investorreport`, `hmdasync`, and `databsync`.

Only run the demo seed flow and batch jobs against an explicitly local
development environment. They write database records and may move or delete
files under `data/batch` when using the production-style shell scripts.

## Development Workflow

1. Read the relevant domain service, controller, SQL schema, and README
   documentation before changing behavior.
2. Keep changes scoped to the requested module and preserve the existing
   project-reference structure.
3. Build the solution after code or project-file changes:

   ```bash
   dotnet build MortgageLOS.sln
   ```

4. Check the final diff and repository status before reporting completion.

There is currently no test project or automated test suite in the repository.
If tests are added, keep them in a dedicated test project and document the
command used to run them.

## Code and Data Conventions

- Target `net8.0` and follow the existing C# style and namespace layout.
- Keep SQL parameterized. Do not add string-concatenated SQL with user input.
- Preserve the separation between the four PostgreSQL databases. Cross-database
  synchronization is handled by batch jobs and is intentionally not transactional.
- Use `MortgageLOS.Shared` for shared models, database access, logging, and
  formatting utilities rather than duplicating helpers.
- Use `FileLogger` for application logging. Do not log full SSNs, credentials,
  connection strings, borrower secrets, or raw credit data. Use masking or
  hashes where the existing model requires an identifier.
- Preserve loan pipeline transitions and compliance timing rules unless the
  requested change explicitly covers them.
- Keep generated output, logs, local PostgreSQL data, and local configuration
  out of commits. Review `.gitignore` before adding generated files.

## High-Risk Areas

The repository documents known issues around siloed database synchronization,
stale cached credit data, SSN matching, hardcoded holidays, HMDA compatibility,
file handoffs, and legacy/deprecated methods. Check the relevant issue
references and existing comments before modifying these areas.

Do not run `scripts/run_overnight_batch.sh` or `scripts/sync_databases.sh`
against production or shared environments. These scripts can update multiple
databases and archive or remove batch files. Never deploy `config/los.config`
without reviewing and replacing local development settings.

## Documentation

Update the README or `docs/ARCHITECTURE.md` when a change affects setup,
services, database boundaries, batch ordering, API behavior, or operational
risks. Keep command examples executable from the repository root.
