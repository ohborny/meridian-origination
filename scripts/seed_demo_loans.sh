#!/bin/bash
# ============================================================================
# Seeds demo loans through the Web API so the pipeline has data to show.
#
# Goes through the REST API rather than raw SQL so the loans pick up real
# loan numbers, pricing and LTV/DTI values from the origination module.
# Some loans are then walked further down the pipeline (credit pull, AUS,
# underwriting submission, compliance checks) so each stage has examples.
#
# Usage:  ./scripts/seed_demo_loans.sh [base-url]
# ============================================================================
set -u

BASE="${1:-http://localhost:7001}"
API="$BASE/api"

if ! curl -sf -o /dev/null "$BASE/health"; then
    echo "ERROR: API is not responding at $BASE"
    echo "Start it with: dotnet run --project src/MortgageLOS.Web"
    exit 1
fi

# create_loan <first> <last> <ssn> <purpose> <product> <amount> <value> <addr> <city> <state> <zip> <occupancy> <income> <lo> <branch>
create_loan() {
    local payload
    payload=$(cat <<JSON
{
  "borrowerFirstName": "$1",
  "borrowerLastName": "$2",
  "borrowerSsn": "$3",
  "loanPurpose": "$4",
  "productCode": "$5",
  "loanAmount": $6,
  "propertyValue": $7,
  "propertyAddr1": "$8",
  "propertyCity": "$9",
  "propertyState": "${10}",
  "propertyZip": "${11}",
  "propertyType": "SFR",
  "propertyOccupancy": "${12}",
  "monthlyIncome": ${13},
  "loanOfficerCode": "${14}",
  "branchCode": "${15}"
}
JSON
)
    curl -s -X POST "$API/loans" -H 'Content-Type: application/json' -d "$payload" \
        | sed -n 's/.*"loanNumber":"\([^"]*\)".*/\1/p'
}

# advance <loanNumber> <times>
advance() {
    local i
    for ((i = 0; i < $2; i++)); do
        curl -s -o /dev/null -X POST "$API/pipeline/advance/$1?changedBy=seed_script"
    done
}

echo "Creating demo loan applications..."

L1=$(create_loan William Hartwell 555-44-4321 PURCHASE      CONF30 425000 510000 "2214 E Camelback Rd"  Phoenix     AZ 85016 PRIMARY    14500 LO001 001)
L2=$(create_loan Patricia Delgado 555-44-8765 REFINANCE     CONF15 288000 395000 "1807 Barton Springs"  Austin      TX 78704 PRIMARY     9800 LO004 003)
L3=$(create_loan Robert Kowalski  555-44-1290 PURCHASE      FHA30  312500 325000 "4420 Tennyson St"     Denver      CO 80212 PRIMARY     8200 LO005 004)
L4=$(create_loan Jennifer Matsuda 555-44-5678 PURCHASE      CONF30 685000 830000 "3155 Sixth Ave"       "San Diego" CA 92103 PRIMARY    21000 LO008 007)
L5=$(create_loan Christopher OBrien 555-44-9012 CASHOUT_REFI CONF30 402000 640000 "890 Piedmont Ave NE" Atlanta     GA 30309 PRIMARY    13750 LO006 005)
L6=$(create_loan Maria Santos     555-44-3456 PURCHASE      VA30   365000 372000 "705 W Azeele St"      Tampa       FL 33606 PRIMARY    10400 LO009 008)
L7=$(create_loan David Goldberg   555-44-7890 REFINANCE     CONF30 244000 380000 "1512 Dilworth Rd"     Charlotte   NC 28203 PRIMARY     9100 LO007 006)
L8=$(create_loan Linda Carter     555-44-2345 PURCHASE      CONF30 298000 340000 "2900 Hennepin Ave"    Minneapolis MN 55408 SECONDARY  11200 LO010 009)

for ln in "$L1" "$L2" "$L3" "$L4" "$L5" "$L6" "$L7" "$L8"; do
    if [ -z "$ln" ]; then
        echo "  WARNING: a loan failed to create"
    else
        echo "  created $ln"
    fi
done

echo
echo "Pulling credit..."
for ln in "$L2" "$L3" "$L4" "$L5" "$L6" "$L7"; do
    [ -n "$ln" ] || continue
    curl -s -o /dev/null -X POST "$API/credit/pull/$ln"
    echo "  credit pulled for $ln"
done

