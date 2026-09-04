using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly IFinanceService _finance;
    private readonly ICurrentUserService _current;

    public BillingController(IFinanceService finance, ICurrentUserService current)
    {
        _finance = finance;
        _current = current;
    }

    [HttpGet("invoices")]
    public async Task<ActionResult<List<InvoiceDto>>> Invoices([FromQuery] int? patientId, CancellationToken cancellationToken)
    {
        return await _finance.ListInvoicesAsync(await ScopePatientAsync(patientId, cancellationToken), cancellationToken);
    }

    [HttpPost("invoices")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<InvoiceDto>> Create(InvoiceCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.CreateInvoiceAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("invoices/{id:int}/cancel")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<InvoiceDto>> Cancel(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.CancelInvoiceAsync(id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<int?> ScopePatientAsync(int? patientId, CancellationToken cancellationToken)
    {
        if (!_current.IsPatient)
        {
            return patientId;
        }

        return await _current.GetPatientIdAsync(cancellationToken);
    }
}
