using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Services;

public interface IFinanceService
{
    Task<List<InvoiceDto>> ListInvoicesAsync(int? patientId, CancellationToken cancellationToken);
    Task<InvoiceDto> CreateInvoiceAsync(InvoiceCreateRequest request, CancellationToken cancellationToken);
    Task<InvoiceDto> IssueInvoiceAsync(int id, CancellationToken cancellationToken);
    Task<InvoiceDto> CancelInvoiceAsync(int id, CancellationToken cancellationToken);
    Task<List<PaymentDto>> ListPaymentsAsync(int? patientId, CancellationToken cancellationToken);
    Task<PaymentDto> CollectPaymentAsync(PaymentCreateRequest request, CancellationToken cancellationToken);
    Task<PaymentDto> RefundAsync(int paymentId, RefundRequest request, CancellationToken cancellationToken);
    Task<List<PolicyDto>> ListPoliciesAsync(int? patientId, CancellationToken cancellationToken);
    Task<PolicyDto> CreatePolicyAsync(PolicyCreateRequest request, CancellationToken cancellationToken);
    Task<PolicyDto> CheckEligibilityAsync(int policyId, EligibilityRequest request, CancellationToken cancellationToken);
    Task<List<ClaimDto>> ListClaimsAsync(int? patientId, CancellationToken cancellationToken);
    Task<ClaimDto> CreateClaimAsync(ClaimCreateRequest request, CancellationToken cancellationToken);
    Task<ClaimDto> UpdateClaimStatusAsync(int id, string status, CancellationToken cancellationToken);
}

public class FinanceService : IFinanceService
{
    private static readonly string[] Methods = ["Cash", "Card", "UPI", "ACH"];

    private readonly AppDbContext _db;

    public FinanceService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<InvoiceDto>> ListInvoicesAsync(int? patientId, CancellationToken cancellationToken)
    {
        var query = _db.Invoices.Include(i => i.Patient).Include(i => i.Lines).AsQueryable();
        if (patientId.HasValue)
        {
            query = query.Where(i => i.PatientId == patientId);
        }

        var items = await query.OrderByDescending(i => i.IssuedAt).ToListAsync(cancellationToken);
        return items.Select(ToInvoiceDto).ToList();
    }

