# AgentifFlow

A .NET 8 Web API application integrating Azure Services with OAuth2.0, Microsoft Graph API for mail, Azure OpenAI LLM, and SQL Server database.

## Features

- **Azure AD OAuth2.0 Authentication** — Secure API access using Microsoft Identity Web (Bearer token)
- **Microsoft Graph API — Mail** — Read inbox, send emails, retrieve and delete messages on behalf of the signed-in user
- **Azure OpenAI LLM** — Chat completions, text summarization, and task extraction powered by GPT-4o
- **SQL Server / Entity Framework Core** — Full CRUD operations on Agent Tasks with EF Core 8 and auto-migration
- **Swagger / OpenAPI** — Interactive API documentation with OAuth2 flow for testing in the browser

## Project Structure

```
AgentifFlow.sln
├── src/
│   └── AgentifFlow.Api/
│       ├── Controllers/
│       │   ├── AgentTaskController.cs   # CRUD operations for agent tasks
│       │   ├── LlmController.cs         # Chat, summarize, extract-tasks endpoints
│       │   └── MailController.cs        # Graph API mail endpoints
│       ├── Data/
│       │   ├── AgentifFlowDbContext.cs  # EF Core DbContext
│       │   └── Migrations/              # EF Core migrations
│       ├── Models/
│       │   ├── AgentTask.cs             # Agent task entity
│       │   ├── ChatModels.cs            # LLM request/response models
│       │   └── EmailRequest.cs          # Email request/response models
│       ├── Services/
│       │   ├── AgentTaskService.cs      # Task persistence (SQL Server)
│       │   ├── GraphMailService.cs      # Graph API mail integration
│       │   └── LlmService.cs            # Azure OpenAI integration
│       ├── appsettings.json
│       └── Program.cs
└── tests/
    └── AgentifFlow.Tests/
        ├── AgentTaskServiceTests.cs     # Service-layer unit tests (in-memory DB)
        └── ModelValidationTests.cs      # Model validation tests
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- SQL Server (or LocalDB for development)
- Azure subscription with:
  - Azure AD app registration (for OAuth2)
  - Azure OpenAI resource
  - Microsoft Graph API permissions: `Mail.Read`, `Mail.Send`, `Mail.ReadWrite`

## Configuration

Copy the placeholder values in `appsettings.json` (or use `appsettings.Development.json`) and replace with your real Azure credentials. **Never commit secrets to source control** — use [Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/) or [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for local development.

| Key | Description |
|---|---|
| `AzureAd:TenantId` | Azure AD tenant ID |
| `AzureAd:ClientId` | Azure AD app registration client ID |
| `AzureAd:ClientSecret` | App registration client secret |
| `AzureOpenAI:Endpoint` | Azure OpenAI resource endpoint URL |
| `AzureOpenAI:ApiKey` | Azure OpenAI API key |
| `AzureOpenAI:DeploymentName` | Model deployment name (default: `gpt-4o`) |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |

### Using .NET User Secrets (recommended for development)

```bash
cd src/AgentifFlow.Api
dotnet user-secrets set "AzureAd:TenantId" "<your-tenant-id>"
dotnet user-secrets set "AzureAd:ClientId" "<your-client-id>"
dotnet user-secrets set "AzureAd:ClientSecret" "<your-client-secret>"
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-api-key>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=AgentifFlowDb;..."
```

## Running the Application

```bash
# Restore dependencies and run
dotnet run --project src/AgentifFlow.Api

# The API will be available at https://localhost:5001
# Swagger UI: https://localhost:5001/swagger
```

## Running Tests

```bash
dotnet test
```

## API Endpoints

### Mail (requires `Mail.Read` / `Mail.Send` scope)

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/mail/inbox?top=10` | Get inbox messages |
| `GET` | `/api/mail/{messageId}` | Get a specific message |
| `POST` | `/api/mail/send` | Send an email |
| `DELETE` | `/api/mail/{messageId}` | Delete a message |

### LLM (Azure OpenAI)

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/llm/chat` | Chat completion |
| `POST` | `/api/llm/summarize` | Summarize text |
| `POST` | `/api/llm/extract-tasks` | Extract tasks from text (returns JSON) |

### Agent Tasks (SQL Server)

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/agenttask` | List all tasks |
| `GET` | `/api/agenttask/{id}` | Get task by ID |
| `GET` | `/api/agenttask/status/{status}` | Filter tasks by status |
| `POST` | `/api/agenttask` | Create a new task |
| `PUT` | `/api/agenttask/{id}` | Update a task |
| `DELETE` | `/api/agenttask/{id}` | Delete a task |

## Architecture

```
Client (Bearer Token)
      │
      ▼
┌─────────────────────────────────┐
│       ASP.NET Core Web API      │
│  (Microsoft.Identity.Web Auth)  │
├─────────────────────────────────┤
│  MailController                 │──► GraphMailService ──► Microsoft Graph API
│  LlmController                  │──► LlmService       ──► Azure OpenAI
│  AgentTaskController            │──► AgentTaskService ──► SQL Server (EF Core)
└─────────────────────────────────┘
```
