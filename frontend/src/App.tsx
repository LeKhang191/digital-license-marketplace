import { MyLicensesPage } from "./pages/MyLicensesPage";

import { Route, Routes } from "react-router-dom";

import { Navbar } from "./components/Navbar";

import { RoleGuard } from "./auth/RoleGuard";
import { Roles } from "./auth/roles";

import { HomePage } from "./pages/HomePage";
import { ProductPage } from "./pages/ProductPage";
import { OrdersPage } from "./pages/OrdersPage";
import { PaymentResultPage } from "./pages/PaymentResultPage";
import { AdminPage } from "./pages/AdminPage";
import { LicensePage } from "./pages/LicensePage";
import { TransactionsPage } from "./pages/TransactionsPage";
import { ForbiddenPage } from "./pages/ForbiddenPage";
import { NotFoundPage } from "./pages/NotFoundPage";

export default function App() {
  return (
    <>
      <Navbar />

      <Routes>
        {/* Public routes */}

        <Route
          path="/"
          element={<HomePage />}
        />

        <Route
          path="/products/:id"
          element={<ProductPage />}
        />

        {/* Customer routes */}

        <Route
          path="/my-licenses"
          element={
            <RoleGuard allowedRoles={[Roles.CUSTOMER]}>
              <MyLicensesPage />
            </RoleGuard>
          }
        />


        <Route
          path="/orders"
          element={
            <RoleGuard allowedRoles={[Roles.CUSTOMER]}>
              <OrdersPage />
            </RoleGuard>
          }
        />

        <Route
          path="/payment/result"
          element={
            <RoleGuard allowedRoles={[Roles.CUSTOMER]}>
              <PaymentResultPage />
            </RoleGuard>
          }
        />

        {/* Catalog Manager routes */}

        <Route
          path="/admin"
          element={
            <RoleGuard
              allowedRoles={[Roles.CATALOG_MANAGER]}
            >
              <AdminPage />
            </RoleGuard>
          }
        />

        {/* License Coordinator routes */}

        <Route
          path="/licenses"
          element={
            <RoleGuard
              allowedRoles={[Roles.LICENSE_COORDINATOR]}
            >
              <LicensePage key="inventory" view="inventory" />
            </RoleGuard>
          }
        />

        <Route path="/licenses/import" element={<RoleGuard allowedRoles={[Roles.LICENSE_COORDINATOR]}><LicensePage key="import" view="import" /></RoleGuard>} />
        <Route path="/licenses/suppliers" element={<RoleGuard allowedRoles={[Roles.LICENSE_COORDINATOR]}><LicensePage key="supplier" view="supplier" /></RoleGuard>} />

        {/* Transaction Auditor routes */}

        <Route
          path="/transactions"
          element={
            <RoleGuard
              allowedRoles={[Roles.TRANSACTION_AUDITOR]}
            >
              <TransactionsPage />
            </RoleGuard>
          }
        />

        {/* Error routes */}

        <Route
          path="/forbidden"
          element={<ForbiddenPage />}
        />

        <Route
          path="*"
          element={<NotFoundPage />}
        />
      </Routes>
    </>
  );
}
