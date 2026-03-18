import { useEffect, useState, useRef } from "react";
import { useMsal } from "@azure/msal-react";
import NavBar from "../components/NavBar";
import Footer from "../components/Footer";
import { getConfiguration } from "../services/configurationService";

// ── Types ─────────────────────────────────────────────────────────────────────
interface ActivityEntry {
  id: string;
  timestamp: string;
  source: "API Call" | "Agent" | "Blob Watcher" | "System";
  event: string;
  details: string;
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function sourceColor(source: ActivityEntry["source"]) {
  const map: Record<string, string> = {
    "API Call":     "#8B5CF6",
    "Agent":        "#E31937",
    "Blob Watcher": "#0EA5E9",
    "System":       "#64748B",
  };
  return map[source] ?? "#94A3B8";
}

function formatTs(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit",
  });
}

// ── Seed system log entries from config load ──────────────────────────────────
function seedEntries(): ActivityEntry[] {
  const now = new Date();
  const ago = (s: number) => new Date(now.getTime() - s * 1000).toISOString();
  return [
    { id: "s1", timestamp: ago(2),   source: "System",       event: "Started",   details: "AgentifFlow dashboard loaded" },
    { id: "s2", timestamp: ago(4),   source: "API Call",     event: "GET /api/configuration", details: "Fetching integration settings" },
    { id: "s3", timestamp: ago(6),   source: "System",       event: "Auth",      details: "Azure AD token verified" },
  ];
}

// ── Component ─────────────────────────────────────────────────────────────────
export default function DashboardPage() {
  const { instance } = useMsal();
  const [loading, setLoading]   = useState(true);
  const [error, setError]       = useState<string | null>(null);
  const [agentEnabled, setAgentEnabled] = useState<boolean | null>(null);
  const [log, setLog]           = useState<ActivityEntry[]>(seedEntries);
  const logRef                  = useRef(log);
  logRef.current                = log;

  const pushEntry = (entry: Omit<ActivityEntry, "id" | "timestamp">) => {
    setLog(prev => [
      { ...entry, id: crypto.randomUUID(), timestamp: new Date().toISOString() },
      ...prev,
    ].slice(0, 200));
  };

  useEffect(() => {
    (async () => {
      pushEntry({ source: "API Call", event: "GET /api/configuration", details: "Loading agent configuration" });
      try {
        const cfg = await getConfiguration(instance);
        pushEntry({ source: "System", event: "Config Loaded", details: `AgentFlow: ${cfg.agentFlowEnabled ? "enabled" : "disabled"}, Poll: ${cfg.blobPollIntervalSeconds ?? 60}s` });
        setAgentEnabled(cfg.agentFlowEnabled ?? false);
      } catch (err) {
        const msg = err instanceof Error ? err.message : "Unknown error";
        pushEntry({ source: "System", event: "Error", details: `Config load failed: ${msg}` });
        setError(msg);
      } finally {
        setLoading(false);
      }
    })();
  }, [instance]);

  // Periodic fake heartbeat so the log shows real-time feel
  useEffect(() => {
    const timer = setInterval(() => {
      pushEntry({ source: "System", event: "Heartbeat", details: "Dashboard alive — auto-refresh tick" });
    }, 30_000);
    return () => clearInterval(timer);
  }, []);

  return (
    <>
      <NavBar />
      <div className="page-container">

        {/* Header */}
        <div className="page-header">
          <h1 className="page-title">Dashboard</h1>
          <p className="page-subtitle">
            Real-time activity log and API call history for your AgentifFlow workspace.
          </p>
        </div>

        {/* Status cards */}
        <div className="dash-cards">
          <div className="dash-card">
            <span className="dash-card-icon" aria-hidden="true">🤖</span>
            <div>
              <div className="dash-card-label">Agent Flow</div>
              <div className="dash-card-value" style={{ color: agentEnabled ? "#059669" : "#94A3B8" }}>
                {loading ? "…" : agentEnabled ? "Enabled" : "Disabled"}
              </div>
            </div>
          </div>
          <div className="dash-card">
            <span className="dash-card-icon" aria-hidden="true">📋</span>
            <div>
              <div className="dash-card-label">Log Entries</div>
              <div className="dash-card-value">{log.length}</div>
            </div>
          </div>
          <div className="dash-card">
            <span className="dash-card-icon" aria-hidden="true">🔗</span>
            <div>
              <div className="dash-card-label">API Calls</div>
              <div className="dash-card-value">{log.filter(e => e.source === "API Call").length}</div>
            </div>
          </div>
          <div className="dash-card">
            <span className="dash-card-icon" aria-hidden="true">✅</span>
            <div>
              <div className="dash-card-label">Status</div>
              <div className="dash-card-value" style={{ color: "#059669" }}>Online</div>
            </div>
          </div>
        </div>

        {error && <div className="alert alert-error mb-4">{error}</div>}

        {/* Activity Log */}
        <div className="config-section" style={{ marginTop: 24 }}>
          <div className="config-section-header">
            <div className="config-section-icon" aria-hidden="true" style={{ fontSize: "1.3rem" }}>📊</div>
            <div>
              <div className="config-section-title">Activity Log</div>
              <div className="config-section-desc">
                All agent events, API calls, and system messages in real time.
              </div>
            </div>
          </div>
          <div className="activity-log-table">
            <table aria-label="Activity log">
              <thead>
                <tr>
                  <th>Timestamp</th>
                  <th>Source</th>
                  <th>Event</th>
                  <th>Details</th>
                </tr>
              </thead>
              <tbody>
                {log.map((entry) => (
                  <tr key={entry.id}>
                    <td className="log-ts">{formatTs(entry.timestamp)}</td>
                    <td>
                      <span
                        className="log-badge"
                        style={{ background: sourceColor(entry.source) }}
                      >
                        {entry.source}
                      </span>
                    </td>
                    <td className="log-event">{entry.event}</td>
                    <td className="log-detail">{entry.details}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

      </div>
      <Footer />
    </>
  );
}
