using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/insurance")]
[Authorize]
public class InsuranceController : ControllerBase
{
    private readonly IFinanceService _finance;
    private readonly ICurrentUserService _current;

    public InsuranceController(IFinanceService finance, ICurrentUserService current)
    {
        _finance = finance;
        _current = current;
    }

    [HttpGet("policies")]
    public async Task<ActionResult<List<PolicyDto>>> Policies([FromQuery] int? patientId, CancellationToken cancellationToken)
    {
        return await _finance.ListPoliciesAsync(await ScopePatientAsync(patientId, cancellationToken), cancellationToken);
    }

    [HttpPost("policies")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<PolicyDto>> CreatePolicy(PolicyCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.CreatePolicyAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("policies/{id:int}/eligibility")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<PolicyDto>> Eligibility(int id, EligibilityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.CheckEligibilityAsync(id, request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("claims")]
    public async Task<ActionResult<List<ClaimDto>>> Claims([FromQuery] int? patientId, CancellationToken cancellationToken)
    {
        return await _finance.ListClaimsAsync(await ScopePatientAsync(patientId, cancellationToken), cancellationToken);
    }

    [HttpPost("claims")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<ClaimDto>> CreateClaim(ClaimCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.CreateClaimAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("claims/{id:int}")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<ActionResult<ClaimDto>> UpdateClaim(int id, ClaimStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _finance.UpdateClaimStatusAsync(id, request.Status, cancellationToken);
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
