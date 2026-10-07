const IST = "Asia/Kolkata";

export function parseClinicDate(value: string | Date) {
  if (value instanceof Date) return value;
  const raw = value.trim();
  if (!raw) return new Date(Number.NaN);
  if (/[zZ]|[+-]\d{2}:\d{2}$/.test(raw)) return new Date(raw);
  const iso = raw.includes("T") ? raw : `${raw}T00:00:00`;
  return new Date(`${iso}+05:30`);
}

function istOptions(extra: Intl.DateTimeFormatOptions): Intl.DateTimeFormatOptions {
  return { timeZone: IST, ...extra };
}

export function formatStamp(value: string | Date) {
  return `${parseClinicDate(value).toLocaleString("en-IN", istOptions({
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
    hour12: true,
  }))} IST`;
}

export function formatTime(value: string) {
  return parseClinicDate(value).toLocaleTimeString("en-IN", istOptions({ hour: "numeric", minute: "2-digit", hour12: true }));
}

export function formatDay(value: string | Date) {
  return parseClinicDate(value).toLocaleDateString("en-IN", istOptions({ weekday: "short", month: "short", day: "numeric" }));
}

export function formatLongDay(value: string | Date) {
  return parseClinicDate(value).toLocaleDateString("en-IN", istOptions({ weekday: "long", month: "long", day: "numeric", year: "numeric" }));
}

export function formatMonthDay(value: string | Date) {
  return parseClinicDate(value).toLocaleDateString("en-IN", istOptions({ month: "short", day: "numeric" })).toUpperCase();
}

export function isoDate(value: Date) {
  return new Intl.DateTimeFormat("en-CA", istOptions({ year: "numeric", month: "2-digit", day: "2-digit" })).format(value);
}

export function sameDay(a: string | Date, b: string | Date) {
  const left = parseClinicDate(a);
  const right = parseClinicDate(b);
  const leftParts = new Intl.DateTimeFormat("en-CA", istOptions({ year: "numeric", month: "2-digit", day: "2-digit" })).format(left);
  const rightParts = new Intl.DateTimeFormat("en-CA", istOptions({ year: "numeric", month: "2-digit", day: "2-digit" })).format(right);
  return leftParts === rightParts;
}

export function displayStatus(status: string) {
  if (status === "Scheduled" || status === "Confirmed" || status === "Pending" || status === "Not Attended") return "not attended";
  if (status === "Partially Paid" || status === "Partial") return "partially paid";
  return status.toLowerCase();
}

export function toInputValue(date: Date) {
  const parts = new Intl.DateTimeFormat("en-CA", istOptions({
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  })).formatToParts(date);
  const get = (type: string) => parts.find((part) => part.type === type)?.value ?? "00";
  return `${get("year")}-${get("month")}-${get("day")}T${get("hour")}:${get("minute")}`;
}
