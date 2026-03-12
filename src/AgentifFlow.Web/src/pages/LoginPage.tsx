import { useMsal } from "@azure/msal-react";
import { loginRequest } from "../authConfig";

/** AgentifFlow logo mark — matches the corner logo in the reference design */
function LogoMark({ size = 36 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 36 36" fill="none" aria-hidden="true">
      <rect width="36" height="36" rx="7" fill="#E31937" />
      <path d="M9 27 L18 8 L27 27"
        stroke="white" strokeWidth="2.5"
        strokeLinecap="round" strokeLinejoin="round" />
      <circle cx="18" cy="21" r="3.5" fill="white" />
    </svg>
  );
}

/** AI network / orbit visual for the dark left panel */
function AiNetworkSvg() {
  const ic = "rgba(255,255,255,0.68)";
  // 8 icon positions: clockwise from top, r=165, center=(250,250)
  const orbits: [number, number][] = [
    [250, 85],   // 0°  top
    [367, 133],  // 45° upper-right
    [415, 250],  // 90° right
    [367, 367],  // 135° lower-right
    [250, 415],  // 180° bottom
    [133, 367],  // 225° lower-left
    [85,  250],  // 270° left
    [133, 133],  // 315° upper-left
  ];
  const gearAngles = [0, 60, 120, 180, 240, 300];

  return (
    <svg
      viewBox="0 0 500 500"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      style={{ width: "min(440px, 90%)", height: "auto", maxHeight: "90vh" }}
      aria-hidden="true"
    >
      {/* ── Concentric orbit circles ── */}
      <circle cx="250" cy="250" r="60"  stroke="rgba(227,25,55,0.30)" strokeWidth="1.5" strokeDasharray="5 8"/>
      <circle cx="250" cy="250" r="112" stroke="rgba(255,255,255,0.09)" strokeWidth="1"   strokeDasharray="4 10"/>
      <circle cx="250" cy="250" r="165" stroke="rgba(255,255,255,0.07)" strokeWidth="1"   strokeDasharray="4 12"/>
      <circle cx="250" cy="250" r="222" stroke="rgba(255,255,255,0.04)" strokeWidth="0.8"/>

      {/* ── Spoke lines from centre to each icon ── */}
      {orbits.map(([x, y], i) => (
        <line key={i} x1="250" y1="250" x2={x} y2={y}
          stroke="rgba(255,255,255,0.07)" strokeWidth="1" />
      ))}

      {/* ── Icon circle backgrounds ── */}
      {orbits.map(([x, y], i) => (
        <circle key={i} cx={x} cy={y} r="30"
          fill="rgba(255,255,255,0.04)"
          stroke="rgba(255,255,255,0.18)" strokeWidth="1" />
      ))}

      {/* Lock — top (250, 85) */}
      <g transform="translate(250,85)">
        <rect x="-6.5" y="-2" width="13" height="10" rx="2" stroke={ic} strokeWidth="1.5" fill="none"/>
        <path d="M-4,-2 V-6 A4,4,0,0,1,4,-6 V-2" stroke={ic} strokeWidth="1.5" fill="none" strokeLinecap="round"/>
        <rect x="-1.5" y="1.5" width="3" height="3.5" rx="1" fill={ic}/>
      </g>

      {/* Chat — upper-right (367, 133) */}
      <g transform="translate(367,133)">
        <rect x="-9" y="-8" width="18" height="13" rx="3" stroke={ic} strokeWidth="1.5" fill="none"/>
        <circle cx="-4" cy="-1.5" r="1.5" fill={ic}/>
        <circle cx="0"  cy="-1.5" r="1.5" fill={ic}/>
        <circle cx="4"  cy="-1.5" r="1.5" fill={ic}/>
        <path d="M-6,5 L-9,9 L-0.5,5" stroke={ic} strokeWidth="1.5" fill="none" strokeLinejoin="round" strokeLinecap="round"/>
      </g>

      {/* Search — right (415, 250) */}
      <g transform="translate(415,250)">
        <circle cx="-2" cy="-2" r="7" stroke={ic} strokeWidth="1.5" fill="none"/>
        <line x1="3.5" y1="3.5" x2="9" y2="9" stroke={ic} strokeWidth="2" strokeLinecap="round"/>
      </g>

      {/* Gear — lower-right (367, 367) */}
      <g transform="translate(367,367)">
        <circle r="5"   stroke={ic} strokeWidth="1.5" fill="none"/>
        <circle r="8.5" stroke={ic} strokeWidth="1.5" fill="none" strokeDasharray="5.3 2.7"/>
      </g>

      {/* Network/Wifi — bottom (250, 415) */}
      <g transform="translate(250,415)">
        <path d="M-8,-4 A9.4,9.4,0,0,1,8,-4"     stroke={ic} strokeWidth="1.5" fill="none" strokeLinecap="round"/>
        <path d="M-5,-1 A5.9,5.9,0,0,1,5,-1"     stroke={ic} strokeWidth="1.5" fill="none" strokeLinecap="round"/>
        <path d="M-2.5,2 A3,3,0,0,1,2.5,2"       stroke={ic} strokeWidth="1.5" fill="none" strokeLinecap="round"/>
        <circle cy="6" r="2" fill={ic}/>
      </g>

      {/* Envelope — lower-left (133, 367) */}
      <g transform="translate(133,367)">
        <rect x="-9" y="-6" width="18" height="13" rx="1.5" stroke={ic} strokeWidth="1.5" fill="none"/>
        <path d="M-9,-6 L0,2 L9,-6" stroke={ic} strokeWidth="1.5" fill="none" strokeLinejoin="round"/>
      </g>

      {/* Globe — left (85, 250) */}
      <g transform="translate(85,250)">
        <circle r="9" stroke={ic} strokeWidth="1.5" fill="none"/>
        <ellipse rx="4.5" ry="9" stroke="rgba(255,255,255,0.45)" strokeWidth="1" fill="none"/>
        <line x1="-9" y1="0"    x2="9"  y2="0"    stroke="rgba(255,255,255,0.40)" strokeWidth="1"/>
        <line x1="-7.5" y1="-4.5" x2="7.5" y2="-4.5" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8"/>
        <line x1="-7.5" y1="4.5"  x2="7.5" y2="4.5"  stroke="rgba(255,255,255,0.25)" strokeWidth="0.8"/>
      </g>

      {/* Chip — upper-left (133, 133) */}
      <g transform="translate(133,133)">
        <rect x="-7" y="-7" width="14" height="14" rx="2" stroke={ic} strokeWidth="1.5" fill="none"/>
        <rect x="-4" y="-4" width="8"  height="8"  rx="1" stroke="rgba(255,255,255,0.45)" strokeWidth="1" fill="none"/>
        <line x1="-7"  y1="-3" x2="-11" y2="-3" stroke={ic} strokeWidth="1.5" strokeLinecap="round"/>
        <line x1="-7"  y1="0"  x2="-11" y2="0"  stroke={ic} strokeWidth="1.5" strokeLinecap="round"/>
        <line x1="-7"  y1="3"  x2="-11" y2="3"  stroke={ic} strokeWidth="1.5" strokeLinecap="round"/>
        <line x1="7"   y1="-3" x2="11"  y2="-3" stroke={ic} strokeWidth="1.5" strokeLinecap="round"/>
        <line x1="7"   y1="0"  x2="11"  y2="0"  stroke={ic} strokeWidth="1.5" strokeLinecap="round"/>
        <line x1="7"   y1="3"  x2="11"  y2="3"  stroke={ic} strokeWidth="1.5" strokeLinecap="round"/>
      </g>

      {/* ── Central AI element ── */}
      <circle cx="250" cy="250" r="45" fill="rgba(227,25,55,0.10)" stroke="rgba(227,25,55,0.35)" strokeWidth="1.5"/>
      <text x="250" y="256" textAnchor="middle" dominantBaseline="middle"
        fontSize="24" fontWeight="800" fill="rgba(255,255,255,0.92)"
        fontFamily="'Segoe UI',system-ui,sans-serif">AI</text>

      {/* Gear above central element */}
      <circle cx="250" cy="212" r="9" stroke="rgba(255,255,255,0.30)" strokeWidth="1.5" fill="none"/>
      <circle cx="250" cy="212" r="4" fill="rgba(255,255,255,0.15)"/>
      {gearAngles.map(a => (
        <rect key={a} x="-2" y="-13.5" width="4" height="4" rx="1"
          fill="rgba(255,255,255,0.35)"
          transform={`translate(250,212) rotate(${a})`} />
      ))}

      {/* Circuit lines below AI */}
      <line x1="250" y1="295" x2="250" y2="316" stroke="rgba(255,255,255,0.20)" strokeWidth="1.5" strokeLinecap="round"/>
      <line x1="228" y1="310" x2="272" y2="310" stroke="rgba(255,255,255,0.12)" strokeWidth="1"/>
      <circle cx="228" cy="310" r="3" fill="rgba(255,255,255,0.15)"/>
      <circle cx="272" cy="310" r="3" fill="rgba(255,255,255,0.15)"/>
    </svg>
  );
}

