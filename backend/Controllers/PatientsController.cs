using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientsController : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".doc", ".docx"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IAppointmentService _appointments;
    private readonly ISarvamAiService _ai;
    private readonly IWebHostEnvironment _env;

    public PatientsController(
        AppDbContext db,
        ICurrentUserService current,
        IAppointmentService appointments,
        ISarvamAiService ai,
        IWebHostEnvironment env)
    {
        _db = db;
        _current = current;
        _appointments = appointments;
        _ai = ai;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult<List<PatientDto>>> List(CancellationToken cancellationToken)
    {
        if (_current.IsPatient)
        {
            var own = await GetOwnAsync(cancellationToken);
            return own is null ? new List<PatientDto>() : new List<PatientDto> { await ToDtoAsync(own, cancellationToken) };
        }

        return await ProjectPatients(_db.Patients.OrderBy(p => p.Name)).ToListAsync(cancellationToken);
    }

    [HttpGet("me")]
    public async Task<ActionResult<PatientDto>> Me(CancellationToken cancellationToken)
    {
        var patient = await GetOwnAsync(cancellationToken);
        return patient is null ? NotFound(new { message = "Patient profile not found." }) : await ToDtoAsync(patient, cancellationToken);
    }

    [HttpGet("me/details")]
    public async Task<ActionResult<PatientDetailDto>> MeDetails(CancellationToken cancellationToken)
    {
        var patient = await GetOwnAsync(cancellationToken);
        return patient is null ? NotFound(new { message = "Patient profile not found." }) : await DetailsDtoAsync(patient, cancellationToken);
    }

    [HttpPut("me")]
    public async Task<ActionResult<PatientDto>> UpdateMe(PatientRequest request, CancellationToken cancellationToken)
    {
        var patient = await GetOwnAsync(cancellationToken);
        if (patient is null)
        {
            return NotFound(new { message = "Patient profile not found." });
        }

        Apply(patient, request);
        await _db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(patient, cancellationToken);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PatientDto>> Get(int id, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var patient = await _db.Patients.FindAsync([id], cancellationToken);
        return patient is null ? NotFound() : await ToDtoAsync(patient, cancellationToken);
    }

    [HttpGet("{id:int}/details")]
    public async Task<ActionResult<PatientDetailDto>> Details(int id, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var patient = await _db.Patients.FindAsync([id], cancellationToken);
        return patient is null ? NotFound() : await DetailsDtoAsync(patient, cancellationToken);
    }

    [HttpPost]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
    public async Task<ActionResult<PatientDto>> Create(PatientRequest request, CancellationToken cancellationToken)
    {
        var patient = new Patient
        {
            Name = request.Name,
            Age = request.Age,
            Contact = request.Contact,
            Email = request.Email ?? string.Empty,
            Address = request.Address ?? string.Empty,
            Notes = request.Notes ?? string.Empty
        };
        ApplyClinical(patient, request);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(cancellationToken);
        AssignUhid(patient);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = patient.Id }, await ToDtoAsync(patient, cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
    public async Task<ActionResult<PatientDto>> Update(int id, PatientRequest request, CancellationToken cancellationToken)
    {
        var patient = await _db.Patients.FindAsync([id], cancellationToken);
        if (patient is null)
        {
            return NotFound();
        }

        Apply(patient, request);
        await _db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(patient, cancellationToken);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var patient = await _db.Patients.FindAsync([id], cancellationToken);
        if (patient is null)
        {
            return NotFound();
        }

        DeletePatientFolder(id);
        _db.Patients.Remove(patient);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:int}/documents")]
    public async Task<ActionResult<List<PatientDocumentDto>>> ListDocuments(int id, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(id, cancellationToken))
        {
            return Forbid();
        }

        if (!await _db.Patients.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return await DocumentQuery(id).ToListAsync(cancellationToken);
    }

    [HttpPost("{id:int}/documents")]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<PatientDocumentDto>> Upload(int id, IFormFile file, [FromForm] string? category, [FromForm] int? appointmentId, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(id, cancellationToken))
        {
            return Forbid();
        }

        if (!await _db.Patients.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Choose a document to upload." });
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Allowed files: PDF, JPG, PNG, WEBP, DOC, DOCX." });
        }

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var folder = PatientFolder(id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, storedName);
        await using (var stream = System.IO.File.Create(path))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        string resolvedCategory;
        if (DocumentCategories.TryCanonical(category, out var chosen))
        {
            resolvedCategory = chosen;
        }
        else
        {
            resolvedCategory = await _ai.ClassifyDocumentAsync(file.FileName, cancellationToken);
        }

        var visitId = await ResolveVisitAsync(id, appointmentId, cancellationToken);
        var document = new PatientDocument
        {
            PatientId = id,
            AppointmentId = visitId,
            Category = resolvedCategory,
            OriginalName = Path.GetFileName(file.FileName),
            StoredName = storedName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow
        };
        _db.PatientDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Download), new { id, docId = document.Id }, ToDocumentDto(document));
    }

    [HttpGet("{id:int}/documents/{docId:int}/file")]
    public async Task<IActionResult> Download(int id, int docId, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var document = await _db.PatientDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.PatientId == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var path = Path.Combine(PatientFolder(id), document.StoredName);
        if (!System.IO.File.Exists(path))
        {
            return NotFound(new { message = "File is no longer on disk." });
        }

        return PhysicalFile(path, document.ContentType, document.OriginalName);
    }

    [HttpPatch("{id:int}/documents/{docId:int}")]
    public async Task<ActionResult<PatientDocumentDto>> Move(int id, int docId, DocumentCategoryRequest request, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var document = await _db.PatientDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.PatientId == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (DocumentCategories.TryCanonical(request.Category, out var category))
        {
            document.Category = category;
        }

        if (request.AppointmentId.HasValue)
        {
            document.AppointmentId = await ResolveVisitAsync(id, request.AppointmentId == 0 ? null : request.AppointmentId, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToDocumentDto(document);
    }

    [HttpPost("{id:int}/charges")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
    public async Task<ActionResult<PatientChargeDto>> AddCharge(int id, PatientChargeRequest request, CancellationToken cancellationToken)
    {
        if (!await _db.Patients.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var kind = NormalizeChargeKind(request.Kind);
        var charge = new PatientCharge
        {
            PatientId = id,
            AppointmentId = await ResolveVisitAsync(id, request.AppointmentId, cancellationToken),
            Kind = kind,
            Title = request.Title,
            Amount = request.Amount,
            Notes = request.Notes ?? string.Empty,
            ChargeDate = request.ChargeDate ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.PatientCharges.Add(charge);
        await _db.SaveChangesAsync(cancellationToken);
        return ToChargeDto(charge);
    }

    [HttpDelete("{id:int}/charges/{chargeId:int}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
    public async Task<IActionResult> DeleteCharge(int id, int chargeId, CancellationToken cancellationToken)
    {
        var charge = await _db.PatientCharges.FirstOrDefaultAsync(c => c.Id == chargeId && c.PatientId == id, cancellationToken);
        if (charge is null)
        {
            return NotFound();
        }

        _db.PatientCharges.Remove(charge);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}/documents/{docId:int}")]
    public async Task<IActionResult> DeleteDocument(int id, int docId, CancellationToken cancellationToken)
    {
        if (!await CanAccessAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var document = await _db.PatientDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.PatientId == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var path = Path.Combine(PatientFolder(id), document.StoredName);
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }

        _db.PatientDocuments.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<bool> CanAccessAsync(int patientId, CancellationToken cancellationToken)
    {
        if (_current.IsAdmin || _current.IsDoctor)
        {
            return true;
        }

        var ownId = await _current.GetPatientIdAsync(cancellationToken);
        return ownId == patientId;
    }

    private async Task<Patient?> GetOwnAsync(CancellationToken cancellationToken)
    {
        var id = await _current.GetPatientIdAsync(cancellationToken);
        return id is null ? null : await _db.Patients.FindAsync([id.Value], cancellationToken);
    }

    private async Task<PatientDetailDto> DetailsDtoAsync(Patient patient, CancellationToken cancellationToken)
    {
        var visits = await _appointments.ListAsync(null, patient.Id, cancellationToken);
        var documents = await DocumentQuery(patient.Id).ToListAsync(cancellationToken);
        var charges = await ChargeQuery(patient.Id).ToListAsync(cancellationToken);
        return new PatientDetailDto(await ToDtoAsync(patient, cancellationToken), visits, documents, charges);
    }

    private IQueryable<PatientDocumentDto> DocumentQuery(int patientId) =>
        _db.PatientDocuments
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new PatientDocumentDto(d.Id, d.PatientId, d.AppointmentId, d.Category, d.OriginalName, d.ContentType, d.SizeBytes, d.UploadedAt));

    private IQueryable<PatientChargeDto> ChargeQuery(int patientId) =>
        _db.PatientCharges
            .Where(c => c.PatientId == patientId)
            .OrderByDescending(c => c.ChargeDate)
            .Select(c => new PatientChargeDto(c.Id, c.PatientId, c.AppointmentId, c.Kind, c.Title, c.Amount, c.Notes, c.ChargeDate));

    private static IQueryable<PatientDto> ProjectPatients(IQueryable<Patient> query) =>
        query.Select(p => new PatientDto(
            p.Id,
            p.Name,
            p.Age,
            p.Contact,
            p.Email,
            p.Address,
            p.Notes,
            p.CreatedAt,
            p.Appointments.Count,
            p.Documents.Count,
            p.Uhid,
            p.Gender,
            p.BloodGroup,
            p.EmergencyName,
            p.EmergencyPhone,
            p.Allergies));

    private async Task<PatientDto> ToDtoAsync(Patient patient, CancellationToken cancellationToken)
    {
        var visits = await _db.Appointments.CountAsync(a => a.PatientId == patient.Id, cancellationToken);
        var documents = await _db.PatientDocuments.CountAsync(d => d.PatientId == patient.Id, cancellationToken);
        return new PatientDto(
            patient.Id,
            patient.Name,
            patient.Age,
            patient.Contact,
            patient.Email,
            patient.Address,
            patient.Notes,
            patient.CreatedAt,
            visits,
            documents,
            patient.Uhid,
            patient.Gender,
            patient.BloodGroup,
            patient.EmergencyName,
            patient.EmergencyPhone,
            patient.Allergies);
    }

    private static PatientDocumentDto ToDocumentDto(PatientDocument document) =>
        new(document.Id, document.PatientId, document.AppointmentId, document.Category, document.OriginalName, document.ContentType, document.SizeBytes, document.UploadedAt);

    private static PatientChargeDto ToChargeDto(PatientCharge charge) =>
        new(charge.Id, charge.PatientId, charge.AppointmentId, charge.Kind, charge.Title, charge.Amount, charge.Notes, charge.ChargeDate);

    private static void Apply(Patient patient, PatientRequest request)
    {
        patient.Name = request.Name;
        patient.Age = request.Age;
        patient.Contact = request.Contact;
        patient.Email = request.Email ?? string.Empty;
        patient.Address = request.Address ?? string.Empty;
        patient.Notes = request.Notes ?? string.Empty;
        ApplyClinical(patient, request);
    }

    private static void ApplyClinical(Patient patient, PatientRequest request)
    {
        patient.Gender = request.Gender ?? string.Empty;
        patient.BloodGroup = request.BloodGroup ?? string.Empty;
        patient.EmergencyName = request.EmergencyName ?? string.Empty;
        patient.EmergencyPhone = request.EmergencyPhone ?? string.Empty;
        patient.Allergies = request.Allergies ?? string.Empty;
    }

    private static void AssignUhid(Patient patient)
    {
        if (string.IsNullOrWhiteSpace(patient.Uhid))
        {
            patient.Uhid = $"ANJ-{patient.Id:D6}";
        }
    }

    private async Task<int?> ResolveVisitAsync(int patientId, int? appointmentId, CancellationToken cancellationToken)
    {
        if (appointmentId is null or 0)
        {
            return null;
        }

        var exists = await _db.Appointments.AnyAsync(a => a.Id == appointmentId && a.PatientId == patientId, cancellationToken);
        return exists ? appointmentId : null;
    }

    private static string NormalizeChargeKind(string kind)
    {
        var value = kind.Trim().ToLowerInvariant();
        return value switch
        {
            "daily bill" or "daily bills" or "daily" => "Daily bill",
            "insurance" or "claim" => "Insurance",
            "payment" or "receipt" => "Payment",
            _ => "Billing"
        };
    }

    private string PatientFolder(int patientId) =>
        Path.Combine(_env.ContentRootPath, "uploads", "patients", patientId.ToString());

    private void DeletePatientFolder(int patientId)
    {
        var folder = PatientFolder(patientId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }
}
