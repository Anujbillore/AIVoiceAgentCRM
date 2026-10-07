import type { Appointment, Doctor } from "../api/client";

export function sameMinute(a: string | Date, b: string | Date) {
  const left = new Date(a);
  const right = new Date(b);
  return (
    left.getFullYear() === right.getFullYear() &&
    left.getMonth() === right.getMonth() &&
    left.getDate() === right.getDate() &&
    left.getHours() === right.getHours() &&
    left.getMinutes() === right.getMinutes()
  );
}

export function isSlotTaken(appointments: Appointment[], doctorId: number, when: string, exceptId?: number) {
  return appointments.some(
    (item) =>
      item.doctorId === doctorId &&
      item.id !== exceptId &&
      item.status !== "Cancelled" &&
      sameMinute(item.scheduledAt, when),
  );
}

export function isWithinDoctorHours(doctor: Doctor | undefined, when: string) {
  if (!doctor?.schedules?.length) return true;
  const date = new Date(when);
  const mins = date.getHours() * 60 + date.getMinutes();
  return doctor.schedules.some((schedule) => {
    if (schedule.dayOfWeek !== date.getDay()) return false;
    const [startHour, startMin] = schedule.startTime.split(":").map(Number);
    const [endHour, endMin] = schedule.endTime.split(":").map(Number);
    return mins >= startHour * 60 + (startMin || 0) && mins < endHour * 60 + (endMin || 0);
  });
}