    public async Task<InvoiceDto> CreateInvoiceAsync(InvoiceCreateRequest request, CancellationToken cancellationToken)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken)
            ?? throw new InvalidOperationException("Patient not found.");
        var lines = (request.Lines ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Description) && l.Quantity > 0).ToList();
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Add at least one invoice line.");
        }

        var invoice = new Invoice
        {
            PatientId = patient.Id,
            AppointmentId = request.AppointmentId,
            Status = "Issued",
            IssuedAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(7),
            Notes = request.Notes ?? string.Empty,
            Lines = lines.Select(l => new InvoiceLine
            {
                Description = l.Description,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Amount = decimal.Round(l.Quantity * l.UnitPrice, 2)
            }).ToList()
        };
        invoice.Subtotal = invoice.Lines.Sum(l => l.Amount);
        invoice.Tax = Math.Max(0, request.Tax);
        invoice.Discount = Math.Max(0, request.Discount);
        invoice.Total = Math.Max(0, invoice.Subtotal + invoice.Tax - invoice.Discount);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken);
        invoice.Number = $"INV-{DateTime.UtcNow.Year}-{invoice.Id:D5}";
        await _db.SaveChangesAsync(cancellationToken);
        invoice.Patient = patient;
        return ToInvoiceDto(invoice);
    }

    public async Task<InvoiceDto> IssueInvoiceAsync(int id, CancellationToken cancellationToken)
    {
        var invoice = await LoadInvoiceAsync(id, cancellationToken);
        if (invoice.Status == "Cancelled")
        {
            throw new InvalidOperationException("Cancelled invoices cannot be reissued.");
        }

        invoice.Status = invoice.PaidAmount >= invoice.Total && invoice.Total > 0 ? "Paid" : "Issued";
        await _db.SaveChangesAsync(cancellationToken);
        return ToInvoiceDto(invoice);
    }

    public async Task<InvoiceDto> CancelInvoiceAsync(int id, CancellationToken cancellationToken)
    {
        var invoice = await LoadInvoiceAsync(id, cancellationToken);
        if (invoice.PaidAmount > 0)
        {
            throw new InvalidOperationException("Refund payments before cancelling this invoice.");
        }

        invoice.Status = "Cancelled";
        await _db.SaveChangesAsync(cancellationToken);
        return ToInvoiceDto(invoice);
    }

    public async Task<List<PaymentDto>> ListPaymentsAsync(int? patientId, CancellationToken cancellationToken)
    {
        var query = _db.Payments
            .Include(p => p.Patient)
            .Include(p => p.Invoice)
            .Include(p => p.Splits)
            .Include(p => p.Refunds)
            .AsQueryable();
        if (patientId.HasValue)
        {
            query = query.Where(p => p.PatientId == patientId);
        }

        var items = await query.OrderByDescending(p => p.PaidAt).ToListAsync(cancellationToken);
        return items.Select(ToPaymentDto).ToList();
    }

    public async Task<PaymentDto> CollectPaymentAsync(PaymentCreateRequest request, CancellationToken cancellationToken)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken)
            ?? throw new InvalidOperationException("Patient not found.");
        var splits = (request.Splits ?? [])
            .Where(s => s.Amount > 0 && Methods.Contains(s.Method, StringComparer.OrdinalIgnoreCase))
            .Select(s => new PaymentSplit { Method = CanonicalMethod(s.Method), Amount = decimal.Round(s.Amount, 2) })
            .ToList();
        if (splits.Count == 0)
        {
            throw new InvalidOperationException("Add at least one payment method and amount.");
        }

        var amount = splits.Sum(s => s.Amount);
        Invoice? invoice = null;
        if (request.InvoiceId.HasValue)
        {
            invoice = await LoadInvoiceAsync(request.InvoiceId.Value, cancellationToken);
            if (invoice.PatientId != patient.Id)
            {
                throw new InvalidOperationException("Invoice does not belong to this patient.");
            }

            if (invoice.Status == "Cancelled")
            {
                throw new InvalidOperationException("Cannot collect against a cancelled invoice.");
            }

            var balance = invoice.Total - invoice.PaidAmount;
            if (amount > balance)
            {
                throw new InvalidOperationException($"Payment exceeds invoice balance of {balance:0.00}.");
            }
        }

        var payment = new Payment
        {
            PatientId = patient.Id,
            InvoiceId = invoice?.Id,
            Amount = amount,
            Status = "Completed",
            Gateway = splits.Any(s => s.Method is "Card" or "UPI" or "ACH") ? "Demo gateway" : "Cash drawer",
            Reference = BuildGatewayReference(splits),
            Notes = request.Notes ?? string.Empty,
            PaidAt = DateTime.UtcNow,
            Splits = splits
        };
        _db.Payments.Add(payment);
        if (invoice is not null)
        {
            invoice.PaidAmount += amount;
            RefreshInvoiceStatus(invoice);
        }

        await _db.SaveChangesAsync(cancellationToken);
        payment.ReceiptNumber = $"RCT-{DateTime.UtcNow.Year}-{payment.Id:D5}";
        await _db.SaveChangesAsync(cancellationToken);
        payment.Patient = patient;
        payment.Invoice = invoice;
        return ToPaymentDto(payment);
    }

    public async Task<PaymentDto> RefundAsync(int paymentId, RefundRequest request, CancellationToken cancellationToken)
    {
        var payment = await _db.Payments
            .Include(p => p.Patient)
            .Include(p => p.Invoice)
            .Include(p => p.Splits)
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new InvalidOperationException("Payment not found.");

        var already = payment.Refunds.Sum(r => r.Amount);
        var available = payment.Amount - already;
        if (request.Amount <= 0 || request.Amount > available)
        {
            throw new InvalidOperationException($"Refund must be between 0.01 and {available:0.00}.");
        }

        var refund = new Refund
        {
            PaymentId = payment.Id,
            InvoiceId = payment.InvoiceId,
            Amount = decimal.Round(request.Amount, 2),
            Reason = request.Reason ?? string.Empty,
            Status = "Completed",
            RefundedAt = DateTime.UtcNow
        };
        _db.Refunds.Add(refund);
        if (payment.Invoice is not null)
        {
            payment.Invoice.PaidAmount = Math.Max(0, payment.Invoice.PaidAmount - refund.Amount);
            payment.Invoice.RefundedAmount += refund.Amount;
            RefreshInvoiceStatus(payment.Invoice);
        }

        if (already + refund.Amount >= payment.Amount)
        {
            payment.Status = "Refunded";
        }

        await _db.SaveChangesAsync(cancellationToken);
        payment.Refunds.Add(refund);
        return ToPaymentDto(payment);
    }

    public async Task<List<PolicyDto>> ListPoliciesAsync(int? patientId, CancellationToken cancellationToken)
    {
        var query = _db.InsurancePolicies.Include(p => p.Patient).AsQueryable();
        if (patientId.HasValue)
        {
            query = query.Where(p => p.PatientId == patientId);
        }

        var items = await query.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        return items.Select(ToPolicyDto).ToList();
    }

    public async Task<PolicyDto> CreatePolicyAsync(PolicyCreateRequest request, CancellationToken cancellationToken)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken)
            ?? throw new InvalidOperationException("Patient not found.");
        var policy = new InsurancePolicy
        {
            PatientId = patient.Id,
            Provider = request.Provider,
            PolicyNumber = request.PolicyNumber,
            Tpa = request.Tpa ?? string.Empty,
            MemberId = request.MemberId ?? string.Empty,
            ValidFrom = request.ValidFrom == default ? DateTime.UtcNow : request.ValidFrom,
            ValidTo = request.ValidTo == default ? DateTime.UtcNow.AddYears(1) : request.ValidTo,
            CoverageLimit = request.CoverageLimit,
            Status = "Active"
        };
        _db.InsurancePolicies.Add(policy);
        await _db.SaveChangesAsync(cancellationToken);
        policy.Patient = patient;
        return ToPolicyDto(policy);
    }

    public async Task<PolicyDto> CheckEligibilityAsync(int policyId, EligibilityRequest request, CancellationToken cancellationToken)
    {
        var policy = await _db.InsurancePolicies.Include(p => p.Patient).FirstOrDefaultAsync(p => p.Id == policyId, cancellationToken)
            ?? throw new InvalidOperationException("Policy not found.");
        var status = request.Status?.Contains("not", StringComparison.OrdinalIgnoreCase) == true ? "Not eligible" : "Eligible";
        if (policy.ValidTo.Date < DateTime.UtcNow.Date)
        {
            status = "Not eligible";
            policy.Status = "Expired";
        }

        policy.Eligibility = status;
        policy.EligibilityNotes = string.IsNullOrWhiteSpace(request.Notes)
            ? $"Demo eligibility check against {policy.Provider} / {policy.Tpa}."
            : request.Notes;
        policy.EligibilityCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToPolicyDto(policy);
    }

    public async Task<List<ClaimDto>> ListClaimsAsync(int? patientId, CancellationToken cancellationToken)
    {
        var query = _db.InsuranceClaims.Include(c => c.Policy).Include(c => c.Patient).Include(c => c.Invoice).AsQueryable();
        if (patientId.HasValue)
        {
            query = query.Where(c => c.PatientId == patientId);
        }

        var items = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        return items.Select(ToClaimDto).ToList();
    }

    public async Task<ClaimDto> CreateClaimAsync(ClaimCreateRequest request, CancellationToken cancellationToken)
    {
        var policy = await _db.InsurancePolicies.Include(p => p.Patient).FirstOrDefaultAsync(p => p.Id == request.PolicyId, cancellationToken)
            ?? throw new InvalidOperationException("Policy not found.");
        Invoice? invoice = null;
        if (request.InvoiceId.HasValue)
        {
            invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.PatientId == policy.PatientId, cancellationToken);
        }

        var claim = new InsuranceClaim
        {
            PolicyId = policy.Id,
            PatientId = policy.PatientId,
            InvoiceId = invoice?.Id,
            Amount = request.Amount,
            Status = "Submitted",
            Notes = request.Notes ?? string.Empty,
            SubmittedAt = DateTime.UtcNow
        };
        _db.InsuranceClaims.Add(claim);
        await _db.SaveChangesAsync(cancellationToken);
        claim.ClaimNumber = $"CLM-{DateTime.UtcNow.Year}-{claim.Id:D5}";
        await _db.SaveChangesAsync(cancellationToken);
        claim.Policy = policy;
        claim.Patient = policy.Patient;
        claim.Invoice = invoice;
        return ToClaimDto(claim);
    }

    public async Task<ClaimDto> UpdateClaimStatusAsync(int id, string status, CancellationToken cancellationToken)
    {
        var claim = await _db.InsuranceClaims.Include(c => c.Policy).Include(c => c.Patient).Include(c => c.Invoice)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Claim not found.");
        claim.Status = NormalizeClaimStatus(status);
        if (claim.Status == "Submitted" && claim.SubmittedAt is null)
        {
            claim.SubmittedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToClaimDto(claim);
    }

    private async Task<Invoice> LoadInvoiceAsync(int id, CancellationToken cancellationToken) =>
        await _db.Invoices.Include(i => i.Patient).Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

    private static void RefreshInvoiceStatus(Invoice invoice)
    {
        if (invoice.Status == "Cancelled")
        {
            return;
        }

        if (invoice.PaidAmount <= 0 && invoice.RefundedAmount > 0)
        {
            invoice.Status = "Refunded";
            return;
        }

        if (invoice.PaidAmount >= invoice.Total && invoice.Total > 0)
        {
            invoice.Status = "Paid";
            return;
        }

        invoice.Status = invoice.PaidAmount > 0 ? "Partial" : "Issued";
    }

    private static string CanonicalMethod(string method) =>
        Methods.First(m => m.Equals(method, StringComparison.OrdinalIgnoreCase));

    private static string BuildGatewayReference(List<PaymentSplit> splits)
    {
        var digital = splits.Where(s => s.Method is "Card" or "UPI" or "ACH").ToList();
        if (digital.Count == 0)
        {
            return $"CASH-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var prefix = digital[0].Method switch
        {
            "UPI" => "UPI",
            "ACH" => "ACH",
            _ => "CARD"
        };
        return $"{prefix}-DEMO-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    private static string NormalizeClaimStatus(string status)
    {
        var value = status.Trim().ToLowerInvariant();
        return value switch
        {
            "under review" or "review" => "Under review",
            "approved" => "Approved",
            "rejected" => "Rejected",
            "paid" => "Paid",
            "draft" => "Draft",
            _ => "Submitted"
        };
    }

    private static InvoiceDto ToInvoiceDto(Invoice invoice) =>
        new(
            invoice.Id,
            invoice.Number,
            invoice.PatientId,
            invoice.Patient?.Name ?? "",
            invoice.Patient?.Uhid ?? "",
            invoice.AppointmentId,
            invoice.Status,
            invoice.IssuedAt,
            invoice.DueAt,
            invoice.Notes,
            invoice.Subtotal,
            invoice.Tax,
            invoice.Discount,
            invoice.Total,
            invoice.PaidAmount,
            invoice.RefundedAmount,
            Math.Max(0, invoice.Total - invoice.PaidAmount),
            invoice.Lines.Select(l => new InvoiceLineDto(l.Id, l.Description, l.Quantity, l.UnitPrice, l.Amount)).ToList());

    private static PaymentDto ToPaymentDto(Payment payment) =>
        new(
            payment.Id,
            payment.ReceiptNumber,
            payment.PatientId,
            payment.Patient?.Name ?? "",
            payment.InvoiceId,
            payment.Invoice?.Number,
            payment.Amount,
            payment.Status,
            payment.Gateway,
            payment.Reference,
            payment.Notes,
            payment.PaidAt,
            payment.Refunds.Sum(r => r.Amount),
            payment.Splits.Select(s => new PaymentSplitDto(s.Method, s.Amount)).ToList(),
            payment.Refunds.Select(r => new RefundDto(r.Id, r.Amount, r.Reason, r.Status, r.RefundedAt)).ToList());

    private static PolicyDto ToPolicyDto(InsurancePolicy policy) =>
        new(
            policy.Id,
            policy.PatientId,
            policy.Patient?.Name ?? "",
            policy.Provider,
            policy.PolicyNumber,
            policy.Tpa,
            policy.MemberId,
            policy.ValidFrom,
            policy.ValidTo,
            policy.CoverageLimit,
            policy.Status,
            policy.Eligibility,
            policy.EligibilityNotes,
            policy.EligibilityCheckedAt);

    private static ClaimDto ToClaimDto(InsuranceClaim claim) =>
        new(
            claim.Id,
            claim.ClaimNumber,
            claim.PolicyId,
            claim.Policy?.Provider ?? "",
            claim.Policy?.PolicyNumber ?? "",
            claim.PatientId,
            claim.Patient?.Name ?? "",
            claim.InvoiceId,
            claim.Invoice?.Number,
            claim.Amount,
            claim.Status,
            claim.Notes,
            claim.CreatedAt,
            claim.SubmittedAt);
}
