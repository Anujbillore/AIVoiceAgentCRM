import type { SortDir } from "../lib/pager";

export function SortSelect({
  value,
  onChange,
}: {
  value: SortDir;
  onChange: (dir: SortDir) => void;
}) {
  return (
    <label className="flex items-center gap-2 text-sm text-slate-600">
      <span className="whitespace-nowrap">Sort</span>
      <select
        className="w-auto py-1.5"
        value={value}
        onChange={(e) => onChange(e.target.value as SortDir)}
        aria-label="Sort records"
      >
        <option value="latest">Latest first</option>
        <option value="oldest">Oldest first</option>
      </select>
    </label>
  );
}

export function Pagination({
  page,
  pageCount,
  total,
  pageSize,
  onPageChange,
  sortDir,
  onSortDirChange,
}: {
  page: number;
  pageCount: number;
  total: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  sortDir?: SortDir;
  onSortDirChange?: (dir: SortDir) => void;
}) {
  if (total <= 0) return null;
  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-5 py-3">
      <p className="text-sm text-slate-500">
        Showing {from}–{to} of {total}
      </p>
      <div className="flex flex-wrap items-center gap-2">
        {sortDir && onSortDirChange ? <SortSelect value={sortDir} onChange={onSortDirChange} /> : null}
        <button type="button" className="btn-ghost" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Previous
        </button>
        <span className="text-sm text-slate-600">
          Page {page} of {pageCount}
        </span>
        <button type="button" className="btn-ghost" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>
          Next
        </button>
      </div>
    </div>
  );
}
