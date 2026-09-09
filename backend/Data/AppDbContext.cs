using AiVoicePortal.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<CallLog> CallLogs => Set<CallLog>();
    public DbSet<CallCallback> CallCallbacks => Set<CallCallback>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<PatientCharge> PatientCharges => Set<PatientCharge>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentSplit> PaymentSplits => Set<PaymentSplit>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<InsuranceClaim> InsuranceClaims => Set<InsuranceClaim>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Doctor>(entity =>
        {
            entity.HasIndex(d => d.Email).IsUnique();
            entity.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DoctorSchedule>(entity =>
        {
            entity.HasOne(s => s.Doctor)
                .WithMany(d => d.Schedules)
                .HasForeignKey(s => s.DoctorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Patient>(entity =>
        {
            entity.HasIndex(p => p.Uhid);
            entity.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CallLog>(entity =>
        {
            entity.HasIndex(c => c.ExternalId);
        });

        builder.Entity<Appointment>(entity =>
        {
            entity.HasOne(a => a.Patient)
                .WithMany(p => p.Appointments)
                .HasForeignKey(a => a.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Doctor)
                .WithMany(d => d.Appointments)
                .HasForeignKey(a => a.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PatientDocument>(entity =>
        {
            entity.HasOne(d => d.Patient)
                .WithMany(p => p.Documents)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Appointment)
                .WithMany()
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PatientCharge>(entity =>
        {
            entity.Property(c => c.Amount).HasPrecision(12, 2);
            entity.HasOne(c => c.Patient)
                .WithMany(p => p.Charges)
                .HasForeignKey(c => c.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Appointment)
                .WithMany()
                .HasForeignKey(c => c.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Invoice>(entity =>
        {
            entity.HasIndex(i => i.Number).IsUnique();
            entity.Property(i => i.Subtotal).HasPrecision(12, 2);
            entity.Property(i => i.Tax).HasPrecision(12, 2);
            entity.Property(i => i.Discount).HasPrecision(12, 2);
            entity.Property(i => i.Total).HasPrecision(12, 2);
            entity.Property(i => i.PaidAmount).HasPrecision(12, 2);
            entity.Property(i => i.RefundedAmount).HasPrecision(12, 2);
            entity.HasOne(i => i.Patient).WithMany().HasForeignKey(i => i.PatientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(i => i.Appointment).WithMany().HasForeignKey(i => i.AppointmentId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InvoiceLine>(entity =>
        {
            entity.Property(l => l.Quantity).HasPrecision(12, 2);
            entity.Property(l => l.UnitPrice).HasPrecision(12, 2);
            entity.Property(l => l.Amount).HasPrecision(12, 2);
            entity.HasOne(l => l.Invoice).WithMany(i => i.Lines).HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Payment>(entity =>
        {
            entity.HasIndex(p => p.ReceiptNumber).IsUnique();
            entity.Property(p => p.Amount).HasPrecision(12, 2);
            entity.HasOne(p => p.Patient).WithMany().HasForeignKey(p => p.PatientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Invoice).WithMany(i => i.Payments).HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PaymentSplit>(entity =>
        {
            entity.Property(s => s.Amount).HasPrecision(12, 2);
            entity.HasOne(s => s.Payment).WithMany(p => p.Splits).HasForeignKey(s => s.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Refund>(entity =>
        {
            entity.Property(r => r.Amount).HasPrecision(12, 2);
            entity.HasOne(r => r.Payment).WithMany(p => p.Refunds).HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.Invoice).WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InsurancePolicy>(entity =>
        {
            entity.Property(p => p.CoverageLimit).HasPrecision(12, 2);
            entity.HasOne(p => p.Patient).WithMany().HasForeignKey(p => p.PatientId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InsuranceClaim>(entity =>
        {
            entity.HasIndex(c => c.ClaimNumber).IsUnique();
            entity.Property(c => c.Amount).HasPrecision(12, 2);
            entity.HasOne(c => c.Policy).WithMany(p => p.Claims).HasForeignKey(c => c.PolicyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(c => c.Patient).WithMany().HasForeignKey(c => c.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.Invoice).WithMany().HasForeignKey(c => c.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
