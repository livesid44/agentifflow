import { MsalProvider, AuthenticatedTemplate, UnauthenticatedTemplate } from "@azure/msal-react";
import { PublicClientApplication } from "@azure/msal-browser";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { msalConfig } from "./authConfig";
import LoginPage from "./pages/LoginPage";
import DashboardPage from "./pages/DashboardPage";
import ConfigurationPage from "./pages/ConfigurationPage";
import AgentConfigurationPage from "./pages/AgentConfigurationPage";

const msalInstance = new PublicClientApplication(msalConfig);

export default function App() {
  return (
    <MsalProvider instance={msalInstance}>
      <BrowserRouter>
        <AuthenticatedTemplate>
          <Routes>
            <Route path="/dashboard" element={<DashboardPage />} />
            <Route path="/configuration" element={<ConfigurationPage />} />
            <Route path="/agent-configuration" element={<AgentConfigurationPage />} />
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </AuthenticatedTemplate>
        <UnauthenticatedTemplate>
          <Routes>
            <Route path="*" element={<LoginPage />} />
          </Routes>
        </UnauthenticatedTemplate>
      </BrowserRouter>
    </MsalProvider>
  );
}
