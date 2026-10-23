import { MenuIcon, UserCircleIcon } from "lucide-react";
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
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { cn, displayRole } from "@/lib/utils";
import { useAuth } from "@/hooks/useAuth";
import { useIsMobile } from "@/hooks/useIsMobile";
import { useMembershipsQuery } from "@/hooks/useMemberships";
import type { AddBusinessFormValues } from "@/schemas/authSchemas";

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
  { to: "/settings", label: "Settings" },
];

const DEFAULT_SWITCH_ERROR = "Unable to switch business. Please try again.";

/**
 * Shared state/logic behind the "Switch business"/"Add a business" affordance
 * (architecture.md's "Business-selector and switcher UI" paragraph): an authenticated person
 * whose identity holds more than one active membership can switch their active session without
 * re-entering their password. Extracted into a hook so the desktop header's "Account" dropdown
 * (which nests this as a submenu) and the mobile drawer's standalone "Switch business" button
 * don't each duplicate the fetch/switch/add-business logic.
 *
 * `isOpen`/`setIsOpen` is meant to be wired to whichever dropdown/submenu actually displays the
 * memberships list, so `useMembershipsQuery`'s `enabled` stays tied to it — `GET
 * /api/auth/memberships` is only ever called once that UI is actually opened, not on every page
 * load, matching architecture.md's "fetched lazily, only when opened" instruction.
 */
function useBusinessSwitcher(onAfterAction?: () => void) {
  const { user, completeBusinessSelection } = useAuth();
  const navigate = useNavigate();

  const [isOpen, setIsOpen] = useState(false);
  const [isAddBusinessOpen, setIsAddBusinessOpen] = useState(false);
  const [switchError, setSwitchError] = useState<string | null>(null);
  const [switchingId, setSwitchingId] = useState<string | null>(null);

  const membershipsQuery = useMembershipsQuery(isOpen);
  const otherMemberships = (membershipsQuery.data ?? []).filter(
    (membership) => membership.businessId !== user?.businessId,
  );

  const handleSwitch = async (businessId: string) => {
    setSwitchError(null);
    setSwitchingId(businessId);
    try {
      const result = await authApi.switchBusiness(businessId);
      completeBusinessSelection(result);
      onAfterAction?.();
      navigate("/dashboard");
    } catch {
      setSwitchError(DEFAULT_SWITCH_ERROR);
    } finally {
      setSwitchingId(null);
    }
  };

  const handleAddBusiness = async (values: AddBusinessFormValues) => {
    const result = await authApi.addBusiness(values.businessName, values.businessEmail);
    completeBusinessSelection(result);
    setIsAddBusinessOpen(false);
    onAfterAction?.();
    navigate("/dashboard");
  };

  return {
    isOpen,
    setIsOpen,
    isAddBusinessOpen,
    setIsAddBusinessOpen,
    switchError,
    switchingId,
    membershipsQuery,
    otherMemberships,
    handleSwitch,
    handleAddBusiness,
  };
}

type BusinessSwitcherState = ReturnType<typeof useBusinessSwitcher>;

/**
 * The menu contents shared by both the desktop "Account" dropdown's "Switch business" submenu
 * and the mobile drawer's standalone switcher dropdown: loading/error/empty states, the list of
 * other active memberships, and the "Add a business" entry point. Renders the same whether its
 * parent is a top-level `DropdownMenuContent` or a nested `DropdownMenuSubContent` — both accept
 * arbitrary `DropdownMenuItem`/`DropdownMenuLabel`/`DropdownMenuSeparator` children.
 */
function BusinessSwitcherMenuItems({ switcher }: { switcher: BusinessSwitcherState }) {
  const { membershipsQuery, otherMemberships, switchingId, handleSwitch, setIsAddBusinessOpen } = switcher;

  return (
    <>
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
    </>
  );
}

/** The "Add a business" dialog, shared the same way as `BusinessSwitcherMenuItems` above. */
function AddBusinessDialog({ switcher }: { switcher: BusinessSwitcherState }) {
  return (
    <Dialog open={switcher.isAddBusinessOpen} onOpenChange={switcher.setIsAddBusinessOpen}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add a business</DialogTitle>
        </DialogHeader>
        <AddBusinessForm onCancel={() => switcher.setIsAddBusinessOpen(false)} onSubmit={switcher.handleAddBusiness} />
      </DialogContent>
    </Dialog>
  );
}

/**
 * Mobile drawer's standalone "Switch business" button — its own top-level dropdown (as opposed
 * to the desktop header's nested submenu, see `AccountMenu` below), since the drawer's "busy
 * single row" problem (the thing this header-declutter effort was actually about) was specific to
 * the desktop header: the drawer already stacks email/"Switch business"/"Log out" on their own
 * full-width rows, which reads cleanly as-is.
 */
