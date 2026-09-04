namespace AiVoicePortal.Api.Models;

public class Payment
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public int? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Completed";
    public string Gateway { get; set; } = "Demo";
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;

    public Patient Patient { get; set; } = null!;
    public Invoice? Invoice { get; set; }
    public ICollection<PaymentSplit> Splits { get; set; } = new List<PaymentSplit>();
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

public class PaymentSplit
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public string Method { get; set; } = "Cash";
    public decimal Amount { get; set; }

    public Payment Payment { get; set; } = null!;
}

public class Refund
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public int? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Completed";
    public DateTime RefundedAt { get; set; } = DateTime.UtcNow;

    public Payment Payment { get; set; } = null!;
    public Invoice? Invoice { get; set; }
}
