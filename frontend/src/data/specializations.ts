export const DOCTOR_SPECIALIZATIONS = [
  "Dentist",
  "General Physician",
  "Pediatrics",
  "Dermatology",
  "Orthopedics",
  "ENT",
  "Gynecology",
  "Cardiology",
  "Ophthalmology",
  "Physiotherapy",
] as const;

export function specializationOptions(current?: string) {
  const extras = current && !DOCTOR_SPECIALIZATIONS.includes(current as (typeof DOCTOR_SPECIALIZATIONS)[number])
    ? [current]
    : [];
  return [...DOCTOR_SPECIALIZATIONS, ...extras];
}
