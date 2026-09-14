import type { ReactNode, SVGProps } from "react";
import type { CheckinStatus, IntegrationApp } from "../api/types";

/* ---------- Logo ---------- */

export function Logo({ size = 34 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" aria-hidden="true">
      <defs>
        <linearGradient id="omni-g" x1="0" y1="0" x2="48" y2="48" gradientUnits="userSpaceOnUse">
          <stop stopColor="#3059F0" />
          <stop offset="1" stopColor="#381F59" />
        </linearGradient>
      </defs>
      <rect width="48" height="48" rx="12" fill="url(#omni-g)" />
      <circle cx="24" cy="24" r="12.5" fill="none" stroke="#FFFFFF" strokeWidth="3" opacity="0.9" />
      <path d="M18 24.5l4.4 4.4 8.6-9.4" fill="none" stroke="#FFFE78" strokeWidth="3.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function BrandLockup({ logoSize = 34, onLight = false }: { logoSize?: number; onLight?: boolean }) {
  return (
    <div className={`brand-lockup${onLight ? " on-light" : ""}`}>
      <Logo size={logoSize} />
      <div>
        <div className="brand-name">Omni<em>Club</em></div>
        <div className="brand-tagline">Automação de check-ins</div>
      </div>
    </div>
  );
}

/* ---------- Ícones (estilo lucide, stroke) ---------- */

type IconProps = SVGProps<SVGSVGElement> & { size?: number };

function Icon({ size = 18, children, ...rest }: IconProps & { children: ReactNode }) {
  return (
    <svg
      className="icon"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      {children}
    </svg>
  );
}

export const IconGrid = (p: IconProps) => (
  <Icon {...p}>
    <rect x="3" y="3" width="7" height="7" rx="1.5" />
    <rect x="14" y="3" width="7" height="7" rx="1.5" />
    <rect x="3" y="14" width="7" height="7" rx="1.5" />
    <rect x="14" y="14" width="7" height="7" rx="1.5" />
  </Icon>
);

export const IconUsers = (p: IconProps) => (
  <Icon {...p}>
    <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
    <circle cx="9" cy="7" r="4" />
    <path d="M22 21v-2a4 4 0 0 0-3-3.87" />
    <path d="M16 3.13a4 4 0 0 1 0 7.75" />
  </Icon>
);

export const IconMapPin = (p: IconProps) => (
  <Icon {...p}>
    <path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z" />
    <circle cx="12" cy="10" r="3" />
  </Icon>
);

export const IconCheckCircle = (p: IconProps) => (
  <Icon {...p}>
    <circle cx="12" cy="12" r="10" />
    <path d="m9 12 2 2 4-4" />
  </Icon>
);

export const IconChart = (p: IconProps) => (
  <Icon {...p}>
    <line x1="6" y1="20" x2="6" y2="14" />
    <line x1="12" y1="20" x2="12" y2="8" />
    <line x1="18" y1="20" x2="18" y2="4" />
  </Icon>
);

export const IconLogout = (p: IconProps) => (
  <Icon {...p}>
    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
    <polyline points="16 17 21 12 16 7" />
    <line x1="21" y1="12" x2="9" y2="12" />
  </Icon>
);

export const IconCreditCard = (p: IconProps) => (
  <Icon {...p}>
    <rect x="2" y="5" width="20" height="14" rx="2" />
    <line x1="2" y1="10" x2="22" y2="10" />
  </Icon>
);

export const IconInbox = (p: IconProps) => (
  <Icon {...p}>
    <polyline points="22 12 16 12 14 15 10 15 8 12 2 12" />
    <path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" />
  </Icon>
);

/* ---------- Avatar com iniciais ---------- */

export function Avatar({ name, small = false }: { name?: string | null; small?: boolean }) {
  const initials = (name ?? "?")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]!.toUpperCase())
    .join("") || "?";
  return <span className={`avatar${small ? " avatar-sm" : ""}`}>{initials}</span>;
}

/* ---------- Badges ---------- */

const STATUS_LABEL: Record<CheckinStatus, string> = {
  Approved: "Aprovado",
  Rejected: "Rejeitado",
  Pending: "Pendente",
};

export function StatusBadge({ status }: { status: CheckinStatus }) {
  return (
    <span className={`badge badge-${status.toLowerCase()}`}>
      <span className="dot" />
      {STATUS_LABEL[status] ?? status}
    </span>
  );
}

export function ActiveBadge({ active }: { active: boolean }) {
  return (
    <span className={`badge ${active ? "badge-approved" : "badge-rejected"}`}>
      <span className="dot" />
      {active ? "Ativo" : "Inativo"}
    </span>
  );
}

export function AppBadge({ app }: { app: IntegrationApp | string }) {
  return <span className={`badge badge-app-${String(app).toLowerCase()}`}>{app}</span>;
}

/* ---------- Estado vazio para tabelas ---------- */

export function TableEmpty({ colSpan, title, hint }: { colSpan: number; title: string; hint?: string }) {
  return (
    <tr>
      <td colSpan={colSpan} className="empty-row">
        <div className="empty-state">
          <div className="icon-ring"><IconInbox size={20} /></div>
          <strong>{title}</strong>
          {hint && <span>{hint}</span>}
        </div>
      </td>
    </tr>
  );
}
