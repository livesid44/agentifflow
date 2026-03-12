import type { IPublicClientApplication } from "@azure/msal-browser";
import { apiBaseUrl, apiRequest } from "../authConfig";
import type {
  AppConfigurationDto,
  UpdateAppConfigurationRequest,
} from "./configurationTypes";

async function getAccessToken(msalInstance: IPublicClientApplication): Promise<string> {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) throw new Error("No authenticated account found.");

  const result = await msalInstance.acquireTokenSilent({
    ...apiRequest,
    account: accounts[0],
  });
  return result.accessToken;
}

export async function getConfiguration(
  msalInstance: IPublicClientApplication
): Promise<AppConfigurationDto> {
  const token = await getAccessToken(msalInstance);
  const response = await fetch(`${apiBaseUrl}/api/configuration`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) throw new Error(`Failed to load configuration: ${response.statusText}`);
  return response.json() as Promise<AppConfigurationDto>;
}

export async function saveConfiguration(
  msalInstance: IPublicClientApplication,
  data: UpdateAppConfigurationRequest
): Promise<AppConfigurationDto> {
  const token = await getAccessToken(msalInstance);
  const response = await fetch(`${apiBaseUrl}/api/configuration`, {
    method: "PUT",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(data),
  });
  if (!response.ok) throw new Error(`Failed to save configuration: ${response.statusText}`);
  return response.json() as Promise<AppConfigurationDto>;
}
