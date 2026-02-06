"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { getToken } from "@/lib/auth";

export default function AuthGuard({
  children,
}: {
  children: React.ReactNode;
}) {
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    const token = getToken();
    if (!token && pathname !== "/login") {
      router.replace("/login");
    }
  }, [router, pathname]);

  return <>{children}</>;
}