export default function LoginPage() {
  const { instance } = useMsal();

  const handleLogin = async () => {
    await instance.loginPopup(loginRequest);
  };

  return (
    <div className="login-page">

      {/* ── Left: Dark AI Visual Panel ─────────────────────────────────────── */}
      <div className="login-hero">
        <div className="login-hero-visual">
          <AiNetworkSvg />
        </div>
      </div>

      {/* ── Right: Dark Form Panel ─────────────────────────────────────────── */}
      <div className="login-panel">

        {/* Corner logo (top-right) — Tech M Orion Marketplace */}
        <div className="lp-logo">
          <LogoMark size={34} />
          <div className="lp-logo-text">
            <span className="lp-logo-line1">TECH M ORION</span>
            <span className="lp-logo-line2">Marketplace</span>
          </div>
        </div>

        {/* Body */}
        <div className="lp-body">

          {/* Mobile-only logo — shown when hero panel collapses */}
          <div className="lp-mobile-logo" aria-hidden="true">
            <LogoMark size={40} />
          </div>

          <h1 className="lp-heading">
            Tech M Orion <span className="lp-accent">Marketplace</span>
          </h1>
          <p className="lp-sub">AI Workflow Platform</p>
          <p className="lp-tagline">Agentify your Business</p>

          <hr className="lp-divider" />

          <p className="lp-already">Already signed up?</p>
          <button className="btn-lp-primary" onClick={handleLogin}>
            {/* Microsoft four-square logo */}
            <svg width="20" height="20" viewBox="0 0 21 21" aria-hidden="true">
              <rect x="1"  y="1"  width="9" height="9" fill="#f25022" />
              <rect x="11" y="1"  width="9" height="9" fill="#7fba00" />
              <rect x="1"  y="11" width="9" height="9" fill="#00a4ef" />
              <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
            </svg>
            Login Now
          </button>

          <div className="lp-or">OR</div>

          <h3 className="lp-create-title">New to AgentifFlow?</h3>
          <p className="lp-create-hint">
            We're glad you're here.<br />
            Contact your Azure AD administrator to provision your account.
          </p>

          <p className="lp-secure">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4z" />
            </svg>
            Secured by Azure Active Directory OAuth 2.0
          </p>

        </div>

        {/* Footer */}
        <div className="lp-footer">
          <p className="lp-footer-text">
            <strong>If your company is not onboarded yet,<br />
            please reach out to your sales team.</strong>
          </p>
          <button className="btn-lp-request">Request Access</button>
        </div>

      </div>
    </div>
  );
}
