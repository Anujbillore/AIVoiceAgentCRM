export function formatStamp(value: string) {
  return new Date(value).toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}

export function formatTime(value: string) {
  return new Date(value).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
}

export function formatDay(value: string | Date) {
  return new Date(value).toLocaleDateString([], { weekday: "short", month: "short", day: "numeric" });
}

export function formatLongDay(value: string | Date) {
  return new Date(value).toLocaleDateString([], { weekday: "long", month: "long", day: "numeric", year: "numeric" });
}

export function formatMonthDay(value: string | Date) {
  return new Date(value).toLocaleDateString([], { month: "short", day: "numeric" }).toUpperCase();
}

export function isoDate(value: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

export function sameDay(a: string | Date, b: string | Date) {
  const left = new Date(a);
  const right = new Date(b);
  return left.getFullYear() === right.getFullYear() && left.getMonth() === right.getMonth() && left.getDate() === right.getDate();
}

export function displayStatus(status: string) {
  if (status === "Scheduled") return "confirmed";
  return status.toLowerCase();
}

export function toInputValue(date: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
