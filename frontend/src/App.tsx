import type { ReactNode } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/components/layout/AppShell";
import { ProtectedRoute } from "@/components/layout/ProtectedRoute";
import { ClientDetailPage } from "@/routes/ClientDetailPage";
import { ClientsListPage } from "@/routes/ClientsListPage";
import { DashboardPage } from "@/routes/DashboardPage";
import { ForgotPasswordPage } from "@/routes/ForgotPasswordPage";
import { InvoiceDetailPage } from "@/routes/InvoiceDetailPage";
import { InvoiceFormPage } from "@/routes/InvoiceFormPage";
import { InvoicesListPage } from "@/routes/InvoicesListPage";
import { LoginPage } from "@/routes/LoginPage";
import { NotificationsPage } from "@/routes/NotificationsPage";
import { RegisterPage } from "@/routes/RegisterPage";
import { ResetPasswordPage } from "@/routes/ResetPasswordPage";
import { UsersPage } from "@/routes/UsersPage";

function Protected({ children }: { children: ReactNode }) {
  return (
    <ProtectedRoute>
      <AppShell>{children}</AppShell>
    </ProtectedRoute>
  );
}

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />

      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route
        path="/dashboard"
        element={
          <Protected>
            <DashboardPage />
          </Protected>
        }
      />
      <Route
        path="/clients"
        element={
          <Protected>
            <ClientsListPage />
          </Protected>
        }
      />
      <Route
        path="/clients/:clientId"
        element={
          <Protected>
            <ClientDetailPage />
          </Protected>
        }
      />
      <Route
        path="/invoices"
        element={
          <Protected>
            <InvoicesListPage />
          </Protected>
        }
      />
      <Route
        path="/invoices/new"
        element={
          <Protected>
            <InvoiceFormPage />
          </Protected>
        }
      />
      <Route
        path="/invoices/:invoiceId"
        element={
          <Protected>
            <InvoiceDetailPage />
          </Protected>
        }
      />
      <Route
        path="/invoices/:invoiceId/edit"
        element={
          <Protected>
            <InvoiceFormPage />
          </Protected>
        }
      />
      <Route
        path="/notifications"
        element={
          <Protected>
            <NotificationsPage />
          </Protected>
        }
      />
      <Route
        path="/users"
        element={
          <Protected>
            <UsersPage />
          </Protected>
        }
      />

      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}

export default App;
