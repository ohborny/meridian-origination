/* ================================================================
   MortgageLOS Web UI - Client JavaScript
   Originally written 2015, patched 2018, 2020.
   Uses XMLHttpRequest because jQuery was removed in 2019 after
   a security audit. Do NOT add jQuery back.
   ================================================================ */

var currentLoanNumber = null;
var currentLoan = null;
var pipelineData = [];
// productId -> productType, needed because LoanData carries the product id but the
// document checklist and eligibility rules are keyed on the product type.
var productTypeById = {};
var officerNameById = {};
var branchNameById = {};
var API = '/api';

// ===== INIT =====
function init() {
    updateClock();
    setInterval(updateClock, 1000);
    loadPipeline();
    loadBranches();
    loadProducts();
    loadOfficers();
}

function updateClock() {
    var now = new Date();
    var d = now.toLocaleDateString('en-US');
    var t = now.toLocaleTimeString('en-US', { hour12: true });
    document.getElementById('clock').textContent = d + ' ' + t;
}

// ===== API HELPER =====
function api(method, url, body, callback) {
    var xhr = new XMLHttpRequest();
    xhr.open(method, API + url, true);
    xhr.setRequestHeader('Content-Type', 'application/json');
    xhr.onreadystatechange = function() {
        if (xhr.readyState === 4) {
            if (xhr.status >= 200 && xhr.status < 300) {
                var data = null;
                try { data = xhr.responseText ? JSON.parse(xhr.responseText) : null; }
                catch (e) { data = xhr.responseText; }
                callback(null, data);
            } else {
                var err = 'Request failed (' + xhr.status + ')';
                try { err = JSON.parse(xhr.responseText).error || err; } catch (e) {}
                callback(err, null);
            }
        }
    };
    xhr.send(body ? JSON.stringify(body) : null);
}

// ===== VIEW SWITCHING =====
function showView(viewId) {
    var views = document.getElementsByClassName('view');
    for (var i = 0; i < views.length; i++) {
        views[i].style.display = 'none';
    }
    document.getElementById(viewId).style.display = '';
}

function showPipeline() {
    showView('view-pipeline');
    setBreadcrumb('Pipeline');
    loadPipeline();
}

function showNewApplication() {
    showView('view-newapp');
    setBreadcrumb('New Application');
}

function showSearch() {
    showView('view-search');
    setBreadcrumb('Loan Search');
}

function showAgingReport() {
    showView('view-aging');
    setBreadcrumb('Aging Report');
    loadAgingReport();
}

function setBreadcrumb(text) {
    document.getElementById('breadcrumb-current').textContent = text;
}

// ===== PIPELINE =====
function loadPipeline() {
    document.getElementById('pipeline-message').className = 'info-msg';
    document.getElementById('pipeline-message').textContent = 'Loading pipeline...';

    api('GET', '/pipeline', null, function(err, data) {
        if (err) {
            document.getElementById('pipeline-message').className = 'error-msg';
            document.getElementById('pipeline-message').textContent = 'Error loading pipeline: ' + err;
            return;
        }

        pipelineData = data || [];
        renderPipeline();
    });
}

function renderPipeline() {
    var msg = document.getElementById('pipeline-message');
    var container = document.getElementById('pipeline-table');

    var statusFilter = document.getElementById('filter-status').value;
    var branchFilter = document.getElementById('filter-branch').value;

    var filtered = pipelineData.filter(function(loan) {
        if (statusFilter && loan.loanStatus !== statusFilter) return false;
        if (branchFilter && loan.branchId !== parseInt(branchFilter)) return false;
        return true;
    });

    if (filtered.length === 0) {
        msg.className = 'info-msg';
        msg.textContent = 'No loans found in pipeline.';
        container.innerHTML = '';
        return;
    }

    msg.style.display = 'none';

    var html = '<table class="data-table"><thead><tr>';
    html += '<th>Loan #</th><th>Borrower</th><th>Status</th><th>Purpose</th>';
    html += '<th style="text-align:right;">Amount</th><th style="text-align:right;">LTV</th><th>State</th>';
    html += '<th>Loan Officer</th><th>Branch</th><th>App Date</th><th style="text-align:right;">Days</th>';
    html += '</tr></thead><tbody>';

    filtered.forEach(function(loan) {
        var days = loan.applicationDt ? Math.floor((new Date() - new Date(loan.applicationDt)) / 86400000) : 0;
        var borrower = (loan.borrowerFirstName || '') + ' ' + (loan.borrowerLastName || '');
        var lo = officerNameById[loan.loanOfficerId] || (loan.loanOfficerId ? 'LO#' + loan.loanOfficerId : 'Unassigned');
        var branch = branchNameById[loan.branchId] || (loan.branchId ? 'Branch #' + loan.branchId : 'N/A');
        var amount = loan.loanAmount ? '$' + Number(loan.loanAmount).toLocaleString('en-US', {minimumFractionDigits: 0, maximumFractionDigits: 0}) : '';
        var appDate = loan.applicationDt ? formatDate(loan.applicationDt) : '';

        html += '<tr onclick="showLoanDetail(\'' + loan.loanNumber + '\')">';
        html += '<td><a href="#" onclick="showLoanDetail(\'' + loan.loanNumber + '\'); return false;">' + loan.loanNumber + '</a></td>';
        html += '<td>' + escHtml(borrower) + '</td>';
        html += '<td><span class="status-' + (loan.loanStatus || '') + '">' + escHtml(loan.loanStatus || '') + '</span></td>';
        html += '<td>' + escHtml(loan.loanPurpose || '') + '</td>';
        html += '<td style="text-align:right;">' + amount + '</td>';
        html += '<td style="text-align:right;">' + (loan.ltv ? Number(loan.ltv).toFixed(2) + '%' : '') + '</td>';
        html += '<td>' + escHtml(loan.propertyState || '') + '</td>';
        html += '<td>' + escHtml(lo) + '</td>';
        html += '<td>' + escHtml(branch) + '</td>';
        html += '<td>' + appDate + '</td>';
        html += '<td style="text-align:right;">' + days + '</td>';
        html += '</tr>';
    });

    html += '</tbody></table>';
    container.innerHTML = html;
}

function filterPipeline() {
    renderPipeline();
}

