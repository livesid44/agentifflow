import { useMsal } from "@azure/msal-react";
import { loginRequest } from "../authConfig";

export default function LoginPage() {
  const { instance } = useMsal();

  const handleLogin = async () => {
    await instance.loginPopup(loginRequest);
  };

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-logo">
          <svg width="56" height="56" viewBox="0 0 56 56" fill="none" aria-hidden="true">
            <rect width="56" height="56" rx="14" fill="#0078d4" />
            <path
              d="M14 42 L28 14 L42 42"
              stroke="white"
              strokeWidth="4"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
            <circle cx="28" cy="32" r="5" fill="white" />
          </svg>
        </div>

        <h1 className="login-title">AgentifFlow</h1>
        <p className="login-subtitle">
          AI-powered workflow platform with Azure integration
        </p>

        <div className="login-features">
          <div className="login-feature">
            <span className="feature-icon">📧</span>
            <span>Microsoft 365 Mail via Graph API</span>
          </div>
          <div className="login-feature">
            <span className="feature-icon">🤖</span>
            <span>Azure OpenAI LLM integration</span>
          </div>
          <div className="login-feature">
            <span className="feature-icon">☁️</span>
            <span>Azure Blob Storage</span>
          </div>
          <div className="login-feature">
            <span className="feature-icon">🗄️</span>
            <span>SQL Database persistence</span>
          </div>
        </div>

        <button className="btn btn-primary btn-login" onClick={handleLogin}>
          <svg
            width="20"
            height="20"
            viewBox="0 0 21 21"
            xmlns="http://www.w3.org/2000/svg"
            aria-hidden="true"
          >
            <rect x="1" y="1" width="9" height="9" fill="#f25022" />
            <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
            <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
            <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
          </svg>
          Sign in with Microsoft
        </button>

        <p className="login-footer">
          Secured by Azure Active Directory OAuth 2.0
        </p>
      </div>
    </div>
  );
}
