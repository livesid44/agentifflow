# Sample Test Files — Agent 1 (Nerandomilast Target Files Monitor)

This directory contains sample data files you can upload to Azure Blob Storage to trigger and test **Agent 1** (Use Case 1).

## File Naming Convention

Agent 1 expects four files per day, named using the pattern:

```
344_bi_nerandomilast_targets_<yyyyMMdd>_<yyyyMMdd>_<type>.txt
```

Where `<yyyyMMdd>` is today's date.  For example, for **2026-03-17**:

| File | Purpose |
|------|---------|
| `344_bi_nerandomilast_targets_20260317_20260317_events.txt`   | Clinical trial adverse-event records |
| `344_bi_nerandomilast_targets_20260317_20260317_topics.txt`   | Active discussion topics / action items |
| `344_bi_nerandomilast_targets_20260317_20260317_diseases.txt` | Disease / indication reference data |
| `344_bi_nerandomilast_targets_20260317_20260317_control.txt`  | Manifest / checksum control record |

## How to Test

1. **Rename files** — Replace `20260317` in every filename with today's date (e.g. `20260318`).
2. **Upload to Blob Storage** — Put all four files in the container configured in **Integration Settings → Blob Storage**.
3. **Wait for the poll cycle** — The agent polls every 30 s in test mode (set `BlobPollIntervalSeconds = 30` in Integration Settings).
4. **Watch the Dashboard** — A new job appears; once all four files are detected, data is pushed to SQL if the SQL Management skill is enabled.

### Trigger an alert (missing-file scenario)
Upload only three of the four files to see the missing-file notification email sent to the configured address.

### Trigger a SQL push
Configure a SQL Server connection string in **Integration Settings → SQL Database** and upload all four files.  The events, topics, and diseases files will be bulk-inserted into three tables auto-created in your database.

## Connection String Samples

### Azure Blob Storage
```
DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=base64key==;EndpointSuffix=core.windows.net
```

### Azurite (local emulator)
```
UseDevelopmentStorage=true
```

### Azure SQL (SQL authentication)
```
Server=myserver.database.windows.net;Database=AgentifFlowDb;User Id=myuser;Password=mypassword;TrustServerCertificate=True;
```

### Local SQL Server Express (Windows authentication)
```
Server=localhost\SQLEXPRESS;Database=AgentifFlowDb;Trusted_Connection=True;TrustServerCertificate=True;
```
