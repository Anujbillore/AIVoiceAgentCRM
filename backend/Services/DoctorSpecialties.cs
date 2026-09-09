namespace AiVoicePortal.Api.Services;

public static class DoctorSpecialties
{
    public const string Dentist = "Dentist";
    public const string GeneralPhysician = "General Physician";
    public const string Pediatrics = "Pediatrics";
    public const string Dermatology = "Dermatology";
    public const string Orthopedics = "Orthopedics";
    public const string Ent = "ENT";
    public const string Gynecology = "Gynecology";
    public const string Cardiology = "Cardiology";
    public const string Ophthalmology = "Ophthalmology";
    public const string Physiotherapy = "Physiotherapy";

    public static readonly string[] All =
    [
        Dentist,
        GeneralPhysician,
        Pediatrics,
        Dermatology,
        Orthopedics,
        Ent,
        Gynecology,
        Cardiology,
        Ophthalmology,
        Physiotherapy
    ];

    public static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(s => s.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool Matches(string? doctorSpecialization, string? needed)
    {
        if (string.IsNullOrWhiteSpace(needed))
        {
            return true;
        }

        var spec = doctorSpecialization ?? string.Empty;
        if (spec.Contains(needed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (needed.Equals(Dentist, StringComparison.OrdinalIgnoreCase))
        {
            return spec.Contains("dental", StringComparison.OrdinalIgnoreCase)
                || spec.Contains("dentistry", StringComparison.OrdinalIgnoreCase)
                || spec.Contains("orthodont", StringComparison.OrdinalIgnoreCase);
        }

        if (needed.Equals(GeneralPhysician, StringComparison.OrdinalIgnoreCase))
        {
            return spec.Contains("general", StringComparison.OrdinalIgnoreCase)
                || spec.Contains("physician", StringComparison.OrdinalIgnoreCase)
                || spec.Contains("family", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
