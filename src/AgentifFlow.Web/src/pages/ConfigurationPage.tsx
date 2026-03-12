import { useEffect, useState } from "react";
import { useMsal } from "@azure/msal-react";
import NavBar from "../components/NavBar";
import ConfigSection from "../components/ConfigSection";
import FormField from "../components/FormField";
import {
  getConfiguration,
  saveConfiguration,
} from "../services/configurationService";
import type { UpdateAppConfigurationRequest } from "../services/configurationTypes";

const MASKED = "••••••••";
const isPlaceholder = (v: string) => v === "" || v === MASKED;

type SaveState = "idle" | "saving" | "saved" | "error";

export default function ConfigurationPage() {
  const { instance } = useMsal();

  // ── Graph / Email ────────────────────────────────────────────────────────
  const [graphTenantId, setGraphTenantId] = useState("");
  const [graphClientId, setGraphClientId] = useState("");
  const [graphClientSecret, setGraphClientSecret] = useState("");
  const [graphScopes, setGraphScopes] = useState("Mail.Read Mail.Send Mail.ReadWrite");

  // ── Azure OpenAI ─────────────────────────────────────────────────────────
  const [openAiEndpoint, setOpenAiEndpoint] = useState("");
  const [openAiApiKey, setOpenAiApiKey] = useState("");
  const [openAiDeploymentName, setOpenAiDeploymentName] = useState("gpt-4o");

  // ── Azure Blob Storage ────────────────────────────────────────────────────
  const [blobConnStr, setBlobConnStr] = useState("");
  const [blobContainer, setBlobContainer] = useState("");

  // ── SQL ──────────────────────────────────────────────────────────────────
  const [sqlConnStr, setSqlConnStr] = useState("");

  // ── UI state ─────────────────────────────────────────────────────────────
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveState, setSaveState] = useState<SaveState>("idle");
  const [saveError, setSaveError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<
    "email" | "openai" | "blob" | "sql"
  >("email");

  useEffect(() => {
    (async () => {
      try {
        const config = await getConfiguration(instance);
        setGraphTenantId(config.graphTenantId ?? "");
        setGraphClientId(config.graphClientId ?? "");
        setGraphClientSecret(config.graphClientSecret ?? "");
        setGraphScopes(config.graphScopes ?? "Mail.Read Mail.Send Mail.ReadWrite");
        setOpenAiEndpoint(config.openAiEndpoint ?? "");
        setOpenAiApiKey(config.openAiApiKey ?? "");
        setOpenAiDeploymentName(config.openAiDeploymentName ?? "gpt-4o");
        setBlobConnStr(config.blobStorageConnectionString ?? "");
        setBlobContainer(config.blobContainerName ?? "");
        setSqlConnStr(config.sqlConnectionString ?? "");
        if (config.updatedAt) setSavedAt(new Date(config.updatedAt).toLocaleString());
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
      graphTenantId,
      graphClientId,
      graphScopes,
      openAiEndpoint,
      openAiDeploymentName,
      blobContainerName: blobContainer,
    };

    // Only send secrets when the user actually typed something new
    if (!isPlaceholder(graphClientSecret)) payload.graphClientSecret = graphClientSecret;
    if (!isPlaceholder(openAiApiKey)) payload.openAiApiKey = openAiApiKey;
    if (!isPlaceholder(blobConnStr)) payload.blobStorageConnectionString = blobConnStr;
    if (!isPlaceholder(sqlConnStr)) payload.sqlConnectionString = sqlConnStr;

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

  const tabs = [
    { id: "email" as const, label: "Email / Graph API", icon: "📧" },
    { id: "openai" as const, label: "Azure OpenAI", icon: "🤖" },
    { id: "blob" as const, label: "Blob Storage", icon: "☁️" },
    { id: "sql" as const, label: "SQL Database", icon: "🗄️" },
  ];

  return (
    <>
      <NavBar />
      <div className="page-container">
        <div className="page-header">
          <h1 className="page-title">Integration Settings</h1>
          <p className="page-subtitle">
            Configure your Azure service connections. Secrets are stored
            securely and displayed as{" "}
            <span style={{ letterSpacing: "2px" }}>••••••••</span> once saved.
          </p>
        </div>

        {/* Tabs */}
        <div className="tabs" role="tablist">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              role="tab"
              aria-selected={activeTab === tab.id}
              className={`tab ${activeTab === tab.id ? "tab-active" : ""}`}
              onClick={() => setActiveTab(tab.id)}
            >
              <span aria-hidden="true">{tab.icon}</span> {tab.label}
            </button>
          ))}
        </div>

        {/* Email / Graph API */}
        {activeTab === "email" && (
          <ConfigSection
            title="Email — Microsoft Graph API"
            icon={<span aria-hidden="true" style={{ fontSize: "1.5rem" }}>📧</span>}
            description="OAuth 2.0 credentials used to read and send mail via Microsoft Graph."
          >
            <FormField
              id="graphTenantId"
              label="Tenant ID"
              value={graphTenantId}
              onChange={setGraphTenantId}
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              hint="Azure AD directory (tenant) ID of your organisation."
            />
            <FormField
              id="graphClientId"
              label="Client ID"
              value={graphClientId}
              onChange={setGraphClientId}
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              hint="Application (client) ID of the registered Azure AD app."
            />
            <FormField
              id="graphClientSecret"
              label="Client Secret"
              value={graphClientSecret}
              onChange={setGraphClientSecret}
              type="password"
              placeholder="Leave blank to keep current secret"
              hint="Client secret generated in the Azure AD app registration."
            />
            <FormField
              id="graphScopes"
              label="Graph Scopes"
              value={graphScopes}
              onChange={setGraphScopes}
              placeholder="Mail.Read Mail.Send Mail.ReadWrite"
              hint="Space-separated Microsoft Graph permission scopes."
            />
          </ConfigSection>
        )}

        {/* Azure OpenAI */}
        {activeTab === "openai" && (
          <ConfigSection
            title="Azure OpenAI — LLM"
            icon={<span aria-hidden="true" style={{ fontSize: "1.5rem" }}>🤖</span>}
            description="Connection details for the Azure OpenAI resource powering chat, summarisation, and task extraction."
          >
            <FormField
              id="openAiEndpoint"
              label="Endpoint URL"
              value={openAiEndpoint}
              onChange={setOpenAiEndpoint}
              type="url"
              placeholder="https://<resource>.openai.azure.com/"
              hint="Full URL of your Azure OpenAI resource."
            />
            <FormField
              id="openAiApiKey"
              label="API Key"
              value={openAiApiKey}
              onChange={setOpenAiApiKey}
              type="password"
              placeholder="Leave blank to keep current key"
              hint="API key from the Azure OpenAI resource keys page."
            />
            <FormField
              id="openAiDeploymentName"
              label="Deployment Name"
              value={openAiDeploymentName}
              onChange={setOpenAiDeploymentName}
              placeholder="gpt-4o"
              hint="Name of your model deployment (e.g. gpt-4o, gpt-35-turbo)."
            />
          </ConfigSection>
        )}

        {/* Blob Storage */}
        {activeTab === "blob" && (
          <ConfigSection
            title="Azure Blob Storage"
            icon={<span aria-hidden="true" style={{ fontSize: "1.5rem" }}>☁️</span>}
            description="Storage account settings for persisting files and agent artefacts."
          >
            <FormField
              id="blobConnStr"
              label="Connection String"
              value={blobConnStr}
              onChange={setBlobConnStr}
              type="password"
              placeholder="DefaultEndpointsProtocol=https;AccountName=…"
              hint="Full connection string from the Storage account Access keys page."
            />
            <FormField
              id="blobContainer"
              label="Container Name"
              value={blobContainer}
              onChange={setBlobContainer}
              placeholder="agentifflow"
              hint="Name of the default blob container to use."
            />
          </ConfigSection>
        )}

        {/* SQL */}
        {activeTab === "sql" && (
          <ConfigSection
            title="SQL Database"
            icon={<span aria-hidden="true" style={{ fontSize: "1.5rem" }}>🗄️</span>}
            description="Connection string for the SQL Server database used by the AgentifFlow backend."
          >
            <FormField
              id="sqlConnStr"
              label="Connection String"
              value={sqlConnStr}
              onChange={setSqlConnStr}
              type="password"
              placeholder="Server=…;Database=AgentifFlowDb;…"
              hint="ADO.NET connection string for your Azure SQL or SQL Server instance."
            />
          </ConfigSection>
        )}

        {/* Footer */}
        <div className="config-footer">
          {saveError && (
            <div className="alert alert-error">{saveError}</div>
          )}
          {savedAt && (
            <p className="saved-at">Last saved: {savedAt}</p>
          )}
          <button
            className={`btn btn-primary ${saveState === "saving" ? "btn-loading" : ""}`}
            onClick={handleSave}
            disabled={saveState === "saving"}
          >
            {saveState === "saving" && (
              <span className="btn-spinner" aria-hidden="true" />
            )}
            {saveState === "saved"
              ? "✓ Saved!"
              : saveState === "saving"
              ? "Saving…"
              : "Save Configuration"}
          </button>
        </div>
      </div>
    </>
  );
}
