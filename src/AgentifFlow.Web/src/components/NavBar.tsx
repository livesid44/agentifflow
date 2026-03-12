import { useMsal } from "@azure/msal-react";
import { useLocation, Link } from "react-router-dom";

export default function NavBar() {
  const { instance, accounts } = useMsal();
  const account = accounts[0];
  const { pathname } = useLocation();

  const handleLogout = () => {
    instance.logoutPopup({ mainWindowRedirectUri: "/" });
  };

  const navLinks = [
    { to: "/dashboard",            label: "Dashboard" },
    { to: "/configuration",        label: "Integration Settings" },
    { to: "/agent-configuration",  label: "Agent Configuration" },
  ];

  return (
    <nav className="navbar">
      {/* ── Brand: logo mark + wordmark + divider + marketplace subtitle ── */}
      <div className="navbar-brand">
        <svg width="30" height="30" viewBox="0 0 36 36" fill="none" aria-hidden="true">
          <rect width="36" height="36" rx="6" fill="#E31937" />
          <path d="M9 27 L18 8 L27 27"
            stroke="white" strokeWidth="2.5"
            strokeLinecap="round" strokeLinejoin="round" />
          <circle cx="18" cy="21" r="3.5" fill="white" />
        </svg>
        <span className="navbar-title">AgentifFlow</span>
        <span className="navbar-brand-divider" aria-hidden="true" />
        <span className="navbar-subtitle">Tech M Orion Marketplace</span>
      </div>

      {/* ── Nav pills (TechM glass pill bar) ── */}
      {account && (
        <div className="navbar-links">
          {navLinks.map(({ to, label }) => (
            <Link
              key={to}
              to={to}
              className={`navbar-link${pathname === to ? " navbar-link-active" : ""}`}
            >
              {label}
            </Link>
          ))}
        </div>
      )}

      {/* ── User zone ── */}
      <div className="navbar-user">
        {account && (
          <>
            <span className="user-name">{account.name ?? account.username}</span>
            <button className="btn btn-outline btn-sm" onClick={handleLogout}>
              Sign out
            </button>
          </>
        )}
      </div>
    </nav>
  );
}