echo
echo "Calculating DTI..."
# calc_dti <loanNumber> <monthlyIncome> <housingPayment> <loanType>
calc_dti() {
    [ -n "$1" ] || return 0
    curl -s -o /dev/null -X POST "$API/credit/dti/$1" -H 'Content-Type: application/json' \
        -d "{\"monthlyIncome\":$2,\"coborrowerIncome\":0,\"proposedHousingPayment\":$3,\"loanType\":\"$4\"}"
    echo "  DTI calculated for $1"
}
calc_dti "$L2" 9800  2150 CONVENTIONAL
calc_dti "$L3" 8200  2480 FHA
calc_dti "$L4" 21000 4630 CONVENTIONAL
calc_dti "$L5" 13750 2870 CONVENTIONAL
calc_dti "$L6" 10400 2610 VA
calc_dti "$L7" 9100  1840 CONVENTIONAL

echo
echo "Advancing loans through the pipeline..."
advance "$L2" 1   # -> PROCESSING
advance "$L3" 1   # -> PROCESSING
advance "$L4" 2   # -> UNDERWRITING
advance "$L5" 2   # -> UNDERWRITING
advance "$L6" 3   # -> CONDITIONAL_APPROVAL
advance "$L7" 4   # -> CLEAR_TO_CLOSE

echo
echo "Running AUS and underwriting submission..."
for ln in "$L4" "$L5" "$L6"; do
    [ -n "$ln" ] || continue
    curl -s -o /dev/null -X POST "$API/underwriting/aus/$ln?engine=DU"
    curl -s -o /dev/null -X POST "$API/underwriting/submit/$ln?underwriterId=uw_jones"
    echo "  AUS + submit done for $ln"
done

echo
echo "Requesting borrower documents..."
# request_docs <loanNumber> <docType>...
request_docs() {
    local ln="$1"; shift
    [ -n "$ln" ] || return 0
    local dt
    for dt in "$@"; do
        curl -s -o /dev/null -X POST "$API/documents/$ln/request" -H 'Content-Type: application/json' \
            -d "{\"documentType\":\"$dt\",\"requestedBy\":\"seed_script\"}"
    done
    echo "  requested $# documents for $ln"
}
request_docs "$L2" 1003 PAYSTUB W2 BANK_STATEMENT
request_docs "$L3" 1003 PAYSTUB W2 BANK_STATEMENT TAX_RETURN
request_docs "$L4" 1003 PAYSTUB W2 BANK_STATEMENT APPRAISAL TITLE_COMMITMENT
request_docs "$L5" 1003 PAYSTUB W2 BANK_STATEMENT APPRAISAL
request_docs "$L6" 1003 PAYSTUB W2 BANK_STATEMENT APPRAISAL DD214
request_docs "$L7" 1003 PAYSTUB W2 BANK_STATEMENT APPRAISAL TITLE_COMMITMENT HOI

echo
echo "Creating HMDA records and TRID timelines..."
TODAY=$(date +%Y-%m-%d)
# hmda_and_trid <loanNumber> <race> <ethnicity> <sex> <income>
hmda_and_trid() {
    [ -n "$1" ] || return 0
    curl -s -o /dev/null -X POST "$API/compliance/hmda/$1" -H 'Content-Type: application/json' \
        -d "{\"race\":\"$2\",\"ethnicity\":\"$3\",\"sex\":\"$4\",\"raceObserved\":\"NOT_OBSERVED\",\"sexObserved\":\"NOT_OBSERVED\",\"incomeAmount\":$5,\"collectionMethod\":\"IN_PERSON\"}"
    curl -s -o /dev/null -X POST "$API/compliance/trid/$1/init" -H 'Content-Type: application/json' \
        -d "{\"applicationDate\":\"$TODAY\"}"
    curl -s -o /dev/null -X POST "$API/compliance/trid/$1/le-sent" -H 'Content-Type: application/json' \
        -d "{\"sentDate\":\"$TODAY\",\"version\":\"1\"}"
    echo "  HMDA + TRID recorded for $1"
}
hmda_and_trid "$L1" WHITE   NOT_HISPANIC MALE   174000
hmda_and_trid "$L2" ASIAN   NOT_HISPANIC FEMALE 117600
hmda_and_trid "$L3" WHITE   NOT_HISPANIC MALE    98400
hmda_and_trid "$L4" ASIAN   NOT_HISPANIC FEMALE 252000
hmda_and_trid "$L5" WHITE   NOT_HISPANIC MALE   165000
hmda_and_trid "$L6" OTHER   HISPANIC     FEMALE 124800
hmda_and_trid "$L7" WHITE   NOT_HISPANIC MALE   109200
hmda_and_trid "$L8" BLACK   NOT_HISPANIC FEMALE 134400

echo
echo "Running compliance checks..."
for ln in "$L4" "$L5" "$L6" "$L7"; do
    [ -n "$ln" ] || continue
    curl -s -o /dev/null -X POST "$API/compliance/check/$ln"
    echo "  compliance checked for $ln"
done

echo
echo "Pipeline by stage:"
curl -s "$API/pipeline/aging"
echo
echo "Done. Open $BASE to view the pipeline."
