import { useMsal } from "@azure/msal-react";

export default function NavBar() {
  const { instance, accounts } = useMsal();
  const account = accounts[0];

  const handleLogout = () => {
    instance.logoutPopup({ mainWindowRedirectUri: "/" });
  };

  return (
    <nav className="navbar">
      <div className="navbar-brand">
        <svg width="32" height="32" viewBox="0 0 32 32" fill="none" aria-hidden="true">
          <rect width="32" height="32" rx="8" fill="#0078d4" />
          <path d="M8 24 L16 8 L24 24" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
          <circle cx="16" cy="18" r="3" fill="white" />
        </svg>
        <span className="navbar-title">AgentifFlow</span>
      </div>
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
