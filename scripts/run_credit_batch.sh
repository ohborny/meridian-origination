#!/bin/bash
# ============================================================================
# Credit Pull Batch Script
# ============================================================================
#
# This script processes pending credit pull requests from the data/batch/
# credit-pulls/ directory. It can be run on-demand or as part of the overnight
# batch.
#
# USAGE:
#   ./run_credit_batch.sh           # Process all pending requests
#   ./run_credit_batch.sh --single  # Process one request and exit
#
# NOTE: Credit pulls are expensive ($15-30 per pull). The batch job limits
# pulls to 100 per run to control costs. If there are more than 100 pending,
# the rest will be processed in the next run.
# ============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BATCH_APP="$PROJECT_ROOT/src/MortgageLOS.BatchJobs/bin/Debug/net8.0/MortgageLOS.BatchJobs"
CREDIT_DIR="$PROJECT_ROOT/data/batch/credit-pulls"
MAX_PULLS=100

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] [CREDIT-BATCH] Starting credit pull batch..."

# Check if credit pull directory exists
if [ ! -d "$CREDIT_DIR" ]; then
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [CREDIT-BATCH] No credit pull directory found. Nothing to do."
    exit 0
fi

# Count pending files
PENDING_COUNT=$(find "$CREDIT_DIR" -name "*.csv" | wc -l | tr -d ' ')
echo "[$(date '+%Y-%m-%d %H:%M:%S')] [CREDIT-BATCH] Found $PENDING_COUNT pending credit pull requests"

if [ "$PENDING_COUNT" -eq 0 ]; then
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [CREDIT-BATCH] No pending requests. Exiting."
    exit 0
fi

# Process credit pulls
if [ "$1" = "--single" ]; then
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [CREDIT-BATCH] Processing single request..."
    "$BATCH_APP" creditpull --single
else
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [CREDIT-BATCH] Processing up to $MAX_PULLS requests..."
    "$BATCH_APP" creditpull --max $MAX_PULLS
fi

echo "[$(date '+%Y-%m-%d %H:%M:%S')] [CREDIT-BATCH] Credit pull batch complete."
