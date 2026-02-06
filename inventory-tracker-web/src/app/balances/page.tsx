"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { clearToken } from "@/lib/auth";

/**
 * Fixes:
 * 1) Calls backend via NEXT_PUBLIC_API_BASE_URL when present (avoids Next.js /api returning HTML)
 * 2) Supports backend paging shape: { items, page, pageSize, total } (matches your controllers)
 * 3) Works with warehouses that include { id, code, name }
 * 4) Better error when response is HTML (text/html) so you see proxy/baseUrl issues immediately.
 */

type Warehouse = {
  id: string;
  code?: string;
  name: string;
  location?: string;
  isActive?: boolean;
};

type BalanceRow = {
  warehouseId: string;
  productId: string;
  sku: string;
  name: string;
  unitOfMeasure?: string; // backend might send this
  uom?: string;          // or this
  onHand: number;
  available: number;
};

type BackendPaged<T> = {
  items: T[];
  page: number;
  pageSize: number;
  total: number; // your API uses Total
};

type UiPaged<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

type NormalizedPaged<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};


function normalizePaged<T>(raw: any): NormalizedPaged<T> {
  // backend style: { items, page, pageSize, total }
  if (raw && Array.isArray(raw.items) && typeof raw.total === "number") {
    const page = Number(raw.page ?? 1) || 1;
    const pageSize = Number(raw.pageSize ?? 20) || 20;
    const totalCount = raw.total;
    const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
    return { items: raw.items, page, pageSize, totalCount, totalPages };
  }

  // UI style: { items, page, pageSize, totalCount, totalPages }
  if (raw && Array.isArray(raw.items) && typeof raw.totalCount === "number") {
    const page = Number(raw.page ?? 1) || 1;
    const pageSize = Number(raw.pageSize ?? 20) || 20;
    const totalCount = raw.totalCount;
    const totalPages = Number(raw.totalPages ?? Math.max(1, Math.ceil(totalCount / pageSize))) || 1;
    return { items: raw.items, page, pageSize, totalCount, totalPages };
  }

  // non-paged array fallback
  if (Array.isArray(raw)) {
    const items = raw;
    return { items, page: 1, pageSize: items.length || 25, totalCount: items.length, totalPages: 1 };
  }

  // unknown
  return { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 1 };
}