function BusinessSwitcher({
  triggerClassName,
  onAfterAction,
}: {
  /** Extra classes for the trigger button — the mobile drawer wants it full-width with a
   * 44px-minimum touch target. */
  triggerClassName?: string;
  /** Called after a successful switch or "Add a business" submit, right before navigating to the
   * dashboard — lets the mobile drawer close itself instead of staying open over the new page. */
  onAfterAction?: () => void;
}) {
  const switcher = useBusinessSwitcher(onAfterAction);

  return (
    <>
      {switcher.switchError && (
        <Alert variant="destructive" className="absolute left-0 right-0 top-full mt-2">
          <AlertTitle>Unable to switch business</AlertTitle>
          <AlertDescription>{switcher.switchError}</AlertDescription>
        </Alert>
      )}

      <DropdownMenu open={switcher.isOpen} onOpenChange={switcher.setIsOpen}>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" className={triggerClassName}>
            Switch business
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-64">
          <BusinessSwitcherMenuItems switcher={switcher} />
        </DropdownMenuContent>
      </DropdownMenu>

      <AddBusinessDialog switcher={switcher} />
    </>
  );
}

/**
 * Desktop header's compact "Account" dropdown — replaces the old inline email/"Switch
 * business"/"Log out" row, which looked busy with everything crammed into one line. Opening it
 * reveals the user's email as a non-interactive label (a `DropdownMenuLabel`, not a selectable
 * `DropdownMenuItem`), a "Switch business" submenu (nesting `BusinessSwitcherMenuItems` via
 * `DropdownMenuSub`/`DropdownMenuSubContent` so it reuses the exact same fetch/switch/add-business
 * logic as the mobile drawer's standalone button, rather than duplicating it), and "Log out".
 */
function AccountMenu() {
  const { user, logout } = useAuth();
  const switcher = useBusinessSwitcher();

  return (
    <>
      {switcher.switchError && (
        <Alert variant="destructive" className="absolute left-0 right-0 top-full mt-2">
          <AlertTitle>Unable to switch business</AlertTitle>
          <AlertDescription>{switcher.switchError}</AlertDescription>
        </Alert>
      )}

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          {/* `h-11` (44px) overrides `size="sm"`'s default `h-7` to meet this app's 44px-minimum
           * touch-target convention, per the explicit ask for this trigger — not otherwise applied
           * to every desktop control. No `aria-label` needed: the visible "Account" text is
           * already an adequate accessible name on its own, and the icon is purely decorative. */}
          <Button variant="outline" size="sm" className="h-11 gap-1.5 px-3">
            <UserCircleIcon aria-hidden="true" className="size-4" />
            Account
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-64">
          {user && (
            <DropdownMenuLabel className="truncate text-sm font-normal text-foreground">
              {user.email}
            </DropdownMenuLabel>
          )}
          <DropdownMenuSeparator />
          <DropdownMenuSub open={switcher.isOpen} onOpenChange={switcher.setIsOpen}>
            <DropdownMenuSubTrigger>Switch business</DropdownMenuSubTrigger>
            <DropdownMenuSubContent className="w-64">
              <BusinessSwitcherMenuItems switcher={switcher} />
            </DropdownMenuSubContent>
          </DropdownMenuSub>
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={logout}>Log out</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <AddBusinessDialog switcher={switcher} />
    </>
  );
}

/**
 * Collapsed nav for below the ~768px breakpoint (architecture.md's "Mobile responsiveness"
 * paragraph): a hamburger trigger opens a left-side drawer carrying everything the desktop
 * header shows today — nav links, the user's email, "Switch business", and "Log out" — sized for
 * a 44×44px-minimum touch target throughout. Nav-link visibility reuses the same `visibleNavItems`
 * the caller already computed, so the Owner-only "Users" filtering (item #16) applies identically
 * here, not just in the desktop nav.
 */
function MobileNav({ visibleNavItems }: { visibleNavItems: typeof navItems }) {
  const { user, logout } = useAuth();
  const [isOpen, setIsOpen] = useState(false);

  return (
    <Sheet open={isOpen} onOpenChange={setIsOpen}>
      <SheetTrigger asChild>
        <Button variant="outline" size="icon" aria-label="Open navigation menu" className="h-11 w-11">
          <MenuIcon className="size-5" />
        </Button>
      </SheetTrigger>
      <SheetContent side="left" className="flex w-4/5 max-w-xs flex-col gap-0 p-0">
        <SheetHeader className="border-b border-border">
          <SheetTitle>Eternity Invoicing</SheetTitle>
        </SheetHeader>
        <nav className="flex flex-1 flex-col gap-1 p-4">
          {visibleNavItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              onClick={() => setIsOpen(false)}
              className={({ isActive }) =>
                cn(
                  "flex min-h-11 items-center rounded-md px-3 text-base font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground",
                  isActive && "bg-accent-subtle text-primary hover:text-primary",
                )
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="flex flex-col gap-3 border-t border-border p-4">
          {user && <p className="truncate text-sm text-muted-foreground">{user.email}</p>}
          <BusinessSwitcher
            triggerClassName="min-h-11 w-full justify-center"
            onAfterAction={() => setIsOpen(false)}
          />
          <Button
            variant="outline"
            className="min-h-11 w-full justify-center"
            onClick={() => {
              setIsOpen(false);
              logout();
            }}
          >
            Log out
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const isMobile = useIsMobile();
  const visibleNavItems = navItems.filter((item) => !item.ownerOnly || user?.role === "Owner");

  return (
    <div className="flex flex-1 flex-col bg-background text-foreground">
      <header className="relative border-b border-border">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
          <span className="text-lg font-semibold">Eternity Invoicing</span>
          {isMobile ? (
            <MobileNav visibleNavItems={visibleNavItems} />
          ) : (
            <>
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
              <AccountMenu />
            </>
          )}
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-6 py-8">{children}</main>
    </div>
  );
}
