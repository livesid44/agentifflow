import { MsalProvider, AuthenticatedTemplate, UnauthenticatedTemplate } from "@azure/msal-react";
import { PublicClientApplication } from "@azure/msal-browser";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { msalConfig } from "./authConfig";
import LoginPage from "./pages/LoginPage";
import ConfigurationPage from "./pages/ConfigurationPage";

const msalInstance = new PublicClientApplication(msalConfig);

export default function App() {
  return (
    <MsalProvider instance={msalInstance}>
      <BrowserRouter>
        <AuthenticatedTemplate>
          <Routes>
            <Route path="/configuration" element={<ConfigurationPage />} />
            <Route path="*" element={<Navigate to="/configuration" replace />} />
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