// ===== LOAN DETAIL =====
function showLoanDetail(loanNumber) {
    currentLoanNumber = loanNumber;
    showView('view-detail');
    setBreadcrumb('Loan ' + loanNumber);
    document.getElementById('detail-title').textContent = 'Loan ' + loanNumber;
    switchTab('general');
    loadLoanDetail(loanNumber);
}

function loadLoanDetail(loanNumber) {
    api('GET', '/loans/' + loanNumber, null, function(err, loan) {
        if (err || !loan) {
            document.getElementById('loan-general').innerHTML = '<div class="error-msg">Error loading loan: ' + (err || 'not found') + '</div>';
            return;
        }
        currentLoan = loan;
        renderLoanGeneral(loan);
        loadCreditTab(loanNumber);
        loadUwTab(loanNumber);
        loadComplianceTab(loanNumber);
        loadDocsTab(loanNumber);
    });
}

function renderLoanGeneral(loan) {
    var borrower = (loan.borrowerFirstName || '') + ' ' + (loan.borrowerLastName || '');
    var coBorrower = loan.coborrowerFirstName ? ((loan.coborrowerFirstName || '') + ' ' + (loan.coborrowerLastName || '')) : 'None';
    var amount = loan.loanAmount ? '$' + Number(loan.loanAmount).toLocaleString('en-US', {minimumFractionDigits: 2}) : 'N/A';
    var rate = loan.interestRate ? loan.interestRate + '%' : 'Not Set';
    var ltv = loan.ltv ? loan.ltv + '%' : 'N/A';
    var dti = loan.dti ? loan.dti + '%' : 'N/A';
    var propValue = loan.appraisedValue ? '$' + Number(loan.appraisedValue).toLocaleString('en-US') : (loan.propertyValue ? '$' + Number(loan.propertyValue).toLocaleString('en-US') : 'N/A');

    var html = '';

    // Loan Summary
    html += '<div class="detail-section">';
    html += '<div class="detail-section-header">Loan Summary</div>';
    html += '<table class="detail-grid"><tbody>';
    html += detailRow('Loan Number', loan.loanNumber);
    html += detailRow('Status', '<span class="status-' + (loan.loanStatus || '') + '">' + escHtml(loan.loanStatus || '') + '</span>');
    html += detailRow('Sub-Status', loan.loanSubstatus || 'None');
    html += detailRow('Loan Purpose', loan.loanPurpose || 'N/A');
    html += detailRow('Loan Amount', amount);
    html += detailRow('Interest Rate', rate);
    html += detailRow('Term', loan.termMonths ? loan.termMonths + ' months' : 'N/A');
    html += detailRow('Amortization', loan.amortizationType || 'N/A');
    html += detailRow('LTV', ltv);
    html += detailRow('CLTV', loan.cltv ? loan.cltv + '%' : 'N/A');
    html += detailRow('DTI', dti);
    html += detailRow('Front-End DTI', loan.htdti ? loan.htdti + '%' : 'N/A');
    html += '</tbody></table></div>';

    // Borrower
    html += '<div class="detail-section">';
    html += '<div class="detail-section-header">Borrower Information</div>';
    html += '<table class="detail-grid"><tbody>';
    html += detailRow('Borrower Name', escHtml(borrower));
    html += detailRow('SSN (Last 4)', loan.borrowerSsnLast4 ? 'XXX-XX-' + loan.borrowerSsnLast4 : 'N/A');
    html += detailRow('Credit Score', loan.borrowerCreditScore ? loan.borrowerCreditScore : 'Not Pulled');
    html += detailRow('Co-Borrower', escHtml(coBorrower));
    html += detailRow('Co-Borrower Credit', loan.coborrowerCreditScore ? loan.coborrowerCreditScore : 'N/A');
    html += '</tbody></table></div>';

    // Property
    html += '<div class="detail-section">';
    html += '<div class="detail-section-header">Property Information</div>';
    html += '<table class="detail-grid"><tbody>';
    html += detailRow('Address', escHtml(loan.propertyAddr1 || 'N/A'));
    html += detailRow('City/State/Zip', escHtml((loan.propertyCity || '') + ', ' + (loan.propertyState || '') + ' ' + (loan.propertyZip || '')));
    html += detailRow('Property Type', loan.propertyType || 'N/A');
    html += detailRow('Occupancy', loan.propertyOccupancy || 'N/A');
    html += detailRow('Property Value', propValue);
    html += detailRow('Appraised Value', loan.appraisedValue ? '$' + Number(loan.appraisedValue).toLocaleString('en-US') : 'Not Appraised');
    html += '</tbody></table></div>';

    // Dates
    html += '<div class="detail-section">';
    html += '<div class="detail-section-header">Key Dates</div>';
    html += '<table class="detail-grid"><tbody>';
    html += detailRow('Application Date', formatDate(loan.applicationDt));
    html += detailRow('Processing Date', formatDate(loan.processingDt));
    html += detailRow('Underwriting Date', formatDate(loan.underwritingDt));
    html += detailRow('Approval Date', formatDate(loan.approvalDt));
    html += detailRow('CTC Date', formatDate(loan.ctcDt));
    html += detailRow('Closing Date', formatDate(loan.closingDt));
    html += detailRow('Funded Date', formatDate(loan.fundedDt));
    html += detailRow('Shipped Date', formatDate(loan.shippedDt));
    html += detailRow('Purchased Date', formatDate(loan.purchasedDt));
    html += '</tbody></table></div>';

    // Lock Info
    html += '<div class="detail-section">';
    html += '<div class="detail-section-header">Rate Lock Information</div>';
    html += '<table class="detail-grid"><tbody>';
    html += detailRow('Lock ID', loan.lockId || 'Not Locked');
    html += detailRow('Lock Rate', loan.lockRate ? loan.lockRate + '%' : 'N/A');
    html += detailRow('Lock Expiration', formatDate(loan.lockExpDt));
    html += detailRow('Base Rate', loan.baseRate ? loan.baseRate + '%' : 'N/A');
    html += detailRow('Total Points', loan.totalPoints ? loan.totalPoints + ' pts' : 'N/A');
    html += detailRow('Lender Credit', loan.lenderCredit ? '$' + Number(loan.lenderCredit).toLocaleString('en-US') : 'N/A');
    html += '</tbody></table></div>';

    // Audit
    html += '<div class="detail-section">';
    html += '<div class="detail-section-header">Audit Trail</div>';
    html += '<table class="detail-grid"><tbody>';
    html += detailRow('Created By', loan.createdBy || 'N/A');
    html += detailRow('Created Date', formatDateTime(loan.createdDt));
    html += detailRow('Updated By', loan.updatedBy || 'N/A');
    html += detailRow('Updated Date', formatDateTime(loan.updatedDt));
    html += '</tbody></table></div>';

    document.getElementById('loan-general').innerHTML = html;
}

