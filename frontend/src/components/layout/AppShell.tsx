import { useState } from "react";
import type { ReactNode } from "react";
import { NavLink, useNavigate } from "react-router-dom";
import * as authApi from "@/api/authApi";
import { AddBusinessForm } from "@/components/auth/AddBusinessForm";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { cn, displayRole } from "@/lib/utils";
import { useAuth } from "@/hooks/useAuth";
import { useMembershipsQuery } from "@/hooks/useMemberships";

// `ownerOnly` entries (currently just Users — requirements.md's Permissions section: Staff has
// "no user management," which the Users screen itself is folded into) are filtered out below for
// any non-Owner. Checked against the *internal* role value ("Owner"), never the display label
// ("Admin" per `displayRole` in lib/utils.ts). This only hides discovery of the nav link — the
// actual access guard lives in App.tsx's `RequireRole` wrapper around the `/users` route.
const navItems = [
  { to: "/dashboard", label: "Dashboard" },
  { to: "/clients", label: "Clients" },
  { to: "/invoices", label: "Invoices" },
  { to: "/notifications", label: "Notifications" },
  { to: "/users", label: "Users", ownerOnly: true },
];

const DEFAULT_SWITCH_ERROR = "Unable to switch business. Please try again.";

/**
 * "Switch business"/"Add a business" affordance (architecture.md's "Business-selector and
 * switcher UI" paragraph): an authenticated person whose identity holds more than one active
 * membership can switch their active session without re-entering their password.
 *
 * The dropdown is controlled (`open`/`onOpenChange`) so `useMembershipsQuery`'s `enabled` can be
 * tied to it — `GET /api/auth/memberships` is only ever called when this is actually opened, not
 * on every page load, matching architecture.md's "fetched lazily, only when opened" instruction.
 */
function BusinessSwitcher() {
  const { user, completeBusinessSelection } = useAuth();
  const navigate = useNavigate();

  const [isSwitcherOpen, setIsSwitcherOpen] = useState(false);
  const [isAddBusinessOpen, setIsAddBusinessOpen] = useState(false);
  const [switchError, setSwitchError] = useState<string | null>(null);
  const [switchingId, setSwitchingId] = useState<string | null>(null);

  const membershipsQuery = useMembershipsQuery(isSwitcherOpen);
  const otherMemberships = (membershipsQuery.data ?? []).filter(
    (membership) => membership.businessId !== user?.businessId,
  );

  const handleSwitch = async (businessId: string) => {
    setSwitchError(null);
    setSwitchingId(businessId);
    try {
      const result = await authApi.switchBusiness(businessId);
      completeBusinessSelection(result);
      navigate("/dashboard");
    } catch {
      setSwitchError(DEFAULT_SWITCH_ERROR);
    } finally {
      setSwitchingId(null);
    }
  };

  return (
    <>
      {switchError && (
        <Alert variant="destructive" className="absolute left-0 right-0 top-full mt-2">
          <AlertTitle>Unable to switch business</AlertTitle>
          <AlertDescription>{switchError}</AlertDescription>
        </Alert>
      )}

      <DropdownMenu open={isSwitcherOpen} onOpenChange={setIsSwitcherOpen}>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm">
            Switch business
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-64">
          <DropdownMenuLabel>Your businesses</DropdownMenuLabel>
          {membershipsQuery.isLoading && (
            <p className="px-2 py-1.5 text-xs text-muted-foreground">Loading…</p>
          )}
          {membershipsQuery.isError && (
            <p className="px-2 py-1.5 text-xs text-destructive">
              Unable to load your other businesses. Please try again.
            </p>
          )}
          {membershipsQuery.isSuccess && otherMemberships.length === 0 && (
            <p className="px-2 py-1.5 text-xs text-muted-foreground">
              No other businesses found for this account.
            </p>
          )}
          {otherMemberships.map((membership) => (
            <DropdownMenuItem
              key={membership.businessId}
              disabled={switchingId === membership.businessId}
              onSelect={() => handleSwitch(membership.businessId)}
            >
              <span className="flex w-full items-center justify-between gap-2">
                <span>{membership.businessName}</span>
                <span className="text-xs text-muted-foreground">{displayRole(membership.role)}</span>
              </span>
            </DropdownMenuItem>
          ))}
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => setIsAddBusinessOpen(true)}>
            Add a business
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={isAddBusinessOpen} onOpenChange={setIsAddBusinessOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Add a business</DialogTitle>
          </DialogHeader>
          <AddBusinessForm
            onCancel={() => setIsAddBusinessOpen(false)}
            onSubmit={async (values) => {
              const result = await authApi.addBusiness(values.businessName, values.businessEmail);
              completeBusinessSelection(result);
              setIsAddBusinessOpen(false);
              navigate("/dashboard");
            }}
          />
        </DialogContent>
      </Dialog>
    </>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();
  const visibleNavItems = navItems.filter((item) => !item.ownerOnly || user?.role === "Owner");

  return (
    <div className="flex min-h-screen flex-col bg-background text-foreground">
      <header className="relative border-b border-border">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
          <span className="text-lg font-semibold">Eternity Invoicing</span>
          <nav className="flex items-center gap-4">
            {visibleNavItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) =>
                  cn(
                    "rounded-md px-2.5 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground",
                    isActive && "bg-accent-subtle text-primary hover:text-primary",
                  )
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>
          <div className="flex items-center gap-3">
            {user && <span className="text-sm text-muted-foreground">{user.email}</span>}
            <BusinessSwitcher />
            <Button variant="outline" size="sm" onClick={logout}>
              Log out
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-6 py-8">{children}</main>
    </div>
  );
}
