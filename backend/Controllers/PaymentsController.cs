using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IFinanceService _finance;
    private readonly ICurrentUserService _current;

    public PaymentsController(IFinanceService finance, ICurrentUserService current)
    {
        _finance = finance;
        _current = current;
    }

    [HttpGet]
    public async Task<ActionResult<List<PaymentDto>>> List([FromQuery] int? patientId, CancellationToken cancellationToken)
    {
        return await _finance.ListPaymentsAsync(await ScopePatientAsync(patientId, cancellationToken), cancellationToken);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<PaymentDto>> Collect(PaymentCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.CollectPaymentAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/refund")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<PaymentDto>> Refund(int id, RefundRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.RefundAsync(id, request, cancellationToken);
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
