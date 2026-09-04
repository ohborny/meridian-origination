using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using MortgageLOS;
using MortgageLOS.Documents;

namespace MortgageLOS.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private readonly DocumentService _docService;
        private readonly DocumentChecklistService _checklistService;

        public DocumentsController(DocumentService docService, DocumentChecklistService checklistService)
        {
            _docService = docService;
            _checklistService = checklistService;
        }

        [HttpGet("{loanNumber}")]
        public IActionResult GetDocuments(string loanNumber)
        {
            return Ok(_docService.GetDocuments(loanNumber));
        }

        [HttpPost("{loanNumber}")]
        public IActionResult AddDocument(string loanNumber, [FromBody] AddDocumentRequest request)
        {
            try
            {
                DocumentData doc = _docService.AddDocument(loanNumber, request.DocumentType, request.DocumentName, request.FilePath, request.UploadedBy);
                return Ok(doc);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("{documentId}/verify")]
        public IActionResult VerifyDocument(int documentId, [FromQuery] string verifiedBy)
        {
            bool success = _docService.VerifyDocument(documentId, verifiedBy);
            if (!success)
                return BadRequest(new { error = "Failed to verify document" });
            return Ok(new { success = true });
        }

        [HttpPut("{documentId}/reject")]
        public IActionResult RejectDocument(int documentId, [FromBody] RejectDocumentRequest request)
        {
            bool success = _docService.RejectDocument(documentId, request.Reason, request.RejectedBy);
            if (!success)
                return BadRequest(new { error = "Failed to reject document" });
            return Ok(new { success = true });
        }

        [HttpPost("{loanNumber}/request")]
        public IActionResult RequestDocument(string loanNumber, [FromBody] RequestDocumentRequest request)
        {
            DocumentData doc = _docService.RequestDocument(loanNumber, request.DocumentType, request.RequestedBy);
            return Ok(doc);
        }

        [HttpGet("checklist/{loanNumber}")]
        public IActionResult GetChecklist(string loanNumber, [FromQuery] string loanType, [FromQuery] string loanPurpose)
        {
            DocumentChecklistResult checklist = _checklistService.GetDocumentChecklist(loanNumber, loanType, loanPurpose);
            return Ok(checklist);
        }

        [HttpGet("checklist/{loanNumber}/missing")]
        public IActionResult GetMissingDocuments(string loanNumber, [FromQuery] string loanType, [FromQuery] string loanPurpose)
        {
            return Ok(_checklistService.GetMissingDocuments(loanNumber, loanType, loanPurpose));
        }
    }

    public class AddDocumentRequest
    {
        public string DocumentType { get; set; }
        public string DocumentName { get; set; }
        public string FilePath { get; set; }
        public string UploadedBy { get; set; }
    }

    public class RejectDocumentRequest
    {
        public string Reason { get; set; }
        public string RejectedBy { get; set; }
    }

    public class RequestDocumentRequest
    {
        public string DocumentType { get; set; }
        public string RequestedBy { get; set; }
    }
}
