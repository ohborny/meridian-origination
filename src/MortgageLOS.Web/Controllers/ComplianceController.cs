using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using MortgageLOS;
using MortgageLOS.Compliance;

namespace MortgageLOS.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ComplianceController : ControllerBase
    {
        private readonly ComplianceCheckService _checkService;
        private readonly HmdaService _hmdaService;
        private readonly TridService _tridService;
        private readonly DisclosureService _disclosureService;

        public ComplianceController(ComplianceCheckService checkService, HmdaService hmdaService,
            TridService tridService, DisclosureService disclosureService)
        {
            _checkService = checkService;
            _hmdaService = hmdaService;
            _tridService = tridService;
            _disclosureService = disclosureService;
        }

        [HttpPost("check/{loanNumber}")]
        public IActionResult RunComplianceChecks(string loanNumber)
        {
            try
            {
                List<ComplianceCheckData> results = _checkService.RunComplianceChecks(loanNumber);
                return Ok(results);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Web", "RunComplianceChecks failed for " + loanNumber, ex);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{loanNumber}")]
        public IActionResult GetComplianceChecks(string loanNumber)
        {
            return Ok(_checkService.GetComplianceChecks(loanNumber));
        }

        [HttpGet("{loanNumber}/status")]
        public IActionResult GetComplianceStatus(string loanNumber)
        {
            string status = _checkService.GetComplianceStatus(loanNumber);
            return Ok(new { status = status });
        }

        [HttpPost("hmda/{loanNumber}")]
        public IActionResult CreateHmdaRecord(string loanNumber, [FromBody] HmdaRequest request)
        {
            try
            {
                int hmdaId = _hmdaService.CreateHmdaRecord(loanNumber, request.Race, request.Ethnicity,
                    request.Sex, request.RaceObserved, request.SexObserved, request.IncomeAmount,
                    request.CollectionMethod, "web_user");
                return Ok(new { hmdaId = hmdaId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("hmda/{loanNumber}")]
        public IActionResult GetHmdaData(string loanNumber)
        {
            HmdaData data = _hmdaService.GetHmdaData(loanNumber);
            if (data == null)
                return NotFound(new { error = "No HMDA data found" });
            return Ok(data);
        }

        [HttpPost("trid/{loanNumber}/init")]
        public IActionResult InitTridTimeline(string loanNumber, [FromBody] TridInitRequest request)
        {
            int tridId = _tridService.InitializeTridTimeline(loanNumber, request.ApplicationDate);
            return Ok(new { tridId = tridId });
        }

        [HttpGet("trid/{loanNumber}")]
        public IActionResult GetTridTimeline(string loanNumber)
        {
            TridTimelineData timeline = _tridService.GetTridTimeline(loanNumber);
            if (timeline == null)
                return NotFound(new { error = "No TRID timeline found" });
            return Ok(timeline);
        }

        [HttpPost("trid/{loanNumber}/le-sent")]
        public IActionResult RecordLeSent(string loanNumber, [FromBody] LeSentRequest request)
        {
            _tridService.RecordLeSent(loanNumber, request.SentDate, request.Version ?? "1");
            return Ok(new { success = true });
        }

        [HttpPost("trid/{loanNumber}/cd-sent")]
        public IActionResult RecordCdSent(string loanNumber, [FromBody] CdSentRequest request)
        {
            _tridService.RecordCdSent(loanNumber, request.SentDate);
            return Ok(new { success = true });
        }

        [HttpGet("disclosures/{loanNumber}")]
        public IActionResult GetDisclosures(string loanNumber)
        {
            return Ok(_disclosureService.GetDisclosures(loanNumber));
        }

        [HttpGet("disclosures/{loanNumber}/pending")]
        public IActionResult GetPendingDisclosures(string loanNumber)
        {
            return Ok(_disclosureService.GetPendingDisclosures(loanNumber));
        }

        [HttpGet("state-rules/{stateCode}")]
        public IActionResult GetStateRules(string stateCode)
        {
            return Ok(_disclosureService.GetStateDisclosureRules(stateCode));
        }
    }

    public class HmdaRequest
    {
        public string Race { get; set; }
        public string Ethnicity { get; set; }
        public string Sex { get; set; }
        public string RaceObserved { get; set; }
        public string SexObserved { get; set; }
        public int? IncomeAmount { get; set; }
        public string CollectionMethod { get; set; }
    }

    public class TridInitRequest
    {
        public DateTime ApplicationDate { get; set; }
    }

    public class LeSentRequest
    {
        public DateTime SentDate { get; set; }
        public string Version { get; set; }
    }

    public class CdSentRequest
    {
        public DateTime SentDate { get; set; }
    }
}
