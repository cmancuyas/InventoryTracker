const API_BASE = (process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:7026").replace(/\/+$/, "");

if (!API_BASE) throw new Error("NEXT_PUBLIC_API_BASE_URL is not defined");

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

function buildUrl(path: string) {
  // If caller accidentally passed an absolute URL, DO NOT prefix API_BASE
if (/^https?:\/\//i.test(path)) {
  throw new Error(`apiFetch: Do not pass absolute URLs. Use "/api/..." paths only. Got: ${path}`);
}

  // Ensure path starts with /
  const p = path.startsWith("/") ? path : `/${path}`;
  return `${API_BASE}${p}`;
}

export async function apiFetch<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = typeof window !== "undefined" ? localStorage.getItem("token") : null;

  const headers = new Headers(options.headers);
  if (!headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  // ✅ ngrok: bypass browser warning page (otherwise you get HTML)
  const base = (process.env.NEXT_PUBLIC_API_BASE_URL ?? "").toLowerCase();
  if (base.includes("ngrok-free.app")) {
    headers.set("ngrok-skip-browser-warning", "true");
  }

  const url = buildUrl(path);
  const res = await fetch(url, { ...options, headers });

  const ct = res.headers.get("content-type") || "";

  if (!res.ok) {
    const body = ct.includes("application/json") ? JSON.stringify(await res.json()) : await res.text();
    throw new ApiError(res.status, `URL=${res.url} :: ${body || res.statusText}`);
  }

  if (res.status === 204) return undefined as T;

  if (!ct.includes("application/json")) {
    const text = await res.text();
    throw new ApiError(
      500,
      `URL=${res.url} :: Expected JSON but got: ${ct}. Body starts with: ${text.slice(0, 80)}`
    );
  }

  return (await res.json()) as T;
}