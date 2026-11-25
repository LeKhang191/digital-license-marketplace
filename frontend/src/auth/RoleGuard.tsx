
import type { ReactNode } from "react";
import { Link, Navigate } from "react-router-dom";
import { useAuth } from "./AuthProvider";
import { Roles, type BusinessRole } from "./roles";

interface RoleGuardProps {
  allowedRoles: BusinessRole[];
  children: ReactNode;
}

export function RoleGuard({
  allowedRoles,
  children,
}: RoleGuardProps) {
  const {
    ready,
    authenticated,
    isCustomer,
    hasRole,
    login,
  } = useAuth();

  if (!ready) {
    return (
      <div className="container">
        <p className="muted">
          Đang kiểm tra quyền truy cập...
        </p>
      </div>
    );
  }

  if (!authenticated) {
    return (
      <div className="container">
        <div className="card">
          <h2>Yêu cầu đăng nhập</h2>

          <p>
            Vui lòng đăng nhập để truy cập trang này.
          </p>

          <div className="row">
            <button className="btn" onClick={login}>
              Đăng nhập
            </button>

            <Link to="/" className="btn btn-secondary">
              Quay về cửa hàng
            </Link>
          </div>
        </div>
      </div>
    );
  }

  const allowed = allowedRoles.some((role) =>
    role === Roles.CUSTOMER
      ? isCustomer
      : hasRole(role)
  );

  if (!allowed) {
    return <Navigate to="/forbidden" replace />;
  }

  return <>{children}</>;
}
