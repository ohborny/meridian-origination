#!/bin/bash
# ============================================================================
# Database Sync Script
# ============================================================================
#
# This script syncs data between the 4 siloed databases. It should be run
# as part of the overnight batch or manually when data is out of sync.
#
# SYNC OPERATIONS:
#   1. Customer -> Core:    Copy borrower name, SSN last 4 from los_customer
#                          to los_core.loans (denormalized for "performance")
#   2. Credit -> Core:      Copy representative credit score from los_credit
#                          to los_core.loans.borrower_credit_score
#   3. Core -> Compliance:  Copy loan status changes to los_compliance
#                          for HMDA action tracking
#
# KNOWN ISSUES:
#   - Sync is one-way only. Changes in los_core are NOT propagated back.
#   - If a borrower's address changes in los_core, it won't update los_customer.
#     See LOS-1502.
#   - The sync uses SSN hash as the join key, which can fail if the SSN was
#     entered differently in different systems. See LOS-1873.
#   - There is no conflict resolution. Last write wins. This can cause data
#     loss if both databases are updated simultaneously.
#
# This script is a band-aid. The real fix is to consolidate the databases,
# but that requires a multi-year migration project that hasn't been approved.
# ============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BATCH_APP="$PROJECT_ROOT/src/MortgageLOS.BatchJobs/bin/Debug/net8.0/MortgageLOS.BatchJobs"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] [DB-SYNC] Starting database synchronization..."

echo "[$(date '+%Y-%m-%d %H:%M:%S')] [DB-SYNC] Syncing customer data to core..."
"$BATCH_APP" databsync

echo "[$(date '+%Y-%m-%d %H:%M:%S')] [DB-SYNC] Syncing credit scores to core..."
"$BATCH_APP" creditsync

echo "[$(date '+%Y-%m-%d %H:%M:%S')] [DB-SYNC] Syncing loan status to compliance..."
"$BATCH_APP" hmdasync

echo "[$(date '+%Y-%m-%d %H:%M:%S')] [DB-SYNC] Database synchronization complete."