// ===== CREDIT TAB =====
function loadCreditTab(loanNumber) {
    document.getElementById('loan-credit').innerHTML = '<div class="loading">Loading credit information...</div>';

    api('GET', '/credit/' + loanNumber, null, function(err, report) {
        var html = '';

        if (err || !report) {
            html = '<div class="info-msg">No credit report on file. Click "Pull Credit" in the action bar above.</div>';
            document.getElementById('loan-credit').innerHTML = html;
            return;
        }

        // Credit Scores. The report row carries one column per bureau rather than a
        // nested score list, so the table is assembled from those three columns.
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">Credit Scores</div>';
        html += '<table class="data-table"><thead><tr>';
        html += '<th>Bureau</th><th>Report Reference</th><th>Score</th>';
        html += '</tr></thead><tbody>';
        var bureaus = [
            ['Experian', report.experianReportId, report.experianScore],
            ['Equifax', report.equifaxReportId, report.equifaxScore],
            ['TransUnion', report.transunionReportId, report.transunionScore]
        ];
        bureaus.forEach(function(b) {
            html += '<tr><td>' + b[0] + '</td>';
            html += '<td>' + escHtml(b[1] || 'N/A') + '</td>';
            html += '<td><b>' + (b[2] || 'N/A') + '</b></td></tr>';
        });
        html += '<tr style="background-color:#e8e0c8;"><td><b>Representative</b></td><td></td>';
        html += '<td><b>' + (report.representativeScore || 'N/A') + '</b></td></tr>';
        html += '</tbody></table></div>';

        // Credit Report Summary
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">Credit Report Summary</div>';
        html += '<table class="detail-grid"><tbody>';
        html += detailRow('Report ID', report.reportId || 'N/A');
        html += detailRow('Pulled Date', formatDateTime(report.pulledDt));
        html += detailRow('Pulled By', escHtml(report.pulledBy || 'N/A'));
        html += detailRow('Pull Type', escHtml(report.pullType || 'N/A'));
        html += detailRow('Report Type', escHtml(report.reportType || 'N/A'));
        html += detailRow('Vendor', escHtml(report.vendorName || 'N/A'));
        html += detailRow('Representative Score', report.representativeScore || 'N/A');
        html += detailRow('Highest / Lowest', (report.highestScore || 'N/A') + ' / ' + (report.lowestScore || 'N/A'));
        html += detailRow('All Bureaus Scored', report.hasAllScores ? 'Yes' : 'No');
        html += detailRow('File Status', escHtml(report.fileStatus || 'N/A'));
        html += detailRow('Fraud Alert', report.fraudAlert ? '<span style="color:#cc0000;font-weight:bold;">YES</span>' : 'No');
        html += detailRow('Active Alerts', report.activeAlertCount || 0);
        html += '</tbody></table></div>';

        html += '<div id="dti-section"></div>';
        html += '<div id="liabilities-section"></div>';

        document.getElementById('loan-credit').innerHTML = html;

        // DTI is stored separately from the report
        api('GET', '/credit/dti/' + loanNumber, null, function(dtiErr, dti) {
            var el = document.getElementById('dti-section');
            if (!el) return;
            if (dtiErr || !dti) {
                el.innerHTML = '<div class="detail-section"><div class="detail-section-header">Debt-to-Income Calculation</div>' +
                    '<div style="padding:8px;" class="info-msg">No DTI calculation on file for this loan.</div></div>';
                return;
            }
            var dtiHtml = '<div class="detail-section">';
            dtiHtml += '<div class="detail-section-header">Debt-to-Income Calculation</div>';
            dtiHtml += '<table class="detail-grid"><tbody>';
            dtiHtml += detailRow('Borrower Income', money(dti.monthlyIncome));
            dtiHtml += detailRow('Co-Borrower Income', money(dti.coborrowerIncome));
            dtiHtml += detailRow('Total Monthly Income', money(dti.totalMonthlyIncome));
            dtiHtml += detailRow('Proposed Housing Payment', money(dti.proposedHousingPayment));
            dtiHtml += detailRow('Total Monthly Debts', money(dti.totalMonthlyDebts));
            dtiHtml += detailRow('Front-End DTI', '<b>' + pct(dti.frontEndDti) + '</b>');
            dtiHtml += detailRow('Back-End DTI', '<b>' + pct(dti.backEndDti) + '</b>');
            dtiHtml += detailRow('Effective Front-End DTI', pct(dti.effectiveFrontEndDti));
            dtiHtml += detailRow('Effective Back-End DTI', pct(dti.effectiveBackEndDti));
            dtiHtml += detailRow('Guideline', escHtml(dti.dtiGuideline || 'N/A'));
            dtiHtml += detailRow('Manual Override', dti.manualOverrideDti ? pct(dti.manualOverrideDti) : 'None');
            dtiHtml += detailRow('Within Guideline', dti.exceedsGuideline
                ? '<span style="color:#cc0000;font-weight:bold;">EXCEEDS GUIDELINE</span>'
                : '<span style="color:#006600;font-weight:bold;">WITHIN GUIDELINE</span>');
            dtiHtml += detailRow('Calculated', formatDateTime(dti.calculatedDt) + ' by ' + escHtml(dti.calculatedBy || 'N/A'));
            dtiHtml += '</tbody></table></div>';
            el.innerHTML = dtiHtml;
        });

        // Liabilities come from their own endpoint
        api('GET', '/credit/' + loanNumber + '/liabilities', null, function(liaErr, liabilities) {
            var el = document.getElementById('liabilities-section');
            if (!el) return;
            var h = '<div class="detail-section">';
            h += '<div class="detail-section-header">Liabilities</div>';
            if (liaErr || !liabilities || liabilities.length === 0) {
                h += '<div style="padding:8px;" class="info-msg">No liabilities on file.</div></div>';
                el.innerHTML = h;
                return;
            }
            h += '<table class="data-table"><thead><tr>';
            h += '<th>Creditor</th><th>Type</th><th>Account #</th><th style="text-align:right;">Balance</th>';
            h += '<th style="text-align:right;">Monthly Pmt</th><th>Status</th><th>Late 30/60/90</th><th>In DTI</th>';
            h += '</tr></thead><tbody>';
            liabilities.forEach(function(l) {
                h += '<tr>';
                h += '<td>' + escHtml(l.creditorName || '') + '</td>';
                h += '<td>' + escHtml(l.accountType || '') + '</td>';
                h += '<td>' + escHtml(l.accountNumber || '') + '</td>';
                h += '<td style="text-align:right;">' + money(l.currentBalance) + '</td>';
                h += '<td style="text-align:right;">' + money(l.monthlyPayment) + '</td>';
                h += '<td>' + (l.isDelinquent
                    ? '<span style="color:#cc0000;font-weight:bold;">' + escHtml(l.accountStatus || 'DELINQUENT') + '</span>'
                    : escHtml(l.accountStatus || '')) + '</td>';
                h += '<td>' + (l.late30Count || 0) + ' / ' + (l.late60Count || 0) + ' / ' + (l.late90Count || 0) + '</td>';
                h += '<td>' + (l.isIncludedInDti ? 'Yes' : 'No') + '</td>';
                h += '</tr>';
            });
            h += '</tbody></table></div>';
            el.innerHTML = h;
        });
    });
}

