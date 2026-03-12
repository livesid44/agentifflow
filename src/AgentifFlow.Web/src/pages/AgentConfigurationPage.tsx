import { useEffect, useState } from "react";
import { useMsal } from "@azure/msal-react";
import NavBar from "../components/NavBar";
import Footer from "../components/Footer";
import { getConfiguration, saveConfiguration } from "../services/configurationService";
import type { UpdateAppConfigurationRequest } from "../services/configurationTypes";

type SaveState = "idle" | "saving" | "saved" | "error";

export default function AgentConfigurationPage() {
  const { instance } = useMsal();

  const [agentFlowEnabled, setAgentFlowEnabled]   = useState(false);
  const [pollInterval, setPollInterval]           = useState(60);
  const [maxRetries, setMaxRetries]               = useState(3);
  const [notificationEmail, setNotificationEmail] = useState("");

  const [loading, setLoading]       = useState(true);
  const [loadError, setLoadError]   = useState<string | null>(null);
  const [saveState, setSaveState]   = useState<SaveState>("idle");
  const [saveError, setSaveError]   = useState<string | null>(null);
  const [savedAt, setSavedAt]       = useState<string | null>(null);

  // Cache other settings so we don't overwrite them on save
  const [otherFields, setOtherFields] = useState<Partial<UpdateAppConfigurationRequest>>({});

  useEffect(() => {
    (async () => {
      try {
        const cfg = await getConfiguration(instance);
        setAgentFlowEnabled(cfg.agentFlowEnabled ?? false);
        setPollInterval(cfg.blobPollIntervalSeconds ?? 60);
        setMaxRetries(cfg.maxRetryCount ?? 3);
        setNotificationEmail(cfg.notificationEmail ?? "");
        if (cfg.updatedAt) setSavedAt(new Date(cfg.updatedAt).toLocaleString());
        // Cache everything else so HandleSave doesn't blank them
        setOtherFields({
          graphTenantId:        cfg.graphTenantId ?? "",
          graphClientId:        cfg.graphClientId ?? "",
          graphScopes:          cfg.graphScopes ?? "",
          openAiEndpoint:       cfg.openAiEndpoint ?? "",
          openAiDeploymentName: cfg.openAiDeploymentName ?? "",
          blobContainerName:    cfg.blobContainerName ?? "",
        });
      } catch (err) {
        setLoadError(err instanceof Error ? err.message : "Failed to load configuration.");
      } finally {
        setLoading(false);
      }
    })();
  }, [instance]);

  const handleSave = async () => {
    setSaveState("saving");
    setSaveError(null);
    const payload: UpdateAppConfigurationRequest = {
      ...otherFields,
      agentFlowEnabled,
      blobPollIntervalSeconds: pollInterval,
      maxRetryCount:            maxRetries,
      notificationEmail,
    };
    try {
      const saved = await saveConfiguration(instance, payload);
      setSaveState("saved");
      if (saved.updatedAt) setSavedAt(new Date(saved.updatedAt).toLocaleString());
      setTimeout(() => setSaveState("idle"), 3000);
    } catch (err) {
      setSaveState("error");
      setSaveError(err instanceof Error ? err.message : "Failed to save.");
    }
  };

  if (loading) {
    return (
      <>
        <NavBar />
        <div className="page-container">
          <div className="loading-spinner" role="status" aria-label="Loading configuration" />
        </div>
      </>
    );
  }

  if (loadError) {
    return (
      <>
        <NavBar />
        <div className="page-container">
          <div className="alert alert-error">{loadError}</div>
        </div>
      </>
    );
  }

  return (
    <>
      <NavBar />
      <div className="page-container">
        <div className="page-header">
          <h1 className="page-title">Agent Configuration</h1>
          <p className="page-subtitle">
            Configure the AgentifFlow background worker. Changes take effect immediately.
          </p>
        </div>

        <div className="config-section">
          <div className="config-section-header">
            <div className="config-section-icon" aria-hidden="true" style={{ fontSize: "1.5rem" }}>🤖</div>
            <div>
              <div className="config-section-title">Agent Flow Worker</div>
              <div className="config-section-desc">
                Controls when and how the background worker processes CSV blobs from Azure Storage.
              </div>
            </div>
          </div>
          <div className="config-section-body">

            {/* Toggle */}
            <div className="form-field">
              <label className="form-label" style={{ display: "flex", alignItems: "center", gap: 12, cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={agentFlowEnabled}
                  onChange={e => setAgentFlowEnabled(e.target.checked)}
                  style={{ width: 18, height: 18, accentColor: "var(--primary)" }}
                />
                <span>Enable Agent Flow</span>
              </label>
              <p className="form-hint">
                When enabled, the background worker polls Blob Storage and processes CSV files automatically.
              </p>
            </div>

            {/* Poll interval */}
            <div className="form-field">
              <label className="form-label" htmlFor="pollInterval">Poll Interval (seconds)</label>
              <input
                id="pollInterval"
                type="number"
                className="form-input"
                value={pollInterval}
                min={10}
                max={3600}
                onChange={e => setPollInterval(Math.max(10, Math.min(3600, Number(e.target.value))))}
              />
              <p className="form-hint">How often the background worker checks Blob Storage for new CSV files.</p>
            </div>

            {/* Max retries */}
            <div className="form-field">
              <label className="form-label" htmlFor="maxRetries">Max Retries</label>
              <input
                id="maxRetries"
                type="number"
                className="form-input"
                value={maxRetries}
                min={0}
                max={10}
                onChange={e => setMaxRetries(Math.max(0, Math.min(10, Number(e.target.value))))}
              />
              <p className="form-hint">Maximum number of automatic retry attempts for failed SQL inserts.</p>
            </div>

            {/* Notification email */}
            <div className="form-field">
              <label className="form-label" htmlFor="notifEmail">Notification Email</label>
              <input
                id="notifEmail"
                type="email"
                className="form-input"
                value={notificationEmail}
                placeholder="alerts@contoso.com"
                onChange={e => setNotificationEmail(e.target.value)}
              />
              <p className="form-hint">Email address that receives CSV validation failure alerts.</p>
            </div>

          </div>
        </div>

        {/* Save footer */}
        <div className="config-footer">
          {saveError && <div className="alert alert-error">{saveError}</div>}
          {savedAt && <p className="saved-at">Last saved: {savedAt}</p>}
          <button
            className={`btn btn-primary ${saveState === "saving" ? "btn-loading" : ""}`}
            onClick={handleSave}
            disabled={saveState === "saving"}
          >
            {saveState === "saving" && <span className="btn-spinner" aria-hidden="true" />}
            {saveState === "saved"
              ? "✓ Saved!"
              : saveState === "saving"
              ? "Saving…"
              : "Save Configuration"}
          </button>
        </div>
      </div>
      <Footer />
    </>
  );
}
