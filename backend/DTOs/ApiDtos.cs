using System.ComponentModel.DataAnnotations;

namespace AiVoicePortal.Api.DTOs;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record RegisterRequest(
    [Required] string FullName,
    [Required, EmailAddress] string Email,
    [Required] string Password,
    [Range(0, 120)] int Age,
    [Required] string Contact,
    string Address);

public record ForgotPasswordRequest([Required, EmailAddress] string Email);

public record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required] string Token,
    [Required] string NewPassword);

public record AuthResponse(string Token, string Email, string FullName, string Role, DateTime ExpiresAt);

public record PatientRequest(
    [Required] string Name,
    [Range(0, 120)] int Age,
    [Required] string Contact,
    string Email,
    string Address,
    string Notes,
    string Gender,
    string BloodGroup,
    string EmergencyName,
    string EmergencyPhone,
    string Allergies);

public record PatientDto(
    int Id,
    string Name,
    int Age,
    string Contact,
    string Email,
    string Address,
    string Notes,
    DateTime CreatedAt,
    int VisitCount = 0,
    int DocumentCount = 0,
    string Uhid = "",
    string Gender = "",
    string BloodGroup = "",
    string EmergencyName = "",
    string EmergencyPhone = "",
    string Allergies = "");

public record PatientDocumentDto(int Id, int PatientId, int? AppointmentId, string Category, string OriginalName, string ContentType, long SizeBytes, DateTime UploadedAt);

public record DocumentCategoryRequest(string? Category, int? AppointmentId);

public record PatientChargeRequest(
    [Required] string Kind,
    [Required] string Title,
    [Range(0, 10000000)] decimal Amount,
    string Notes,
    DateTime? ChargeDate,
    int? AppointmentId);

public record PatientChargeDto(int Id, int PatientId, int? AppointmentId, string Kind, string Title, decimal Amount, string Notes, DateTime ChargeDate);

public static class DocumentCategories
{
    public static readonly string[] All =
    [
        "Aadhaar",
        "Reports",
        "Prescriptions",
        "Insurance",
        "Billing",
        "Daily bills",
        "Discharge",
        "Consent",
        "Other"
    ];

    public static bool TryCanonical(string? value, out string category)
    {
        category = All.FirstOrDefault(item => item.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return category.Length > 0;
    }
}

public record PatientDetailDto(PatientDto Patient, List<AppointmentDto> Visits, List<PatientDocumentDto> Documents, List<PatientChargeDto> Charges);

public record ScheduleDto(int Id, DayOfWeek DayOfWeek, string StartTime, string EndTime);

public record ScheduleRequest(DayOfWeek DayOfWeek, string StartTime, string EndTime);

public record DoctorRequest(
    [Required] string Name,
    [Required, EmailAddress] string Email,
    string Specialization,
    string Phone,
    bool IsActive,
    string? Password,
    List<ScheduleRequest>? Schedules);

public record DoctorDto(
    int Id,
    string Name,
    string Email,
    string Specialization,
    string Phone,
    bool IsActive,
    bool HasLogin,
    List<ScheduleDto> Schedules);

public record BookAppointmentRequest(
    [Required] int PatientId,
    [Required] int DoctorId,
    [Required] DateTime ScheduledAt,
    string Notes);

public record AppointmentPageDto(List<AppointmentDto> Items, int Total, int Page, int PageSize);

public record AppointmentDto(
    int Id,
    int PatientId,
    string PatientName,
    int PatientAge,
    string PatientContact,
    int DoctorId,
    string DoctorName,
    string DoctorEmail,
    DateTime ScheduledAt,
    string Status,
    string Notes,
    DateTime CreatedAt);

public record CallLogDto(
    int Id,
    string CallerName,
    string CallerPhone,
    string Summary,
    string ActionTaken,
    string Intent,
    string Transcript,
    DateTime Timestamp,
    string Outcome = "Contained",
    string EscalationReason = "",
    string TransferType = "None",
    double Confidence = 1,
    string Sentiment = "Neutral",
    bool ConsentGiven = true);

public record SimulateCallRequest(
    string CallerName,
    string CallerPhone,
    string SpokenText,
    int? PreferredDoctorId,
    DateTime? PreferredTime);

public record VoiceSessionStartRequest(string CallerName, string CallerPhone);

public record VoiceSessionTurnRequest(string SpokenText);

public record VoiceChatTurnDto(string Role, string Text, string Intent);

public record VoiceSessionDto(
    string SessionId,
    string AgentName,
    string WelcomeMessage,
    string ReplyText,
    string Intent,
    string Summary,
    string ActionTaken,
    bool Ended,
    CallLogDto? CallLog,
    AppointmentDto? Appointment,
    string? AudioBase64,
    List<VoiceChatTurnDto> Transcript,
    string Phase = "Greeting",
    double Confidence = 1,
    string Outcome = "",
    string EscalationReason = "",
    string TransferType = "None",
    string? TransferNumber = null,
    bool AfterHours = false,
    bool ConsentGiven = false,
    int ClarifyingQuestions = 0,
    bool IsVip = false);

public record VoiceStatusDto(
    bool SarvamConfigured,
    string AgentName,
    string WelcomeMessage,
    string IncomingWebhook,
    string GatherWebhook,
    string[] Languages,
    string ExotelIncomingWebhook,
    string ExotelTurnWebhook,
    string ExotelAudioWebhook,
    bool ExotelConfigured,
    string PublicBaseUrl = "",
    bool PhoneReady = false);

public record VoiceTurnResponse(
    string AgentName,
    string WelcomeMessage,
    string ReplyText,
    string Intent,
    string Summary,
    string ActionTaken,
    CallLogDto CallLog,
    AppointmentDto? Appointment,
    string? AudioBase64);

public record AiSettingsDto(
    int Id,
    string AgentName,
    string WelcomeMessage,
    string Language,
    string Instructions,
    string VoiceSpeaker,
    string ConsentMessage = "",
    string TransferNumber = "");

public record DashboardStatsDto(
    DateTime FromDate,
    DateTime ToDate,
    int CallsToday,
    int AppointmentsToday,
    int UpcomingAppointments,
    int ActiveDoctors,
    int BookedAppointments,
    int PendingAppointments,
    int CompletedAppointments,
    int CancelledAppointments,
    List<DailyCountDto> CallVolume,
    List<DailyCountDto> AppointmentStats,
    List<AppointmentStatusDayDto> AppointmentStatusByDay,
    List<CallLogDto> ActionItems,
    int ActionItemTotal,
    int ActionItemPage,
    int ActionItemPageSize,
    List<EmailMessageDto> RecentEmails,
    double ContainmentRate = 0,
    int EscalatedCalls = 0,
    int CallbackQueued = 0);

public record DailyCountDto(string Label, int Count);

public record AppointmentStatusDayDto(string Label, int Booked, int Pending, int Completed, int Cancelled);

public record SystemStatusDto(
    string Provider,
    string Host,
    bool Connected,
    int Patients,
    int Doctors,
    int Appointments,
    int CallLogs,
    int Emails);

public record EmailMessageDto(int Id, string Recipient, string Subject, string Body, DateTime SentAt, string Delivery);

public record InvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice);