export default function BalancesPage() {
  const router = useRouter();

  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehousesLoading, setWarehousesLoading] = useState(false);
  const [warehousesError, setWarehousesError] = useState<string | null>(null);

  const [warehouseId, setWarehouseId] = useState("");

  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState(search);
  const searchTimerRef = useRef<number | null>(null);

  const [sortBy, setSortBy] = useState("sku");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const [data, setData] = useState<NormalizedPaged<BalanceRow> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (searchTimerRef.current) window.clearTimeout(searchTimerRef.current);
    searchTimerRef.current = window.setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 350);

    return () => {
      if (searchTimerRef.current) window.clearTimeout(searchTimerRef.current);
    };
  }, [search]);

  function logout() {
    clearToken();
    router.replace("/login");
  }

  // Load warehouses
  useEffect(() => {
    let mounted = true;

    async function loadWarehouses() {
      setWarehousesLoading(true);
      setWarehousesError(null);

      try {
        const res = await apiFetch<Warehouse[]>(("/api/warehouses"), { method: "GET" });
        if (!mounted) return;

        const list = Array.isArray(res) ? res : [];
        setWarehouses(list);

        // auto-select first warehouse (helps avoid "blank because nothing selected")
        if (!warehouseId && list.length > 0) {
          setWarehouseId(list[0].id);
        }
      } catch (e: any) {
        if (!mounted) return;

        if (e instanceof ApiError && e.status === 401) {
          clearToken();
          router.replace("/login");
          return;
        }

        setWarehousesError(e?.message ?? "Failed to load warehouses");
      } finally {
        if (mounted) setWarehousesLoading(false);
      }
    }

    loadWarehouses();
    return () => {
      mounted = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const queryString = useMemo(() => {
    const qs = new URLSearchParams();
    qs.set("warehouseId", warehouseId);

    const s = debouncedSearch.trim();
    if (s) qs.set("search", s);

    qs.set("sortBy", sortBy);
    qs.set("sortDir", sortDir);
    qs.set("page", String(page));
    qs.set("pageSize", String(pageSize));
    return qs.toString();
  }, [warehouseId, debouncedSearch, sortBy, sortDir, page, pageSize]);

  async function loadBalances() {
    if (!warehouseId) return;

    setLoading(true);
    setError(null);

    try {
        
        const raw = await apiFetch<any>(`/api/inventory-balances?${queryString}`, { method: "GET" });


      const normalized = normalizePaged<BalanceRow>(raw);

      // Normalize row shape too (unitOfMeasure/uom)
      normalized.items = normalized.items.map((r) => ({
        ...r,
        uom: r.uom ?? r.unitOfMeasure ?? "",
        onHand: Number(r.onHand ?? 0),
        available: Number(r.available ?? 0),
      }));

      setData(normalized);
    } catch (e: any) {
      if (e instanceof ApiError && e.status === 401) {
        clearToken();
        router.replace("/login");
        return;
      }

      // This is the exact error you’re seeing when HTML is returned.
      setError(e?.message ?? "Failed to load balances");
      setData(null);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (!warehouseId) {
      setData(null);
      setError(null);
      return;
    }
    loadBalances();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [queryString, warehouseId]);

  function toggleSort(next: string) {
    setPage(1);
    if (sortBy === next) setSortDir(sortDir === "asc" ? "desc" : "asc");
    else {
      setSortBy(next);
      setSortDir("asc");
    }
  }

  const canPrev = !!data && data.page > 1 && !loading;
  const canNext = !!data && data.page < data.totalPages && !loading;

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Inventory Balances</h1>
        <button className="border rounded px-3 py-2" onClick={logout}>
          Logout
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-6 gap-3">
        <div className="md:col-span-2">
          <label className="text-sm">Warehouse</label>
            <select
            className="
                w-full
                bg-black
                border border-gray-600
                text-gray-200
                rounded px-3 py-2
                focus:outline-none
                focus:ring-2 focus:ring-blue-500
                focus:border-blue-500
                disabled:opacity-50
                disabled:cursor-not-allowed
            "
            value={warehouseId}
            onChange={(e) => {
                setWarehouseId(e.target.value);
                setPage(1);
            }}
            disabled={warehousesLoading}
            >

            <option value="">Select warehouse…</option>
            {warehouses.map((w) => (
              <option key={w.id} value={w.id}>
                {w.code ? `${w.code} — ${w.name}` : w.name}
              </option>
            ))}
          </select>

          {warehousesLoading && <p className="text-xs text-gray-500 mt-1">Loading warehouses…</p>}
          {warehousesError && <p className="text-xs text-red-600 mt-1">{warehousesError}</p>}
        </div>

        <div className="md:col-span-2">
          <label className="text-sm">Search</label>
          <input
            className="w-full border rounded px-3 py-2"
            placeholder="SKU / name"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            disabled={!warehouseId}
          />
        </div>

        <div>
          <label className="text-sm">Page size</label>
            <select
            className="
                w-full
                bg-black
                border border-gray-600
                text-gray-200
                rounded px-3 py-2
                focus:outline-none
                focus:ring-2 focus:ring-blue-500
                focus:border-blue-500
                disabled:opacity-50
                disabled:cursor-not-allowed
            "
            value={pageSize}
            onChange={(e) => {
                setPageSize(Number(e.target.value));
                setPage(1);
            }}
            disabled={!warehouseId}
            >
            {[10, 25, 50, 100].map((n) => (
                <option key={n} value={n} className="bg-black text-gray-200">
                {n}
                </option>
            ))}
            </select>

        </div>

        <div className="flex items-end gap-2">
          <button
            className="border rounded px-3 py-2 w-full"
            onClick={() => {
              setSearch("");
              setDebouncedSearch("");
              setSortBy("sku");
              setSortDir("asc");
              setPage(1);
            }}
            disabled={!warehouseId}
          >
            Reset
          </button>

          <button
            className="border rounded px-3 py-2 w-full"
            onClick={loadBalances}
            disabled={!warehouseId || loading}
            title="Force reload"
          >
            Reload
          </button>
        </div>
      </div>

      {!warehouseId && (
        <div className="border rounded p-3 bg-gray-50 text-gray-700">
          Select a warehouse to load balances.
        </div>
      )}

      {error && (
        <div className="border border-red-300 bg-red-50 text-red-700 rounded p-3">
          {error}
          <div className="text-xs mt-2 opacity-80">
            Tip: If you see “text/html” / “&lt;!DOCTYPE html&gt;”, your frontend is not hitting the .NET API.
            Set <code className="px-1">NEXT_PUBLIC_API_BASE_URL</code> to your ngrok URL and restart Next.
          </div>
        </div>
      )}

    <div className="border border-gray-700 rounded overflow-x-auto bg-black">
    <table className="min-w-full text-sm text-gray-200">
        <thead className="bg-gray-900 border-b border-gray-700">

            <tr>
              <Th onClick={() => toggleSort("sku")} active={sortBy === "sku"} dir={sortDir}>
                SKU
              </Th>
              <Th onClick={() => toggleSort("name")} active={sortBy === "name"} dir={sortDir}>
                Name
              </Th>
              <Th>UOM</Th>
              <Th onClick={() => toggleSort("onHand")} active={sortBy === "onHand"} dir={sortDir}>
                OnHand
              </Th>
              <Th onClick={() => toggleSort("available")} active={sortBy === "available"} dir={sortDir}>
                Available
              </Th>
            </tr>
          </thead>

          <tbody>
            {warehouseId && loading && (
              <tr>
                <td className="p-3" colSpan={5}>
                  Loading…
                </td>
              </tr>
            )}

            {warehouseId && !loading && data && data.items.length === 0 && (
              <tr>
                <td className="p-3" colSpan={5}>
                  No records.
                </td>
              </tr>
            )}

            {data?.items?.map((r) => (
              <tr key={`${r.warehouseId}-${r.productId}`} className="border-t">
                <td className="p-3 font-mono">{r.sku}</td>
                <td className="p-3">{r.name}</td>
                <td className="p-3">{r.uom ?? r.unitOfMeasure ?? ""}</td>
                <td className="p-3">{r.onHand}</td>
                <td className="p-3">{r.available}</td>
              </tr>
            ))}

            {!warehouseId && (
              <tr>
                <td className="p-3 text-gray-500" colSpan={5}>
                  —
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between">
        <div className="text-sm text-gray-200">
          {data ? (
            <>
              Page {data.page} of {data.totalPages} — {data.totalCount} total
            </>
          ) : (
            "—"
          )}
        </div>

        <div className="flex gap-2">
          <button
            className="border rounded px-3 py-2 disabled:opacity-50"
            disabled={!canPrev}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
          >
            Prev
          </button>
          <button
            className="border rounded px-3 py-2 disabled:opacity-50"
            disabled={!canNext}
            onClick={() => setPage((p) => p + 1)}
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}

function Th({
  children,
  onClick,
  active,
  dir,
}: {
  children: React.ReactNode;
  onClick?: () => void;
  active?: boolean;
  dir?: "asc" | "desc";
}) {
  const clickable = !!onClick;
  return (
    <th
      className={`text-left p-3 ${clickable ? "cursor-pointer select-none" : ""}`}
      onClick={onClick}
      title={clickable ? "Sort" : undefined}
    >
      <span className="inline-flex items-center gap-2">
        {children}
        {active && <span className="text-xs">{dir === "asc" ? "▲" : "▼"}</span>}
      </span>
    </th>
  );
}
