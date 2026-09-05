using AiVoicePortal.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        AppDbContext db;
        try
        {
            db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not open Supabase. On Render, set ConnectionStrings__DefaultConnection to the Session pooler URI (port 5432), with no quotes. " + Innermost(ex),
                ex);
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Database migrate failed. On the Session pooler the username must be postgres.YOUR_PROJECT_REF (not postgres). Copy the Session pooler URI from Supabase → Database, port 5432. If the database password contains @, paste Host=...;Username=...;Password=... instead of a URI, or encode @ as %40. " + Innermost(ex),
                ex);
        }

        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }

        var admin = await EnsureUserAsync(users, "admin@clinic.com", "Admin@123", "Clinic Admin", AppRoles.Admin);
        var doctorUser = await EnsureUserAsync(users, "mehta@clinic.com", "Doctor@123", "Dr. Mehta", AppRoles.Doctor);
        await EnsureUserAsync(users, "patient@clinic.com", "Patient@123", "Rahul Sharma", AppRoles.Patient, age: 34);

        if (!await db.Doctors.AnyAsync())
        {
            var mehta = new Doctor
            {
                UserId = doctorUser.Id,
                Name = "Dr. Mehta",
                Email = "mehta@clinic.com",
                Specialization = "General Physician",
                Phone = "+91 98765 11111",
                IsActive = true,
                Schedules = WeekdaySchedule(new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0))
            };
            var kapoor = new Doctor
            {
                Name = "Dr. Kapoor",
                Email = "kapoor@clinic.com",
                Specialization = "Pediatrics",
                Phone = "+91 98765 22222",
                IsActive = true,
                Schedules = WeekdaySchedule(new TimeSpan(10, 0, 0), new TimeSpan(16, 0, 0))
            };
            var iyer = new Doctor
            {
                Name = "Dr. Iyer",
                Email = "iyer@clinic.com",
                Specialization = "Dermatology",
                Phone = "+91 98765 33333",
                IsActive = true,
                Schedules = WeekdaySchedule(new TimeSpan(11, 0, 0), new TimeSpan(19, 0, 0))
            };
            db.Doctors.AddRange(mehta, kapoor, iyer);
            await db.SaveChangesAsync();
        }

        if (!await db.Patients.AnyAsync())
        {
            var patientUser = await users.FindByEmailAsync("patient@clinic.com");
            db.Patients.AddRange(
                new Patient { UserId = patientUser?.Id, Name = "Rahul Sharma", Age = 34, Contact = "+91 90000 10001", Email = "patient@clinic.com", Address = "Andheri, Mumbai", Gender = "Male", BloodGroup = "B+", EmergencyName = "Neha Sharma", EmergencyPhone = "+91 90000 10011", Allergies = "Penicillin" },
                new Patient { Name = "Priya Nair", Age = 29, Contact = "+91 90000 10002", Email = "priya@example.com", Address = "Pune", Gender = "Female", BloodGroup = "O+" },
                new Patient { Name = "Amit Verma", Age = 41, Contact = "+91 90000 10003", Email = "amit@example.com", Address = "Delhi", Gender = "Male", BloodGroup = "A+" },
                new Patient { Name = "Sana Khan", Age = 8, Contact = "+91 90000 10004", Email = "sana@example.com", Address = "Hyderabad", Notes = "Pediatric follow-up", Gender = "Female", BloodGroup = "B+" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.Appointments.AnyAsync())
        {
            var rahul = await db.Patients.FirstAsync(p => p.Name == "Rahul Sharma");
            var priya = await db.Patients.FirstAsync(p => p.Name == "Priya Nair");
            var mehta = await db.Doctors.FirstAsync(d => d.Name == "Dr. Mehta");
            var kapoor = await db.Doctors.FirstAsync(d => d.Name == "Dr. Kapoor");
            var today = DateTime.Today;

            db.Appointments.AddRange(
                new Appointment { PatientId = rahul.Id, DoctorId = mehta.Id, ScheduledAt = today.AddHours(17), Status = "Scheduled", Notes = "Booked via AI voice agent" },
                new Appointment { PatientId = priya.Id, DoctorId = kapoor.Id, ScheduledAt = today.AddDays(1).AddHours(11), Status = "Scheduled", Notes = "Fever follow-up" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.CallLogs.AnyAsync())
        {
            db.CallLogs.AddRange(
                new CallLog
                {
                    CallerName = "Rahul Sharma",
                    CallerPhone = "+91 90000 10001",
                    Summary = "Asked for Dr. Mehta appointment",
                    ActionTaken = "Booked for 5 PM",
                    Intent = "Appointment",
                    Transcript = "I need an appointment with Dr. Mehta at 5 PM today.",
                    Timestamp = DateTime.Today.AddHours(20).AddMinutes(30)
                },
                new CallLog
                {
                    CallerName = "Unknown",
                    CallerPhone = "+91 80000 00000",
                    Summary = "Spam call",
                    ActionTaken = "Ended politely",
                    Intent = "Spam",
                    Transcript = "Congratulations you have won a lottery prize.",
                    Timestamp = DateTime.Today.AddHours(20).AddMinutes(32)
                }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.AiSettings.AnyAsync())
        {
            db.AiSettings.Add(new AiSettings
            {
                AgentName = "Anuj's AI Assistant",
                WelcomeMessage = "Hello, thank you for calling Anuj's clinic. I can help you book a doctor appointment or answer a quick query.",
                Language = "en-IN",
                Instructions = "Be polite and concise. Book appointments when a doctor and time are mentioned. End spam calls politely. Summarize every call for the clinic dashboard.",
                VoiceSpeaker = "ritu"
            });
            await db.SaveChangesAsync();
        }

        var ai = await db.AiSettings.FirstOrDefaultAsync();
        if (ai is not null && string.IsNullOrWhiteSpace(ai.ConsentMessage))
        {
            ai.ConsentMessage = "This call may be recorded and handled by our clinic AI. You can ask for a person at any time.";
        }

        var missingUhid = await db.Patients.Where(p => p.Uhid == null || p.Uhid == "").ToListAsync();
        foreach (var patient in missingUhid)
        {
            patient.Uhid = $"ANJ-{patient.Id:D6}";
        }

        var rahulChart = await db.Patients.FirstOrDefaultAsync(p => p.Name == "Rahul Sharma");
        if (rahulChart is not null)
        {
            rahulChart.IsVip = true;
        }

        if (rahulChart is not null && string.IsNullOrWhiteSpace(rahulChart.BloodGroup))
        {
            rahulChart.Gender = "Male";
            rahulChart.BloodGroup = "B+";
            rahulChart.EmergencyName = "Neha Sharma";
            rahulChart.EmergencyPhone = "+91 90000 10011";
            rahulChart.Allergies = "Penicillin";
        }

        if (rahulChart is not null && !await db.PatientCharges.AnyAsync(c => c.PatientId == rahulChart.Id))
        {
            var visit = await db.Appointments
                .Where(a => a.PatientId == rahulChart.Id)
                .OrderByDescending(a => a.ScheduledAt)
                .FirstOrDefaultAsync();
            db.PatientCharges.AddRange(
                new PatientCharge
                {
                    PatientId = rahulChart.Id,
                    AppointmentId = visit?.Id,
                    Kind = "Daily bill",
                    Title = "OPD consultation",
                    Amount = 800,
                    ChargeDate = DateTime.Today
                },
                new PatientCharge
                {
                    PatientId = rahulChart.Id,
                    Kind = "Billing",
                    Title = "Lab invoice",
                    Amount = 1450,
                    ChargeDate = DateTime.Today.AddDays(-3)
                }
            );
        }

        if (rahulChart is not null && !await db.Invoices.AnyAsync())
        {
            var invoice = new Invoice
            {
                PatientId = rahulChart.Id,
                Status = "Partial",
                IssuedAt = DateTime.Today,
                DueAt = DateTime.Today.AddDays(7),
                Notes = "OPD + lab",
                Subtotal = 2250,
                Tax = 0,
                Discount = 0,
                Total = 2250,
                PaidAmount = 800,
                Lines =
                [
                    new InvoiceLine { Description = "OPD consultation", Quantity = 1, UnitPrice = 800, Amount = 800 },
                    new InvoiceLine { Description = "CBC lab panel", Quantity = 1, UnitPrice = 1450, Amount = 1450 }
                ]
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoice.Number = $"INV-{DateTime.UtcNow.Year}-{invoice.Id:D5}";

            var payment = new Payment
            {
                PatientId = rahulChart.Id,
                InvoiceId = invoice.Id,
                Amount = 800,
                Status = "Completed",
                Gateway = "Cash drawer",
                Reference = $"CASH-{DateTime.UtcNow:yyyyMMddHHmmss}",
                PaidAt = DateTime.Today,
                Splits = [new PaymentSplit { Method = "Cash", Amount = 800 }]
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            payment.ReceiptNumber = $"RCT-{DateTime.UtcNow.Year}-{payment.Id:D5}";

            var policy = new InsurancePolicy
            {
                PatientId = rahulChart.Id,
                Provider = "Star Health",
                PolicyNumber = "SH-998877",
                Tpa = "Medi Assist",
                MemberId = "MA-44021",
                ValidFrom = DateTime.Today.AddMonths(-6),
                ValidTo = DateTime.Today.AddMonths(6),
                CoverageLimit = 500000,
                Status = "Active",
                Eligibility = "Eligible",
                EligibilityNotes = "Demo eligibility check passed.",
                EligibilityCheckedAt = DateTime.UtcNow
            };
            db.InsurancePolicies.Add(policy);
            await db.SaveChangesAsync();

            var claim = new InsuranceClaim
            {
                PolicyId = policy.Id,
                PatientId = rahulChart.Id,
                InvoiceId = invoice.Id,
                Amount = 1450,
                Status = "Under review",
                Notes = "Lab charges submitted to TPA",
                SubmittedAt = DateTime.UtcNow
            };
            db.InsuranceClaims.Add(claim);
            await db.SaveChangesAsync();
            claim.ClaimNumber = $"CLM-{DateTime.UtcNow.Year}-{claim.Id:D5}";
        }

        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> users,
        string email,
        string password,
        string fullName,
        string role,
        int? age = null)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                Age = age
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            await users.AddToRoleAsync(user, role);
        }

        return user;
    }

    private static string Innermost(Exception ex)
    {
        while (ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex.Message;
    }

    private static List<DoctorSchedule> WeekdaySchedule(TimeSpan start, TimeSpan end)
    {
        return Enum.GetValues<DayOfWeek>()
            .Where(d => d is not DayOfWeek.Sunday)
            .Select(d => new DoctorSchedule { DayOfWeek = d, StartTime = start, EndTime = end })
            .ToList();
    }
}
