import { useMsal } from "@azure/msal-react";
import { loginRequest } from "../authConfig";

const features = [
  { icon: "📧", text: "Microsoft 365 Mail via Graph API" },
  { icon: "🤖", text: "Azure OpenAI LLM integration" },
  { icon: "☁️", text: "Azure Blob Storage monitoring" },
  { icon: "🗄️", text: "SQL Database persistence" },
] as const;

export default function LoginPage() {
  const { instance } = useMsal();

  const handleLogin = async () => {
    await instance.loginPopup(loginRequest);
  };

  return (
    <div className="login-page">

      {/* ── Left Hero Panel ─────────────────────────────────────────────── */}
      <div className="login-hero">
        <div className="hero-orb hero-orb-1" aria-hidden="true" />
        <div className="hero-orb hero-orb-2" aria-hidden="true" />

        {/* Logo mark */}
        <div className="hero-logo">
          <svg width="56" height="56" viewBox="0 0 56 56" fill="none" aria-hidden="true">
            <rect width="56" height="56" rx="15"
              fill="rgba(255,255,255,0.10)"
              stroke="rgba(255,255,255,0.18)" strokeWidth="1" />
            <path d="M14 42 L28 13 L42 42"
              stroke="white" strokeWidth="3.5"
              strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="28" cy="33" r="5.5" fill="white" />
          </svg>
        </div>

        <h1 className="hero-product-name">AgentifFlow</h1>
        <p className="hero-tagline">
          AI-powered workflow automation<br />
          with Azure integration
        </p>

        <div className="hero-features">
          {features.map(({ icon, text }) => (
            <div key={text} className="hero-feature-item">
              <span className="hero-feature-icon" aria-hidden="true">{icon}</span>
              <span>{text}</span>
            </div>
          ))}
        </div>

        <p className="hero-version">Enterprise Edition</p>
      </div>

      {/* ── Right Form Panel ──────────────────────────────────────────────── */}
      <div className="login-panel">
        <div className="login-form-wrap">

          {/* Mobile-only logo — hidden on desktop where the hero panel shows */}
          <div className="login-mobile-logo" aria-hidden="true">
            <svg width="44" height="44" viewBox="0 0 44 44" fill="none">
              <rect width="44" height="44" rx="12" fill="#4F46E5" />
              <path d="M11 33 L22 10 L33 33"
                stroke="white" strokeWidth="3"
                strokeLinecap="round" strokeLinejoin="round" />
              <circle cx="22" cy="25" r="4.5" fill="white" />
            </svg>
          </div>

          <h2 className="login-form-title">Welcome back</h2>
          <p className="login-form-subtitle">
            Sign in with your Microsoft account to access the platform
          </p>

          <button className="btn-ms-login" onClick={handleLogin}>
            {/* Official Microsoft four-square logo */}
            <svg width="21" height="21" viewBox="0 0 21 21" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
              <rect x="1"  y="1"  width="9" height="9" fill="#f25022" />
              <rect x="11" y="1"  width="9" height="9" fill="#7fba00" />
              <rect x="1"  y="11" width="9" height="9" fill="#00a4ef" />
              <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
            </svg>
            <span>Sign in with Microsoft</span>
          </button>

          <p className="login-secure-note">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4z" />
            </svg>
            Secured by Azure Active Directory OAuth 2.0
          </p>

        </div>
      </div>

    </div>
  );
}
