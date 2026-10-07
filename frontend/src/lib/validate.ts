export function digitsOnly(value: string) {
  return (value ?? "").replace(/\D/g, "");
}

export function isValidName(value: string) {
  return value.trim().length >= 2;
}

export function isValidPhone(value: string) {
  const digits = digitsOnly(value);
  return digits.length >= 10 && digits.length <= 15;
}

export function isValidEmail(value: string) {
  if (!value.trim()) return true;
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

export function isValidAge(value: number) {
  return Number.isFinite(value) && value >= 1 && value <= 120;
}

export function isPositiveAmount(value: number) {
  return Number.isFinite(value) && value > 0;
}

export function requiredMessage(field: string, ok: boolean) {
  return ok ? "" : `${field} is required.`;
}