// ===== UNDERWRITING TAB =====
function loadUwTab(loanNumber) {
    document.getElementById('loan-uw').innerHTML = '<div class="loading">Loading underwriting information...</div>';

    var html = '';
    var loadedAUS = false;
    var loadedConditions = false;

    function checkDone() {
        if (loadedAUS && loadedConditions) {
            document.getElementById('loan-uw').innerHTML = html;
        }
    }

    // AUS Results
    api('GET', '/underwriting/aus/' + loanNumber, null, function(err, aus) {
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">Automated Underwriting (AUS)</div>';
        if (err || !aus) {
            html += '<div style="padding:8px;" class="info-msg">No AUS results on file. Click "Run AUS" in the action bar above.</div>';
        } else {
            html += '<table class="detail-grid"><tbody>';
            html += detailRow('AUS ID', aus.ausId || 'N/A');
            html += detailRow('Engine', escHtml(aus.engine || 'N/A'));
            html += detailRow('Result', '<span class="aus-' + (aus.result || '') + '">' + escHtml(aus.result || '') + '</span>');
            html += detailRow('Run Date', formatDateTime(aus.runDt));
            html += detailRow('Run By', escHtml(aus.runBy || 'N/A'));
            html += detailRow('Credit Score Used', aus.creditScore || 'N/A');
            html += detailRow('LTV', pct(aus.ltv));
            html += detailRow('DTI', pct(aus.dti));
            html += '</tbody></table>';

            if (aus.findings && aus.findings.length > 0) {
                // A fresh AUS submission returns finding objects, but a stored result read
                // back from aus_results.findings is only the persisted description text.
                var rich = typeof aus.findings[0] === 'object';
                html += '<table class="data-table" style="margin-top:5px;"><thead><tr>';
                if (rich) {
                    html += '<th>Code</th><th>Severity</th><th>Description</th><th>Condition</th>';
                } else {
                    html += '<th>AUS Findings</th>';
                }
                html += '</tr></thead><tbody>';
                aus.findings.forEach(function(f) {
                    html += '<tr>';
                    if (rich) {
                        html += '<td>' + escHtml(f.findingCode || '') + '</td>';
                        html += '<td>' + escHtml(f.severity || '') + '</td>';
                        html += '<td>' + escHtml(f.findingDesc || '') + '</td>';
                        html += '<td>' + (f.isCondition ? escHtml(f.conditionText || 'Yes') : 'No') + '</td>';
                    } else {
                        html += '<td>' + escHtml(String(f)) + '</td>';
                    }
                    html += '</tr>';
                });
                html += '</tbody></table>';
            }
        }
        html += '</div>';
        loadedAUS = true;
        checkDone();
    });

    // Conditions
    api('GET', '/underwriting/' + loanNumber + '/conditions', null, function(err, conditions) {
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">Underwriting Conditions</div>';
        if (err || !conditions || conditions.length === 0) {
            html += '<div style="padding:8px;" class="info-msg">No conditions on file.</div>';
        } else {
            html += '<table class="data-table"><thead><tr>';
            html += '<th>ID</th><th>Type</th><th>Category</th><th>Description</th><th>Status</th><th>Action</th>';
            html += '</tr></thead><tbody>';
            conditions.forEach(function(c) {
                var status = c.isSatisfied ? '<span class="condition-satisfied">Satisfied</span>' : '<span class="condition-outstanding">Outstanding</span>';
                var action = c.isSatisfied ? '' :
                    '<button onclick="satisfyCondition(' + c.conditionId + ')" class="btn" style="font-size:10px;">Satisfy</button>';
                html += '<tr>';
                html += '<td>' + c.conditionId + '</td>';
                html += '<td>' + escHtml(c.conditionType || '') + '</td>';
                html += '<td>' + escHtml(c.conditionCategory || '') + '</td>';
                html += '<td>' + escHtml(c.conditionDesc || '') + '</td>';
                html += '<td>' + status + '</td>';
                html += '<td>' + action + '</td>';
                html += '</tr>';
            });
            html += '</tbody></table>';
        }
        html += '</div>';
        loadedConditions = true;
        checkDone();
    });
}

