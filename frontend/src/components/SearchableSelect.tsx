import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronDown } from "lucide-react";

export type SearchableOption = {
  id: number;
  label: string;
  search?: string;
};

export function SearchableSelect({
  label,
  value,
  onChange,
  options,
  placeholder = "Search and select",
  required,
}: {
  label?: string;
  value: number;
  onChange: (id: number) => void;
  options: SearchableOption[];
  placeholder?: string;
  required?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const root = useRef<HTMLDivElement>(null);
  const selected = options.find((option) => option.id === value);

  useEffect(() => {
    function close(event: MouseEvent) {
      if (!root.current?.contains(event.target as Node)) {
        setOpen(false);
        setQuery("");
      }
    }
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, []);

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return options;
    return options.filter((option) => (option.search ?? option.label).toLowerCase().includes(term));
  }, [options, query]);

  return (
    <div ref={root} className="relative">
      {label && <label>{label}</label>}
      <button
        type="button"
        className="flex w-full items-center justify-between rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-left text-sm text-slate-800 outline-none transition focus:border-teal-500 focus:ring-2 focus:ring-teal-100"
        onClick={() => {
          setOpen((current) => !current);
          setQuery("");
        }}
        aria-haspopup="listbox"
        aria-expanded={open}
      >
        <span className={selected ? "text-slate-800" : "text-slate-400"}>{selected?.label ?? placeholder}</span>
        <ChevronDown className={`h-4 w-4 text-slate-400 transition ${open ? "rotate-180" : ""}`} />
      </button>
      {required && <input className="sr-only" tabIndex={-1} value={value || ""} onChange={() => undefined} required />}
      {open && (
        <div className="absolute z-20 mt-1 w-full overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg">
          <input
            autoFocus
            className="rounded-none border-0 border-b border-slate-100"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Type to search..."
          />
          <ul className="max-h-56 overflow-y-auto" role="listbox">
            {filtered.length === 0 ? (
              <li className="px-3 py-2 text-sm text-slate-500">No matches.</li>
            ) : (
              filtered.map((option) => (
                <li key={option.id}>
                  <button
                    type="button"
                    className={`w-full px-3 py-2 text-left text-sm hover:bg-teal-50 ${option.id === value ? "bg-teal-50 font-medium text-teal-800" : "text-slate-700"}`}
                    onClick={() => {
                      onChange(option.id);
                      setOpen(false);
                      setQuery("");
                    }}
                  >
                    {option.label}
                  </button>
                </li>
              ))
            )}
          </ul>
        </div>
      )}
    </div>
  );
}