public record InvoiceCreateRequest(
    [Required] int PatientId,
    int? AppointmentId,
    string Notes,
    decimal Tax,
    decimal Discount,
    List<InvoiceLineRequest> Lines);

public record InvoiceLineDto(int Id, string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

public record InvoiceDto(
    int Id,
    string Number,
    int PatientId,
    string PatientName,
    string PatientUhid,
    int? AppointmentId,
    string Status,
    DateTime IssuedAt,
    DateTime? DueAt,
    string Notes,
    decimal Subtotal,
    decimal Tax,
    decimal Discount,
    decimal Total,
    decimal PaidAmount,
    decimal RefundedAmount,
    decimal Balance,
    List<InvoiceLineDto> Lines);

public record PaymentSplitRequest(string Method, decimal Amount);

public record PaymentCreateRequest(
    [Required] int PatientId,
    int? InvoiceId,
    string Notes,
    List<PaymentSplitRequest> Splits);

public record PaymentSplitDto(string Method, decimal Amount);

public record RefundDto(int Id, decimal Amount, string Reason, string Status, DateTime RefundedAt);

public record PaymentDto(
    int Id,
    string ReceiptNumber,
    int PatientId,
    string PatientName,
    int? InvoiceId,
    string? InvoiceNumber,
    decimal Amount,
    string Status,
    string Gateway,
    string Reference,
    string Notes,
    DateTime PaidAt,
    decimal RefundedAmount,
    List<PaymentSplitDto> Splits,
    List<RefundDto> Refunds);

public record RefundRequest([Range(0.01, 10000000)] decimal Amount, string Reason);

public record PolicyCreateRequest(
    [Required] int PatientId,
    [Required] string Provider,
    [Required] string PolicyNumber,
    string Tpa,
    string MemberId,
    DateTime ValidFrom,
    DateTime ValidTo,
    decimal CoverageLimit);

public record EligibilityRequest(string Status, string Notes);

public record PolicyDto(
    int Id,
    int PatientId,
    string PatientName,
    string Provider,
    string PolicyNumber,
    string Tpa,
    string MemberId,
    DateTime ValidFrom,
    DateTime ValidTo,
    decimal CoverageLimit,
    string Status,
    string Eligibility,
    string EligibilityNotes,
    DateTime? EligibilityCheckedAt);

public record ClaimCreateRequest(int PolicyId, int? InvoiceId, decimal Amount, string Notes);

public record ClaimDto(
    int Id,
    string ClaimNumber,
    int PolicyId,
    string Provider,
    string PolicyNumber,
    int PatientId,
    string PatientName,
    int? InvoiceId,
    string? InvoiceNumber,
    decimal Amount,
    string Status,
    string Notes,
    DateTime CreatedAt,
    DateTime? SubmittedAt);

public record ClaimStatusRequest(string Status);
