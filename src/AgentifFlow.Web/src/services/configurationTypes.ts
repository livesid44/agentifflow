export interface AppConfigurationDto {
  graphTenantId: string | null;
  graphClientId: string | null;
  graphClientSecret: string | null;
  graphScopes: string | null;
  graphMailboxAddress: string | null;
  openAiEndpoint: string | null;
  openAiApiKey: string | null;
  openAiDeploymentName: string | null;
  blobStorageConnectionString: string | null;
  blobContainerName: string | null;
  sqlConnectionString: string | null;
  agentFlowEnabled: boolean | null;
  blobPollIntervalSeconds: number | null;
  maxRetryCount: number | null;
  notificationEmail: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface UpdateAppConfigurationRequest {
  graphTenantId?: string;
  graphClientId?: string;
  graphClientSecret?: string;
  graphScopes?: string;
  graphMailboxAddress?: string;
  openAiEndpoint?: string;
  openAiApiKey?: string;
  openAiDeploymentName?: string;
  blobStorageConnectionString?: string;
  blobContainerName?: string;
  sqlConnectionString?: string;
  agentFlowEnabled?: boolean;
  blobPollIntervalSeconds?: number;
  maxRetryCount?: number;
  notificationEmail?: string;
}

export interface ConnectivityResult {
  success: boolean;
  message: string;
}
