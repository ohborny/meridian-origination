using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using MortgageLOS;
using MortgageLOS.Origination;

namespace MortgageLOS.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoansController : ControllerBase
    {
        private readonly LoanApplicationService _loanService;
        private readonly LoanProductService _productService;
        private readonly LoanOfficerService _officerService;

        public LoansController(LoanApplicationService loanService, LoanProductService productService, LoanOfficerService officerService)
        {
            _loanService = loanService;
            _productService = productService;
            _officerService = officerService;
        }

        [HttpPost]
        public IActionResult CreateLoan([FromBody] LoanApplicationRequest request, [FromQuery] string createdBy = "web_user")
        {
            try
            {
                LoanData loan = _loanService.CreateLoanApplication(request, createdBy);
                return CreatedAtAction(nameof(GetLoan), new { loanNumber = loan.LoanNumber }, loan);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Web", "CreateLoan failed", ex);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{loanNumber}")]
        public IActionResult GetLoan(string loanNumber)
        {
            LoanData loan = _loanService.GetLoan(loanNumber);
            if (loan == null)
                return NotFound(new { error = "Loan not found: " + loanNumber });
            return Ok(loan);
        }

        [HttpGet]
        public IActionResult ListLoans([FromQuery] string status, [FromQuery] string branch, [FromQuery] string lo, [FromQuery] string from, [FromQuery] string to)
        {
            DateTime? fromDate = null;
            DateTime? toDate = null;
            if (!string.IsNullOrEmpty(from)) fromDate = DateTime.Parse(from);
            if (!string.IsNullOrEmpty(to)) toDate = DateTime.Parse(to);

            List<LoanData> loans = _loanService.ListLoans(status, branch, lo, fromDate, toDate);
            return Ok(loans);
        }

        [HttpPut("{loanNumber}/status")]
        public IActionResult UpdateStatus(string loanNumber, [FromBody] StatusUpdateRequest request)
        {
            bool success = _loanService.UpdateLoanStatus(loanNumber, request.Status, request.ChangedBy, request.Reason);
            if (!success)
                return NotFound(new { error = "Loan not found or update failed" });
            return Ok(new { success = true });
        }

        [HttpPut("{loanNumber}/officer")]
        public IActionResult AssignOfficer(string loanNumber, [FromBody] AssignOfficerRequest request)
        {
            bool success = _loanService.AssignLoanOfficer(loanNumber, request.LoCode, request.AssignedBy);
            if (!success)
                return BadRequest(new { error = "Loan or officer not found" });
            return Ok(new { success = true });
        }

        [HttpGet("search")]
        public IActionResult Search([FromQuery] string borrower, [FromQuery] string loanNumber, [FromQuery] string state)
        {
            List<LoanData> loans = _loanService.SearchLoans(borrower, loanNumber, state);
            return Ok(loans);
        }

        [HttpGet("products")]
        public IActionResult GetProducts()
        {
            return Ok(_productService.ListActiveProducts());
        }

        [HttpGet("officers")]
        public IActionResult GetOfficers([FromQuery] int? branchId)
        {
            return Ok(_officerService.ListLoanOfficers(branchId));
        }

        [HttpGet("branches")]
        public IActionResult GetBranches()
        {
            return Ok(_officerService.ListBranches());
        }
    }

    public class StatusUpdateRequest
    {
        public string Status { get; set; }
        public string ChangedBy { get; set; }
        public string Reason { get; set; }
    }

    public class AssignOfficerRequest
    {
        public string LoCode { get; set; }
        public string AssignedBy { get; set; }
    }
}
