using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
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

        if (!await db.Doctors.AnyAsync(d => d.Specialization.Contains("Dentist")))
        {
            db.Doctors.AddRange(
                new Doctor
                {
                    Name = "Dr. Desai",
                    Email = "desai@clinic.com",
                    Specialization = "Dentist",
                    Phone = "+91 98765 44444",
                    IsActive = true,
                    Schedules = WeekdaySchedule(new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0))
                },
                new Doctor
                {
                    Name = "Dr. Banerjee",
                    Email = "banerjee@clinic.com",
                    Specialization = "Dentist",
                    Phone = "+91 98765 55555",
                    IsActive = true,
                    Schedules = WeekdaySchedule(new TimeSpan(10, 0, 0), new TimeSpan(18, 0, 0))
                });
            await db.SaveChangesAsync();
        }

        var soleGp = await db.Doctors
            .Include(d => d.Schedules)
            .Where(d => d.IsActive && d.Specialization.Contains("General Physician"))
            .OrderBy(d => d.Id)
            .FirstOrDefaultAsync();
        if (soleGp is not null && soleGp.Schedules.Count == 0)
        {
            soleGp.Schedules = WeekdaySchedule(new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0));
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
            var rahul = await db.Patients.FirstOrDefaultAsync(p => p.Name == "Rahul Sharma");
            var priya = await db.Patients.FirstOrDefaultAsync(p => p.Name == "Priya Nair");
            var mehta = await db.Doctors.FirstOrDefaultAsync(d => d.Specialization.Contains("General Physician"));
            var kapoor = await db.Doctors.FirstOrDefaultAsync(d => d.Name == "Dr. Kapoor") ?? mehta;
            var today = DateTime.Today;
            if (rahul is not null && priya is not null && mehta is not null)
            {
            db.Appointments.AddRange(
                new Appointment { PatientId = rahul.Id, DoctorId = mehta.Id, ScheduledAt = today.AddHours(17), Status = "Scheduled", Notes = "Booked via AI voice agent" },
                new Appointment { PatientId = priya.Id, DoctorId = kapoor.Id, ScheduledAt = today.AddDays(1).AddHours(11), Status = "Scheduled", Notes = "Fever follow-up" }
            );
            await db.SaveChangesAsync();
            }
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

        foreach (var doctor in await db.Doctors.ToListAsync())
        {
            if (string.IsNullOrWhiteSpace(doctor.Email))
            {
                continue;
            }

            var user = await EnsureUserAsync(users, doctor.Email, "Doctor@123", doctor.Name, AppRoles.Doctor);
            if (string.IsNullOrWhiteSpace(doctor.UserId))
            {
                doctor.UserId = user.Id;
            }
        }

        var mehtaUser = await EnsureUserAsync(users, "mehta@clinic.com", "Doctor@123", "Dr. Mehta", AppRoles.Doctor);
        var mehtaDoctor = await db.Doctors.FirstOrDefaultAsync(d => d.Email == "mehta@clinic.com");
        if (mehtaDoctor is null)
        {
            mehtaDoctor = new Doctor
            {
                UserId = mehtaUser.Id,
                Name = "Dr. Mehta",
                Email = "mehta@clinic.com",
                Specialization = DoctorSpecialties.GeneralPhysician,
                Phone = "+91 98765 11111",
                IsActive = true,
                Schedules = WeekdaySchedule(new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0))
            };
            db.Doctors.Add(mehtaDoctor);
        }
        else
        {
            mehtaDoctor.UserId = mehtaUser.Id;
            mehtaDoctor.IsActive = true;
            if (string.IsNullOrWhiteSpace(mehtaDoctor.Specialization))
            {
                mehtaDoctor.Specialization = DoctorSpecialties.GeneralPhysician;
            }
        }

        await db.SaveChangesAsync();
        await EnsureDashboardDemoAsync(db);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDashboardDemoAsync(AppDbContext db)
    {
        const string marker = "Dashboard demo seed";
        var patients = await db.Patients.OrderBy(p => p.Id).Take(4).ToListAsync();
        var mehta = await db.Doctors.FirstOrDefaultAsync(d => d.Email == "mehta@clinic.com" && d.IsActive);
        var others = await db.Doctors.Where(d => d.IsActive && d.Email != "mehta@clinic.com").OrderBy(d => d.Id).Take(2).ToListAsync();
        if (patients.Count < 3 || mehta is null)
        {
            return;
        }

        var doctors = new List<Doctor> { mehta };
        doctors.AddRange(others);
        var today = IndiaTime.Now.Date;

        if (!await db.Appointments.AnyAsync(a => a.Notes.Contains(marker)))
        {
            var demos = new List<(Patient Patient, Doctor Doctor, DateTime At, string Status, int Minutes)>
            {
                (patients[0], mehta, today.AddDays(-5).AddHours(10), "Completed", 4),
                (patients[1], mehta, today.AddDays(-3).AddHours(11), "Completed", 6),
                (patients[2], doctors.Count > 1 ? doctors[1] : mehta, today.AddDays(-2).AddHours(15), "Cancelled", 2),
                (patients[0], mehta, today.AddDays(-1).AddHours(9).AddMinutes(30), "Scheduled", 5),
                (patients[1], doctors.Count > 1 ? doctors[1] : mehta, today.AddHours(16), "Pending", 3),
                (patients[2], mehta, today.AddDays(1).AddHours(12), "Scheduled", 0),
                (patients.Count > 3 ? patients[3] : patients[0], doctors.Count > 2 ? doctors[2] : mehta, today.AddDays(2).AddHours(14), "Scheduled", 0),
            };

            foreach (var item in demos)
            {
                await AddDemoVisitAsync(db, item.Patient, item.Doctor, item.At, item.Status, item.Minutes, marker);
            }

            return;
        }

        if (!await db.Appointments.AnyAsync(a => a.DoctorId == mehta.Id && a.Notes.Contains(marker)))
        {
            await AddDemoVisitAsync(db, patients[0], mehta, today.AddDays(-4).AddHours(10), "Completed", 4, marker);
            await AddDemoVisitAsync(db, patients[1], mehta, today.AddDays(-1).AddHours(11), "Scheduled", 5, marker);
            await AddDemoVisitAsync(db, patients[2], mehta, today.AddDays(1).AddHours(12), "Pending", 0, marker);
        }
    }

    private static async Task AddDemoVisitAsync(
        AppDbContext db,
        Patient patient,
        Doctor doctor,
        DateTime at,
        string status,
        int minutes,
        string marker)
    {
        var appointment = new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            ScheduledAt = at,
            Status = status,
            Notes = $"{marker} · clinic visit",
            CreatedAt = IndiaTime.ToUtcFromIst(at.AddHours(-2))
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        if (minutes <= 0)
        {
            return;
        }

        db.CallLogs.Add(new CallLog
        {
            PatientId = patient.Id,
            CallerName = patient.Name,
            CallerPhone = patient.Contact,
            Summary = $"{patient.Name} booked with {doctor.Name} for {at:ddd d MMM, h:mm tt} IST.",
            ActionTaken = $"Booked with {doctor.Name} at {at:h:mm tt} IST ({minutes * 60}s)",
            Intent = "Appointment",
            Transcript = $"I need an appointment with {doctor.Name}.",
            Outcome = status == "Cancelled" ? "Cancelled" : "Booked",
            Timestamp = IndiaTime.ToUtcFromIst(at.AddHours(-1)),
            DurationSeconds = minutes * 60,
            ExternalId = $"sarvam-book:{appointment.Id}",
            ConsentGiven = true
        });
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
        else if (email.EndsWith("@clinic.com", StringComparison.OrdinalIgnoreCase))
        {
            // Keep demo logins reliable across restarts.
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await users.ResetPasswordAsync(user, token, password);
            if (user.FullName != fullName)
            {
                user.FullName = fullName;
                await users.UpdateAsync(user);
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
