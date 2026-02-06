"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch } from "@/lib/api";
import { setToken } from "@/lib/auth";

type LoginResponse = {
  accessToken?: string;
  token?: string;
};

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState("admin@inventorytracker.com");
  const [password, setPassword] = useState("Admin@12345");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const res = await apiFetch<LoginResponse>("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ email, password }),
      });

      const token = res.accessToken ?? res.token;
      if (!token) throw new Error("No token returned");

      setToken(token);
      router.replace("/balances");
    } catch (err: any) {
      setError(err.message ?? "Login failed");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <form
        onSubmit={onSubmit}
        className="w-full max-w-sm border rounded-lg p-6 space-y-4"
      >
        <h1 className="text-xl font-semibold">InventoryTracker Login</h1>

        <div>
          <label>Email</label>
          <input
            className="w-full border rounded px-3 py-2"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </div>

        <div>
          <label>Password</label>
          <input
            className="w-full border rounded px-3 py-2"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </div>

        {error && <p className="text-red-600 text-sm">{error}</p>}

        <button
          className="w-full bg-black text-white rounded px-3 py-2 disabled:opacity-50"
          disabled={loading}
        >
          {loading ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}
