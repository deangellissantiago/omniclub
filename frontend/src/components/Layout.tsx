import type { ComponentType, ReactNode } from "react";
import { NavLink } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import {
  Avatar,
  BrandLockup,
  IconChart,
  IconCheckCircle,
  IconCreditCard,
  IconGrid,
  IconLogout,
  IconMapPin,
  IconUsers,
} from "./ui";

const NAV_ITEMS: { to: string; label: string; icon: ComponentType<{ size?: number }>; end?: boolean }[] = [
  { to: "/", label: "Dashboard", icon: IconGrid, end: true },
  { to: "/alunos", label: "Alunos", icon: IconUsers },
  { to: "/pontos-de-checkin", label: "Pontos de check-in", icon: IconMapPin },
  { to: "/checkins", label: "Check-ins", icon: IconCheckCircle },
  { to: "/relatorios", label: "Relatórios", icon: IconChart },
  { to: "/assinatura", label: "Assinatura", icon: IconCreditCard },
];

export function Layout({ children }: { children: ReactNode }) {
  const { auth, logout } = useAuth();

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <BrandLockup />
        </div>
        <nav>
          <p className="nav-section-label">Menu</p>
          {NAV_ITEMS.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => "nav-link" + (isActive ? " active" : "")}
            >
              <item.icon size={18} />
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-footer">
          <p className="integrations-label">Integrações</p>
          <div className="integration-chips">
            <span className="integration-chip"><span className="dot" />Wellhub</span>
            <span className="integration-chip soon"><span className="dot" />TotalPass</span>
          </div>
        </div>
      </aside>
      <div className="main-area">
        <header className="topbar">
          <span className="topbar-tenant">{auth?.tenantName}</span>
          <div className="topbar-right">
            <span className="topbar-user">
              <Avatar name={auth?.adminName} />
              {auth?.adminName}
            </span>
            <button className="btn-ghost" onClick={logout}>
              <IconLogout size={16} />
              Sair
            </button>
          </div>
        </header>
        <main className="content">{children}</main>
      </div>
    </div>
  );
}
