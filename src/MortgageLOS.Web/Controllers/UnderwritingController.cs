using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using MortgageLOS;
using MortgageLOS.Underwriting;

namespace MortgageLOS.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UnderwritingController : ControllerBase
    {
        private readonly AusService _ausService;
        private readonly UnderwritingService _uwService;
        private readonly UnderwritingWorkflowService _workflowService;

        public UnderwritingController(AusService ausService, UnderwritingService uwService, UnderwritingWorkflowService workflowService)
        {
            _ausService = ausService;
            _uwService = uwService;
            _workflowService = workflowService;
        }

        [HttpPost("aus/{loanNumber}")]
        public IActionResult RunAus(string loanNumber, [FromQuery] string engine = null)
        {
            try
            {
                string ausEngine = engine ?? "DU";
                AusResultData result = _ausService.RunAus(loanNumber, ausEngine);
                return Ok(result);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Web", "RunAus failed for " + loanNumber, ex);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("aus/{loanNumber}")]
        public IActionResult GetAusResult(string loanNumber)
        {
            AusResultData result = _ausService.GetAusResult(loanNumber);
            if (result == null)
                return NotFound(new { error = "No AUS result found" });
            return Ok(result);
        }

        [HttpPost("submit/{loanNumber}")]
        public IActionResult SubmitForUnderwriting(string loanNumber, [FromQuery] string underwriterId)
        {
            bool success = _uwService.SubmitForUnderwriting(loanNumber, underwriterId);
            if (!success)
                return BadRequest(new { error = "Failed to submit for underwriting" });
            return Ok(new { success = true });
        }

        [HttpPost("decision/{loanNumber}")]
        public IActionResult MakeDecision(string loanNumber, [FromBody] DecisionRequest request)
        {
            bool success = _uwService.MakeDecision(loanNumber, request.Decision, request.UnderwriterId, request.Notes);
            if (!success)
                return BadRequest(new { error = "Failed to record decision" });
            return Ok(new { success = true });
        }

        [HttpPost("process/{loanNumber}")]
        public IActionResult ProcessForUnderwriting(string loanNumber, [FromQuery] string underwriterId)
        {
            try
            {
                UnderwritingResult result = _workflowService.ProcessLoanForUnderwriting(loanNumber, underwriterId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("ctc/{loanNumber}")]
        public IActionResult ClearToClose(string loanNumber, [FromQuery] string clearedBy)
        {
            bool success = _workflowService.ClearToClose(loanNumber, clearedBy);
            if (!success)
                return BadRequest(new { error = "Loan not ready for clear to close" });
            return Ok(new { success = true, message = "Loan cleared to close" });
        }

        [HttpGet("{loanNumber}/conditions")]
        public IActionResult GetConditions(string loanNumber)
        {
            return Ok(_uwService.GetConditions(loanNumber));
        }

        [HttpGet("{loanNumber}/conditions/outstanding")]
        public IActionResult GetOutstandingConditions(string loanNumber)
        {
            return Ok(_uwService.GetOutstandingConditions(loanNumber));
        }

        [HttpPut("conditions/{conditionId}/satisfy")]
        public IActionResult SatisfyCondition(int conditionId, [FromQuery] string satisfiedBy)
        {
            bool success = _uwService.SatisfyCondition(conditionId, satisfiedBy);
            if (!success)
                return BadRequest(new { error = "Failed to satisfy condition" });
            return Ok(new { success = true });
        }

        [HttpGet("{loanNumber}/summary")]
        public IActionResult GetSummary(string loanNumber)
        {
            return Ok(_workflowService.GetUnderwritingSummary(loanNumber));
        }
    }

    public class DecisionRequest
    {
        public string Decision { get; set; }
        public string UnderwriterId { get; set; }
        public string Notes { get; set; }
    }
}
