import { useEffect, useMemo, useState } from "react";

export const PAGE_SIZE = 8;
export type SortDir = "latest" | "oldest";

const TIME_KEYS = [
  "timestamp",
  "paidAt",
  "uploadedAt",
  "issuedAt",
  "chargeDate",
  "sentAt",
  "scheduledAt",
  "createdAt",
  "at",
  "refundedAt",
] as const;

export function recordTime(item: unknown): number {
  if (!item || typeof item !== "object") return 0;
  const rec = item as Record<string, unknown>;
  for (const key of TIME_KEYS) {
    const value = rec[key];
    if (typeof value === "string" || value instanceof Date) {
      const t = new Date(value).getTime();
      if (!Number.isNaN(t)) return t;
    }
  }
  if (typeof rec.id === "number") return rec.id;
  return 0;
}

export function sortRecords<T>(items: T[], dir: SortDir, getTime: (item: T) => number = recordTime): T[] {
  const copy = [...items];
  copy.sort((a, b) => {
    const diff = getTime(a) - getTime(b);
    if (diff === 0) return 0;
    return dir === "latest" ? -diff : diff;
  });
  return copy;
}

export function usePaged<T>(items: T[], pageSize = PAGE_SIZE, resetKey = "") {
  const [page, setPage] = useState(1);
  const [sortDir, setSortDir] = useState<SortDir>("latest");
  const sorted = useMemo(() => sortRecords(items, sortDir), [items, sortDir]);
  const total = sorted.length;
  const pageCount = Math.max(1, Math.ceil(total / pageSize) || 1);

  useEffect(() => {
    setPage(1);
  }, [resetKey, pageSize, sortDir]);

  useEffect(() => {
    if (page > pageCount) setPage(pageCount);
  }, [page, pageCount]);

  const slice = useMemo(
    () => sorted.slice((page - 1) * pageSize, page * pageSize),
    [sorted, page, pageSize],
  );

  return { page, setPage, pageCount, total, pageSize, items: slice, sortDir, setSortDir };
}

export function pagerProps(paged: {
  page: number;
  setPage: (page: number) => void;
  pageCount: number;
  total: number;
  pageSize: number;
  sortDir: SortDir;
  setSortDir: (dir: SortDir) => void;
}) {
  return {
    page: paged.page,
    pageCount: paged.pageCount,
    total: paged.total,
    pageSize: paged.pageSize,
    onPageChange: paged.setPage,
    sortDir: paged.sortDir,
    onSortDirChange: paged.setSortDir,
  };
}
