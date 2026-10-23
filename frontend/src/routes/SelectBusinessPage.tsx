import { useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import * as authApi from "@/api/authApi";
import { ApiError } from "@/api/client";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/hooks/useAuth";
import { displayRole } from "@/lib/utils";
import type { MembershipOption } from "@/types/auth";

const DEFAULT_ERROR_MESSAGE = "Something went wrong. Please try again.";

interface LocationState {
  preAuthToken?: string;
  memberships?: MembershipOption[];
}

function extractErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const body = error.body as { title?: string } | null | undefined;
    return body?.title ?? DEFAULT_ERROR_MESSAGE;
  }
  return error instanceof Error ? error.message : DEFAULT_ERROR_MESSAGE;
}

/**
 * Shown when `login()` resolves with `requiresBusinessSelection: true` — reached only via
 * `LoginPage`'s redirect, which hands off the preauth token + membership list as router state
 * (never persisted to context/localStorage, since both are single-use/transient — see
 * architecture.md's "Business-selector and switcher UI" paragraph). If someone lands here
 * directly (page refresh, back button, bookmarked URL) that state is gone, so there's nothing
 * usable to complete the login with — bounce back to `/login` rather than rendering a dead end.
 */
export function SelectBusinessPage() {
  const { completeBusinessSelection } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state = location.state as LocationState | null;

  const [pageError, setPageError] = useState<string | null>(null);
  const [selectingId, setSelectingId] = useState<string | null>(null);

  if (!state?.preAuthToken || !state.memberships || state.memberships.length === 0) {
    return <Navigate to="/login" replace />;
  }

  const { preAuthToken, memberships } = state;

  const handleSelect = async (businessId: string) => {
    setPageError(null);
    setSelectingId(businessId);
    try {
      const result = await authApi.selectBusiness(preAuthToken, businessId);
      completeBusinessSelection(result);
      navigate("/dashboard", { replace: true });
    } catch (error) {
      setPageError(extractErrorMessage(error));
      setSelectingId(null);
    }
  };

  return (
    <div className="flex flex-1 items-center justify-center bg-background text-foreground">
      <div className="w-full max-w-sm space-y-6">
        <div className="space-y-2 text-center">
          <h1 className="text-2xl font-semibold">Choose a business</h1>
          <p className="text-sm text-muted-foreground">
            Your email is associated with more than one business. Select one to continue.
          </p>
        </div>

        {pageError && (
          <Alert variant="destructive">
            <AlertTitle>Unable to continue</AlertTitle>
            <AlertDescription>{pageError}</AlertDescription>
          </Alert>
        )}

        <div className="space-y-3 md:space-y-2">
          {memberships.map((membership) => (
            <Button
              key={membership.businessId}
              type="button"
              variant="outline"
              className="h-11 w-full justify-between md:h-8"
              disabled={selectingId !== null}
              onClick={() => handleSelect(membership.businessId)}
            >
              <span>{membership.businessName}</span>
              <span className="text-xs text-muted-foreground">
                {selectingId === membership.businessId ? "Continuing…" : displayRole(membership.role)}
              </span>
            </Button>
          ))}
        </div>
      </div>
    </div>
  );
}
