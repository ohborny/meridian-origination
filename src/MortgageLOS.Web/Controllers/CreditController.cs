using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using MortgageLOS;
using MortgageLOS.Credit;

namespace MortgageLOS.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CreditController : ControllerBase
    {
        private readonly CreditReportService _creditService;
        private readonly DtiCalculator _dtiCalculator;

        public CreditController(CreditReportService creditService, DtiCalculator dtiCalculator)
        {
            _creditService = creditService;
            _dtiCalculator = dtiCalculator;
        }

        [HttpPost("pull/{loanNumber}")]
        public IActionResult PullCredit(string loanNumber, [FromQuery] string requestedBy = "web_user")
        {
            try
            {
                CreditReportData report = _creditService.ProcessCreditPull(loanNumber);
                if (report == null)
                    return StatusCode(500, new { error = "Credit pull failed" });
                return Ok(report);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Web", "PullCredit failed for " + loanNumber, ex);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{loanNumber}")]
        public IActionResult GetCreditReport(string loanNumber)
        {
            CreditReportData report = _creditService.GetCreditReport(loanNumber);
            if (report == null)
                return NotFound(new { error = "No credit report found for " + loanNumber });
            return Ok(report);
        }

        [HttpGet("{loanNumber}/liabilities")]
        public IActionResult GetLiabilities(string loanNumber)
        {
            return Ok(_creditService.GetCreditLiabilities(loanNumber));
        }

        [HttpPost("dti/{loanNumber}")]
        public IActionResult CalculateDti(string loanNumber, [FromBody] DtiRequest request)
        {
            try
            {
                DtiResultData result = _dtiCalculator.CalculateDti(loanNumber, request.MonthlyIncome, request.CoborrowerIncome, request.ProposedHousingPayment, request.LoanType);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("dti/{loanNumber}")]
        public IActionResult GetDti(string loanNumber)
        {
            DtiResultData result = _dtiCalculator.GetDtiResult(loanNumber);
            if (result == null)
                return NotFound(new { error = "No DTI calculation found" });
            return Ok(result);
        }

        [HttpGet("{loanNumber}/summary")]
        public IActionResult GetCreditSummary(string loanNumber)
        {
            CreditAnalysisService analysis = new CreditAnalysisService();
            return Ok(analysis.GetCreditSummary(loanNumber));
        }
    }

    public class DtiRequest
    {
        public decimal MonthlyIncome { get; set; }
        public decimal CoborrowerIncome { get; set; }
        public decimal ProposedHousingPayment { get; set; }
        public string LoanType { get; set; }
    }
}
