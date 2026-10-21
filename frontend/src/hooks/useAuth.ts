import { useContext } from "react";
import { AuthContext } from "@/context/AuthContext";

/** Thin wrapper around AuthContext — the one place components should read auth state from. */
export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
