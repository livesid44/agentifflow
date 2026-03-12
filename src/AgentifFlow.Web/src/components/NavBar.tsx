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
      <div className="navbar-brand">
        <svg width="32" height="32" viewBox="0 0 32 32" fill="none" aria-hidden="true">
          <rect width="32" height="32" rx="7" fill="#E31937" />
          <path d="M8 24 L16 8 L24 24"
            stroke="white" strokeWidth="2.5"
            strokeLinecap="round" strokeLinejoin="round" />
          <circle cx="16" cy="19" r="3" fill="white" />
        </svg>
        <span className="navbar-title">AgentifFlow</span>
      </div>

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