// ===== COMPLIANCE TAB =====
function loadComplianceTab(loanNumber) {
    document.getElementById('loan-compliance').innerHTML = '<div class="loading">Loading compliance information...</div>';

    var html = '';
    var loaded = 0;
    var total = 3;

    function checkDone() {
        loaded++;
        if (loaded === total) {
            document.getElementById('loan-compliance').innerHTML = html;
        }
    }

    // Compliance Checks
    api('GET', '/compliance/' + loanNumber, null, function(err, checks) {
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">Compliance Checks</div>';
        if (err || !checks || checks.length === 0) {
            html += '<div style="padding:8px;" class="info-msg">No compliance checks on file. Click "Run Compliance" in the action bar above.</div>';
        } else {
            html += '<table class="data-table"><thead><tr>';
            html += '<th>Check Type</th><th>Subtype</th><th>Status</th><th>Result</th><th>Checked</th><th>Exception</th>';
            html += '</tr></thead><tbody>';
            checks.forEach(function(c) {
                html += '<tr>';
                html += '<td>' + escHtml(c.chkType || '') + '</td>';
                html += '<td>' + escHtml(c.chkSubtype || '') + '</td>';
                html += '<td><span class="compliance-' + (c.chkStatus || '') + '">' + escHtml(c.chkStatus || '') + '</span></td>';
                html += '<td>' + escHtml(c.chkResult || '') + '</td>';
                html += '<td>' + formatDateTime(c.chkDt) + '</td>';
                html += '<td>' + (c.hasException ? 'Exception #' + escHtml(c.exceptionId) : 'No') + '</td>';
                html += '</tr>';
            });
            html += '</tbody></table>';
        }
        html += '</div>';
        checkDone();
    });

    // HMDA
    api('GET', '/compliance/hmda/' + loanNumber, null, function(err, hmda) {
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">HMDA Data</div>';
        if (err || !hmda) {
            html += '<div style="padding:8px;" class="info-msg">No HMDA data on file.</div>';
        } else {
            // The table keeps both the pre-2018 (*_old) and 2018+ (*_new) demographic
            // columns. usesNewRaceFormat says which set is authoritative for this record.
            var useNew = hmda.usesNewRaceFormat !== false;
            html += '<table class="detail-grid"><tbody>';
            html += detailRow('HMDA ID', hmda.hmdaId || 'N/A');
            html += detailRow('Reporting Year', hmda.reportingYear || 'N/A');
            html += detailRow('Format', useNew ? '2018+ (disaggregated)' : 'Pre-2018 (legacy)');
            html += detailRow('Race', escHtml((useNew ? hmda.raceNew : hmda.raceOld) || 'Not Provided'));
            html += detailRow('Ethnicity', escHtml((useNew ? hmda.ethnicityNew : hmda.ethnicityOld) || 'Not Provided'));
            html += detailRow('Sex', escHtml((useNew ? hmda.sexNew : hmda.sexOld) || 'Not Provided'));
            html += detailRow('Race Observed', escHtml(hmda.raceObserved || 'N/A'));
            html += detailRow('Sex Observed', escHtml(hmda.sexObserved || 'N/A'));
            html += detailRow('Action Taken', escHtml(hmda.actionTaken || 'Not Yet Reported'));
            html += detailRow('Action Taken Date', formatDate(hmda.actionTakenDt));
            html += detailRow('Income', hmda.incomeAmount ? '$' + Number(hmda.incomeAmount).toLocaleString('en-US') : 'N/A');
            html += detailRow('Loan Type Code', escHtml(hmda.loanTypeCode || 'N/A'));
            html += detailRow('Loan Purpose Code', escHtml(hmda.loanPurposeCode || 'N/A'));
            html += detailRow('Lien Status', escHtml(hmda.lienStatus || 'N/A'));
            html += detailRow('HOEPA Status', escHtml(hmda.hoepaStatus || 'N/A'));
            html += detailRow('Rate Spread', hmda.rateSpread ? hmda.rateSpread : 'N/A');
            html += detailRow('Census Tract', escHtml(hmda.propertyCensusTract || 'N/A'));
            html += '</tbody></table>';
        }
        html += '</div>';
        checkDone();
    });

    // TRID
    api('GET', '/compliance/trid/' + loanNumber, null, function(err, trid) {
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">TRID Timeline</div>';
        if (err || !trid) {
            html += '<div style="padding:8px;" class="info-msg">No TRID timeline on file.</div>';
        } else {
            html += '<table class="detail-grid"><tbody>';
            html += detailRow('Application Date', formatDate(trid.applicationDt));
            html += detailRow('LE Required By', formatDate(trid.leRequiredByDt));
            html += detailRow('LE Sent Date', formatDate(trid.leSentDt));
            html += detailRow('LE Received Date', formatDate(trid.leReceivedDt));
            html += detailRow('LE Version', trid.leVersion || 'N/A');
            html += detailRow('LE Status', trid.leIsSent
                ? (trid.leIsLate
                    ? '<span style="color:#cc0000;font-weight:bold;">SENT LATE</span>'
                    : '<span style="color:#006600;font-weight:bold;">SENT ON TIME</span>')
                : '<span style="color:#cc6600;font-weight:bold;">NOT SENT</span>');
            html += detailRow('Revised LE Required', formatDate(trid.revisedLeRequiredDt));
            html += detailRow('Revised LE Sent', formatDate(trid.revisedLeSentDt));
            html += detailRow('CD Prepared Date', formatDate(trid.cdPreparedDt));
            html += detailRow('CD Sent Date', formatDate(trid.cdSentDt));
            html += detailRow('CD Received Date', formatDate(trid.cdReceivedDt));
            html += detailRow('CD Status', trid.cdIsSent
                ? (trid.cdWaitingMet
                    ? '<span style="color:#006600;font-weight:bold;">3-DAY WAIT MET</span>'
                    : '<span style="color:#cc0000;font-weight:bold;">3-DAY WAIT NOT MET</span>')
                : '<span style="color:#cc6600;font-weight:bold;">NOT SENT</span>');
            html += detailRow('Consummation Date', formatDate(trid.consummationDt));
            html += detailRow('Rate Lock', formatDate(trid.rateLockDt) + ' - ' + formatDate(trid.rateLockExpDt));
            html += detailRow('Changed Circumstance', trid.hasChangedCircumstance
                ? escHtml(trid.changedCircumstanceDesc || 'Yes') + ' (' + formatDate(trid.changedCircumstanceDt) + ')'
                : 'None');
            html += detailRow('LE Tolerance Cure', trid.leToleranceCure ? money(trid.leToleranceCure) : 'None');
            html += '</tbody></table>';
        }
        html += '</div>';
        checkDone();
    });
}

