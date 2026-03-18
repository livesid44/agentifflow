import type { Configuration, PopupRequest } from "@azure/msal-browser";

/**
 * MSAL configuration.
 * Values are read from environment variables injected by Vite at build time.
 * Set them in a `.env.local` file:
 *
 *   VITE_AAD_CLIENT_ID=<your-client-id>
 *   VITE_AAD_TENANT_ID=<your-tenant-id>
 *   VITE_API_CLIENT_ID=<your-api-client-id>   (the exposed API client id)
 *   VITE_API_BASE_URL=https://localhost:7001   (backend base URL)
 */
const clientId = import.meta.env.VITE_AAD_CLIENT_ID ?? "YOUR_CLIENT_ID";
const tenantId = import.meta.env.VITE_AAD_TENANT_ID ?? "YOUR_TENANT_ID";
const apiClientId = import.meta.env.VITE_API_CLIENT_ID ?? clientId;

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: "sessionStorage",
  },
};

/** Scopes requested on sign-in (OpenID Connect + profile) */
export const loginRequest: PopupRequest = {
  scopes: ["openid", "profile", "User.Read"],
};

/** Scopes for calling the AgentifFlow backend API */
export const apiRequest: PopupRequest = {
  scopes: [`api://${apiClientId}/access_as_user`],
};

export const apiBaseUrl: string =
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000";
