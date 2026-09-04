namespace AiVoicePortal.Api.Models;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Doctor = "Doctor";
    public const string Patient = "Patient";

    public static readonly string[] All = [Admin, Doctor, Patient];
}