// ===== DOCUMENTS TAB =====
function loadDocsTab(loanNumber) {
    document.getElementById('loan-docs').innerHTML = '<div class="loading">Loading document information...</div>';

    var html = '';
    var loaded = 0;
    var total = 2;

    function checkDone() {
        loaded++;
        if (loaded === total) {
            document.getElementById('loan-docs').innerHTML = html;
        }
    }

    // Documents
    api('GET', '/documents/' + loanNumber, null, function(err, docs) {
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">Documents on File</div>';
        if (err || !docs || docs.length === 0) {
            html += '<div style="padding:8px;" class="info-msg">No documents on file.</div>';
        } else {
            html += '<table class="data-table"><thead><tr>';
            html += '<th>ID</th><th>Type</th><th>Name</th><th>Status</th><th>Received</th><th>Verified</th><th>Action</th>';
            html += '</tr></thead><tbody>';
            docs.forEach(function(d) {
                var status = '<span class="doc-' + (d.status || '') + '">' + escHtml(d.status || '') + '</span>';
                var action = '';
                if (!d.isVerified && d.status !== 'REJECTED' && d.status !== 'WAIVED') {
                    action = '<button onclick="verifyDocument(' + d.documentId + ')" class="btn" style="font-size:10px;">Verify</button>';
                }
                html += '<tr>';
                html += '<td>' + d.documentId + '</td>';
                html += '<td>' + escHtml(d.documentType || '') + '</td>';
                html += '<td>' + escHtml(d.documentName || '') + '</td>';
                html += '<td>' + status + '</td>';
                html += '<td>' + formatDate(d.receivedDt) + '</td>';
                html += '<td>' + (d.isVerified ? 'Yes' : 'No') + '</td>';
                html += '<td>' + action + '</td>';
                html += '</tr>';
            });
            html += '</tbody></table>';
        }
        html += '</div>';
        checkDone();
    });

    // Checklist. The requirements matrix is keyed on loan type and purpose, so both
    // have to be supplied or the service returns an empty list.
    var qs = '';
    if (currentLoan) {
        var loanType = productTypeById[currentLoan.productId] || 'CONVENTIONAL';
        qs = '?loanType=' + encodeURIComponent(loanType) +
             '&loanPurpose=' + encodeURIComponent(currentLoan.loanPurpose || 'PURCHASE');
    }

    api('GET', '/documents/checklist/' + loanNumber + qs, null, function(err, checklist) {
        html += '<div class="detail-section">';
        html += '<div class="detail-section-header">Document Checklist</div>';
        if (err || !checklist) {
            html += '<div style="padding:8px;" class="info-msg">No checklist available. Ensure loan type and purpose are set.</div>';
        } else {
            html += '<table class="detail-grid"><tbody>';
            html += detailRow('Total Required', checklist.totalRequired || 0);
            html += detailRow('Received', checklist.totalReceived || 0);
            html += detailRow('Missing', checklist.totalMissing || 0);
            html += detailRow('Expired', checklist.totalExpired || 0);
            html += detailRow('All Required Received', checklist.allRequiredDocsReceived
                ? '<span style="color:#006600;font-weight:bold;">YES</span>'
                : '<span style="color:#cc0000;font-weight:bold;">NO</span>');
            html += '</tbody></table>';

            if (checklist.items && checklist.items.length > 0) {
                html += '<table class="data-table" style="margin-top:5px;"><thead><tr>';
                html += '<th>Document Type</th><th>Document Name</th><th>Required</th><th>Received</th><th>Status</th>';
                html += '</tr></thead><tbody>';
                checklist.items.forEach(function(item) {
                    html += '<tr>';
                    html += '<td>' + escHtml(item.documentType || '') + '</td>';
                    html += '<td>' + escHtml(item.documentName || '') + '</td>';
                    html += '<td>' + (item.isRequired ? 'Yes' : 'No') + '</td>';
                    html += '<td>' + (item.isReceived ? formatDate(item.receivedDt) : 'No') + '</td>';
                    html += '<td><span class="doc-' + (item.status || '') + '">' + escHtml(item.status || '') + '</span></td>';
                    html += '</tr>';
                });
                html += '</tbody></table>';
            }
        }
        html += '</div>';
        checkDone();
    });
}

// ===== TAB SWITCHING =====
function switchTab(tabName) {
    var tabs = ['general', 'credit', 'uw', 'compliance', 'docs'];
    tabs.forEach(function(t) {
        document.getElementById('tab-' + t).className = 'tab';
        document.getElementById('tab-content-' + t).style.display = 'none';
    });
    document.getElementById('tab-' + tabName).className = 'tab tab-active';
    document.getElementById('tab-content-' + tabName).style.display = '';
}

// ===== ACTIONS =====
function pullCredit() {
    if (!currentLoanNumber) return;
    if (!confirm('Pull credit report for loan ' + currentLoanNumber + '?')) return;
    api('POST', '/credit/pull/' + currentLoanNumber, null, function(err, data) {
        if (err) {
            alert('Credit pull failed: ' + err);
        } else {
            alert('Credit pulled successfully.\nRepresentative score: ' + (data.representativeScore || 'N/A') +
                  '\nExperian: ' + (data.experianScore || 'N/A') +
                  '  Equifax: ' + (data.equifaxScore || 'N/A') +
                  '  TransUnion: ' + (data.transunionScore || 'N/A'));
            loadCreditTab(currentLoanNumber);
            loadLoanDetail(currentLoanNumber);
        }
    });
}

// Income and the proposed housing payment are not carried on the loan record, so they
// have to be entered here. AUS reads whatever this writes, so it must run before AUS.
function calculateDti() {
    if (!currentLoanNumber || !currentLoan) return;

    var income = prompt('Borrower total monthly income:', '12000');
    if (income === null) return;
    var coIncome = prompt('Co-borrower monthly income (0 if none):', '0');
    if (coIncome === null) return;

    // Rough P&I estimate at 7% so the operator has a starting figure.
    var amt = Number(currentLoan.loanAmount) || 0;
    var n = Number(currentLoan.termMonths) || 360;
    var r = 0.07 / 12;
    var est = amt > 0 ? Math.round(amt * r / (1 - Math.pow(1 + r, -n))) : 0;
    var housing = prompt('Proposed monthly housing payment (PITI):', String(est));
    if (housing === null) return;

    var body = {
        monthlyIncome: parseFloat(income) || 0,
        coborrowerIncome: parseFloat(coIncome) || 0,
        proposedHousingPayment: parseFloat(housing) || 0,
        loanType: productTypeById[currentLoan.productId] || 'CONVENTIONAL'
    };

    api('POST', '/credit/dti/' + currentLoanNumber, body, function(err, data) {
        if (err) {
            alert('DTI calculation failed: ' + err);
        } else {
            alert('DTI calculated.\nFront-end: ' + (data.frontEndDti || 0) + '%' +
                  '\nBack-end: ' + (data.backEndDti || 0) + '%' +
                  '\nGuideline: ' + (data.dtiGuideline || 'N/A') + '%' +
                  '\nExceeds guideline: ' + (data.exceedsGuideline ? 'YES' : 'No'));
            loadCreditTab(currentLoanNumber);
        }
    });
}

