#!/bin/bash
# ============================================================================
# Overnight Batch Processing Script
# ============================================================================
#
# This script runs the overnight batch jobs for the MortgageLOS system.
# It is scheduled via cron to run at 10:00 PM every weeknight.
#
# CRON ENTRY (on production server):
# 0 22 * * 1-5 /opt/mortgage-los/scripts/run_overnight_batch.sh >> /var/log/mortgage-los/batch.log 2>&1
#
# NOTE: This script was written in 2008 and has been patched many times.
# The order of operations is IMPORTANT. Do not rearrange without understanding
# the dependencies between jobs.
#
# BATCH WINDOW: 22:00 - 04:00 (6 hours)
# If any job exceeds the window, it will be killed and the next job will start.
# This can leave data in an inconsistent state. See INC-2019-044.
# ============================================================================

set -e

# === CONFIGURATION ===
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BATCH_APP="$PROJECT_ROOT/src/MortgageLOS.BatchJobs/bin/Debug/net8.0/MortgageLOS.BatchJobs"
LOG_DIR="$PROJECT_ROOT/logs"
LOCK_FILE="/tmp/mortgage_los_batch.lock"
MAX_RUNTIME=21600  # 6 hours in seconds

# === FUNCTIONS ===

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [BATCH] $1"
}

error_log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [BATCH] [ERROR] $1" >&2
}

cleanup() {
    log "Cleaning up..."
    rm -f "$LOCK_FILE"
    log "Batch processing complete. End time: $(date '+%Y-%m-%d %H:%M:%S')"
}

# === LOCK CHECK ===
# Prevent multiple instances from running simultaneously
if [ -f "$LOCK_FILE" ]; then
    PID=$(cat "$LOCK_FILE")
    if kill -0 "$PID" 2>/dev/null; then
        error_log "Another batch process is already running (PID: $PID). Exiting."
        exit 1
    else
        error_log "Stale lock file found (PID: $PID not running). Removing."
        rm -f "$LOCK_FILE"
    fi
fi
echo $$ > "$LOCK_FILE"
trap cleanup EXIT

# === ENVIRONMENT SETUP ===
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

mkdir -p "$LOG_DIR"

log "========================================"
log "MortgageLOS Overnight Batch Processing"
log "Start time: $(date '+%Y-%m-%d %H:%M:%S')"
log "========================================"

# === STEP 1: SYNC CUSTOMER DATA TO CORE ===
# Sync borrower information from los_customer to los_core.loans
# This denormalizes customer data into the loans table for "performance"
log "STEP 1: Syncing customer data to core database..."
if "$BATCH_APP" databsync >> "$LOG_DIR/batch_$(date +%Y%m%d).log" 2>&1; then
    log "  Step 1 complete: Customer data synced"
else
    error_log "Step 1 FAILED: Customer data sync failed"
    error_log "  Continuing despite failure (data will be stale until next run)"
fi

# === STEP 2: PROCESS PENDING CREDIT PULLS ===
# Read credit pull request files from data/batch/credit-pulls/
# Process each one and save results to los_credit
log "STEP 2: Processing pending credit pulls..."
if "$BATCH_APP" creditpull >> "$LOG_DIR/batch_$(date +%Y%m%d).log" 2>&1; then
    log "  Step 2 complete: Credit pulls processed"
else
    error_log "Step 2 FAILED: Credit pull processing failed"
    error_log "  Credit scores may be missing. Loans may be stuck in PROCESSING."
fi

# === STEP 3: SYNC CREDIT SCORES TO CORE ===
# Copy representative credit scores from los_credit to los_core.loans
log "STEP 3: Syncing credit scores to core database..."
if "$BATCH_APP" creditsync >> "$LOG_DIR/batch_$(date +%Y%m%d).log" 2>&1; then
    log "  Step 3 complete: Credit scores synced"
else
    error_log "Step 3 FAILED: Credit score sync failed"
    error_log "  Cached scores in los_core will be STALE. See LOS-2289."
fi

# === STEP 4: RUN COMPLIANCE CHECKS ===
# Run compliance checks for all loans in UNDERWRITING status
log "STEP 4: Running compliance checks..."
if "$BATCH_APP" compliancecheck >> "$LOG_DIR/batch_$(date +%Y%m%d).log" 2>&1; then
    log "  Step 4 complete: Compliance checks processed"
else
    error_log "Step 4 FAILED: Compliance check processing failed"
    error_log "  Compliance violations may go undetected. Review manually."
fi

# === STEP 5: GENERATE INVESTOR REPORTS ===
# Generate investor reports for loans funded in the current period
log "STEP 5: Generating investor reports..."
if "$BATCH_APP" investorreport >> "$LOG_DIR/batch_$(date +%Y%m%d).log" 2>&1; then
    log "  Step 5 complete: Investor reports generated"
else
    error_log "Step 5 FAILED: Investor report generation failed"
    error_log "  Reports will need to be generated manually."
fi

# === STEP 6: GENERATE HMDA DATA ===
# Update HMDA records for loans that changed status
log "STEP 6: Updating HMDA data..."
if "$BATCH_APP" hmdasync >> "$LOG_DIR/batch_$(date +%Y%m%d).log" 2>&1; then
    log "  Step 6 complete: HMDA data updated"
else
    error_log "Step 6 FAILED: HMDA data update failed"
    error_log "  HMDA data may be incomplete. Review before filing."
fi

# === STEP 7: ARCHIVE OLD FILES ===
# Move processed files from data/batch/ to data/archive/
log "STEP 7: Archiving processed files..."
find "$PROJECT_ROOT/data/batch" -name "*.csv" -mtime +7 -exec mv {} "$PROJECT_ROOT/data/archive/" \; 2>/dev/null || true
log "  Step 7 complete: Old files archived"

# === STEP 8: CLEANUP TEMP FILES ===
log "STEP 8: Cleaning up temporary files..."
find "$PROJECT_ROOT/data/batch" -name "*.tmp" -delete 2>/dev/null || true
find "$PROJECT_ROOT/data/batch" -name "*.lock" -delete 2>/dev/null || true
log "  Step 8 complete: Temporary files cleaned"

# === SUMMARY ===
log "========================================"
log "Batch Summary:"
log "  End time: $(date '+%Y-%m-%d %H:%M:%S')"
log "  Log file: $LOG_DIR/batch_$(date +%Y%m%d).log"
log "========================================"

# Send email notification (commented out because mail relay is broken since 2020)
# mail -s "MortgageLOS Batch Report $(date +%Y-%m-%d)" ops@meridianlending.example < "$LOG_DIR/batch_$(date +%Y%m%d).log"

exit 0
