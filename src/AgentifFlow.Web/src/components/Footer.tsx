export default function Footer() {
  return (
    <footer className="app-footer">
      <div className="app-footer-inner">
        <svg width="20" height="20" viewBox="0 0 36 36" fill="none" aria-hidden="true">
          <rect width="36" height="36" rx="6" fill="#E31937"/>
          <path d="M9 27 L18 8 L27 27" stroke="white" strokeWidth="2.5"
            strokeLinecap="round" strokeLinejoin="round"/>
          <circle cx="18" cy="21" r="3.5" fill="white"/>
        </svg>
        <span className="app-footer-text">
          Powered by <strong>Tech M Orion Marketplace</strong>
        </span>
        <span className="app-footer-divider" aria-hidden="true" />
        <span className="app-footer-copy">© 2026 AgentifFlow. All rights reserved.</span>
      </div>
    </footer>
  );
}