function runAus() {
    if (!currentLoanNumber) return;
    api('POST', '/underwriting/aus/' + currentLoanNumber + '?engine=DU', null, function(err, data) {
        if (err) {
            alert('AUS run failed: ' + err);
        } else {
            alert('AUS Result: ' + (data.result || 'N/A') + '\nEngine: ' + (data.engine || 'N/A'));
            loadUwTab(currentLoanNumber);
        }
    });
}

function submitUw() {
    if (!currentLoanNumber) return;
    var uwId = prompt('Enter underwriter ID:', 'uw_jones');
    if (!uwId) return;
    api('POST', '/underwriting/submit/' + currentLoanNumber + '?underwriterId=' + encodeURIComponent(uwId), null, function(err, data) {
        if (err) {
            alert('Submit failed: ' + err);
        } else {
            alert('Loan submitted to underwriting.');
            loadUwTab(currentLoanNumber);
            loadLoanDetail(currentLoanNumber);
        }
    });
}

function runCompliance() {
    if (!currentLoanNumber) return;
    api('POST', '/compliance/check/' + currentLoanNumber, null, function(err, data) {
        if (err) {
            alert('Compliance check failed: ' + err);
        } else {
            var passCount = 0, failCount = 0, warnCount = 0, reviewCount = 0;
            if (data && data.length) {
                data.forEach(function(c) {
                    if (c.chkStatus === 'PASS') passCount++;
                    else if (c.chkStatus === 'FAIL') failCount++;
                    else if (c.chkStatus === 'WARNING') warnCount++;
                    else if (c.chkStatus === 'MANUAL_REVIEW') reviewCount++;
                });
            }
            alert('Compliance checks complete.\nPassed: ' + passCount + '\nFailed: ' + failCount +
                  '\nWarnings: ' + warnCount + '\nManual review: ' + reviewCount);
            loadComplianceTab(currentLoanNumber);
        }
    });
}

function advanceStage() {
    if (!currentLoanNumber) return;
    if (!confirm('Advance loan ' + currentLoanNumber + ' to next pipeline stage?')) return;
    api('POST', '/pipeline/advance/' + currentLoanNumber + '?changedBy=jsmith', null, function(err, data) {
        if (err) {
            alert('Advance failed: ' + err);
        } else {
            alert('Loan advanced to next stage.');
            loadLoanDetail(currentLoanNumber);
        }
    });
}

function clearToClose() {
    if (!currentLoanNumber) return;
    if (!confirm('Issue Clear to Close for loan ' + currentLoanNumber + '?')) return;
    api('POST', '/underwriting/ctc/' + currentLoanNumber + '?clearedBy=jsmith', null, function(err, data) {
        if (err) {
            alert('Clear to Close failed: ' + err);
        } else {
            alert('Loan cleared to close!');
            loadLoanDetail(currentLoanNumber);
        }
    });
}

function satisfyCondition(conditionId) {
    api('PUT', '/underwriting/conditions/' + conditionId + '/satisfy?satisfiedBy=jsmith', null, function(err, data) {
        if (err) {
            alert('Failed to satisfy condition: ' + err);
        } else {
            alert('Condition satisfied.');
            loadUwTab(currentLoanNumber);
        }
    });
}

function verifyDocument(docId) {
    api('PUT', '/documents/' + docId + '/verify?verifiedBy=jsmith', null, function(err, data) {
        if (err) {
            alert('Failed to verify document: ' + err);
        } else {
            alert('Document verified.');
            loadDocsTab(currentLoanNumber);
        }
    });
}

// ===== NEW APPLICATION =====
function submitNewApplication() {
    var req = {
        borrowerFirstName: val('na-borrower-first'),
        borrowerLastName: val('na-borrower-last'),
        borrowerSsn: val('na-borrower-ssn'),
        borrowerDob: val('na-borrower-dob') || null,
        borrowerEmail: val('na-borrower-email'),
        borrowerPhone: val('na-borrower-phone'),
        coborrowerFirstName: val('na-coborrower-first') || null,
        coborrowerLastName: val('na-coborrower-last') || null,
        coborrowerSsn: val('na-coborrower-ssn') || null,
        coborrowerDob: null,
        loanPurpose: val('na-purpose'),
        productCode: val('na-product'),
        loanAmount: parseFloat(val('na-loan-amount')) || 0,
        propertyValue: parseFloat(val('na-property-value')) || 0,
        propertyAddr1: val('na-prop-addr'),
        propertyCity: val('na-prop-city'),
        propertyState: val('na-prop-state'),
        propertyZip: val('na-prop-zip'),
        propertyType: val('na-prop-type'),
        propertyOccupancy: val('na-prop-occupancy'),
        monthlyIncome: parseFloat(val('na-monthly-income')) || 0,
        coborrowerMonthlyIncome: parseFloat(val('na-coborrower-income')) || null,
        loanOfficerCode: val('na-loan-officer') || null,
        branchCode: val('na-branch') || null,
        hmdaRace: val('na-hmda-race') || null,
        hmdaEthnicity: val('na-hmda-ethnicity') || null,
        hmdaSex: val('na-hmda-sex') || null
    };

    var msgEl = document.getElementById('newapp-message');
    msgEl.innerHTML = '<div class="info-msg">Submitting application...</div>';

    api('POST', '/loans', req, function(err, loan) {
        if (err) {
            msgEl.innerHTML = '<div class="error-msg">Error: ' + escHtml(err) + '</div>';
            return;
        }
        msgEl.innerHTML = '<div class="success-msg">Application created successfully! Loan Number: ' + (loan.loanNumber || 'N/A') + '</div>';
        alert('Loan application created!\nLoan Number: ' + (loan.loanNumber || 'N/A'));
        setTimeout(function() { showLoanDetail(loan.loanNumber); }, 1500);
    });
}

