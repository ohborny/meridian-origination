using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using MortgageLOS;
using MortgageLOS.Origination;

namespace MortgageLOS.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PipelineController : ControllerBase
    {
        private readonly PipelineService _pipelineService;

        public PipelineController(PipelineService pipelineService)
        {
            _pipelineService = pipelineService;
        }

        [HttpGet]
        public IActionResult GetPipeline()
        {
            List<LoanData> loans = _pipelineService.GetPipelineLoans();
            return Ok(loans);
        }

        [HttpGet("stage/{stage}")]
        public IActionResult GetByStage(string stage)
        {
            List<LoanData> loans = _pipelineService.GetLoansByStage(stage);
            return Ok(loans);
        }

        [HttpGet("aging")]
        public IActionResult GetAgingReport()
        {
            Dictionary<string, int> report = _pipelineService.GetAgingReport();
            return Ok(report);
        }

        [HttpPost("advance/{loanNumber}")]
        public IActionResult AdvanceStage(string loanNumber, [FromQuery] string changedBy)
        {
            bool success = _pipelineService.AdvanceLoanStage(loanNumber, changedBy);
            if (!success)
                return BadRequest(new { error = "Cannot advance loan stage" });
            return Ok(new { success = true });
        }
    }
}