// ===== SEARCH =====
function doSearch() {
    var borrower = val('search-borrower');
    var loanNum = val('search-loannum');
    var state = val('search-state');

    var url = '/loans/search?';
    var params = [];
    if (borrower) params.push('borrower=' + encodeURIComponent(borrower));
    if (loanNum) params.push('loanNumber=' + encodeURIComponent(loanNum));
    if (state) params.push('state=' + encodeURIComponent(state));
    url += params.join('&');

    document.getElementById('search-results').innerHTML = '<div class="loading">Searching...</div>';

    api('GET', url, null, function(err, data) {
        if (err || !data || data.length === 0) {
            document.getElementById('search-results').innerHTML = '<div class="info-msg">No loans found.</div>';
            return;
        }

        var html = '<table class="data-table"><thead><tr>';
        html += '<th>Loan #</th><th>Borrower</th><th>Status</th><th>Amount</th><th>State</th><th>App Date</th>';
        html += '</tr></thead><tbody>';
        data.forEach(function(loan) {
            var borrowerName = (loan.borrowerFirstName || '') + ' ' + (loan.borrowerLastName || '');
            var amount = loan.loanAmount ? '$' + Number(loan.loanAmount).toLocaleString('en-US') : '';
            html += '<tr onclick="showLoanDetail(\'' + loan.loanNumber + '\')">';
            html += '<td><a href="#" onclick="showLoanDetail(\'' + loan.loanNumber + '\'); return false;">' + loan.loanNumber + '</a></td>';
            html += '<td>' + escHtml(borrowerName) + '</td>';
            html += '<td><span class="status-' + (loan.loanStatus || '') + '">' + escHtml(loan.loanStatus || '') + '</span></td>';
            html += '<td style="text-align:right;">' + amount + '</td>';
            html += '<td>' + escHtml(loan.propertyState || '') + '</td>';
            html += '<td>' + formatDate(loan.applicationDt) + '</td>';
            html += '</tr>';
        });
        html += '</tbody></table>';
        document.getElementById('search-results').innerHTML = html;
    });
}

// ===== AGING REPORT =====
function loadAgingReport() {
    document.getElementById('aging-content').innerHTML = '<div class="loading">Loading aging report...</div>';
    api('GET', '/pipeline/aging', null, function(err, data) {
        if (err || !data) {
            document.getElementById('aging-content').innerHTML = '<div class="error-msg">Error loading aging report: ' + (err || 'no data') + '</div>';
            return;
        }

        // The endpoint returns day buckets ("0-7 Days", "8-15 Days", ...), not stages.
        var html = '<table class="data-table"><thead><tr>';
        html += '<th>Age in Pipeline</th><th style="text-align:right;">Loan Count</th>';
        html += '</tr></thead><tbody>';
        var total = 0;
        for (var key in data) {
            html += '<tr><td>' + escHtml(key) + '</td>';
            html += '<td style="text-align:right;">' + data[key] + '</td></tr>';
            total += data[key];
        }
        html += '<tr style="font-weight:bold;background-color:#d4cfc4;"><td>TOTAL</td><td style="text-align:right;">' + total + '</td></tr>';
        html += '</tbody></table>';
        document.getElementById('aging-content').innerHTML = html;
    });
}

// ===== DROPDOWN LOADING =====
function loadBranches() {
    api('GET', '/loans/branches', null, function(err, branches) {
        if (err || !branches) return;
        var sel = document.getElementById('na-branch');
        var filter = document.getElementById('filter-branch');
        branches.forEach(function(b) {
            branchNameById[b.branchId] = b.branchName;
            sel.appendChild(new Option(b.branchName + ' (' + b.branchCode + ')', b.branchCode));
            filter.appendChild(new Option(b.branchName, b.branchId));
        });
    });
}

function loadProducts() {
    api('GET', '/loans/products', null, function(err, products) {
        if (err || !products) return;
        var sel = document.getElementById('na-product');
        products.forEach(function(p) {
            productTypeById[p.productId] = p.productType;
            sel.appendChild(new Option(p.productName + ' - ' + p.productType + ' ' + p.termMonths + 'mo', p.productCode));
        });
    });
}

function loadOfficers() {
    api('GET', '/loans/officers', null, function(err, officers) {
        if (err || !officers) return;
        var sel = document.getElementById('na-loan-officer');
        officers.forEach(function(o) {
            officerNameById[o.loId] = o.fullName;
            sel.appendChild(new Option(o.fullName + ' (' + o.loCode + ')', o.loCode));
        });
        // The pipeline may have rendered before this finished; redraw so the officer
        // column shows names instead of ids.
        if (pipelineData.length > 0) renderPipeline();
    });
}

// ===== HELPERS =====
function val(id) {
    var el = document.getElementById(id);
    return el ? el.value.trim() : '';
}

function detailRow(label, value) {
    return '<tr><td class="label">' + escHtml(label) + '</td><td class="value">' + value + '</td></tr>';
}

function money(v) {
    if (v === null || v === undefined || v === '') return 'N/A';
    return '$' + Number(v).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function pct(v) {
    if (v === null || v === undefined || v === '') return 'N/A';
    return Number(v).toFixed(2) + '%';
}

function formatDate(d) {
    if (!d) return 'N/A';
    var dt = new Date(d);
    if (isNaN(dt)) return 'N/A';
    return (dt.getMonth() + 1).toString().padStart(2, '0') + '/' +
           dt.getDate().toString().padStart(2, '0') + '/' +
           dt.getFullYear();
}

function formatDateTime(d) {
    if (!d) return 'N/A';
    var dt = new Date(d);
    if (isNaN(dt)) return 'N/A';
    return formatDate(d) + ' ' +
           dt.getHours().toString().padStart(2, '0') + ':' +
           dt.getMinutes().toString().padStart(2, '0');
}

function escHtml(s) {
    if (s === null || s === undefined) return '';
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}
