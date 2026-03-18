-- ============================================================
-- AgentifFlow — SQL script for Agent 1 (Nerandomilast Target Files Monitor)
-- Run this script against your SQL Server database BEFORE uploading
-- the sample files so that the SQL Management skill can push data.
--
-- Compatible with: SQL Server 2016+ / Azure SQL Database
-- Generated for the four sample file types included in this directory.
-- ============================================================

-- ── 1. Events table ──────────────────────────────────────────────────────────
-- Source file: 344_bi_nerandomilast_targets_<date>_<date>_events.txt

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'nerandomilast_events')
BEGIN
    CREATE TABLE [nerandomilast_events] (
        [PatientId]        NVARCHAR(MAX),
        [EventDate]        NVARCHAR(MAX),
        [EventType]        NVARCHAR(MAX),
        [AECode]           NVARCHAR(MAX),
        [AETerm]           NVARCHAR(MAX),
        [Severity]         NVARCHAR(MAX),
        [Grade]            NVARCHAR(MAX),
        [StudyArm]         NVARCHAR(MAX),
        [SiteId]           NVARCHAR(MAX),
        [VisitNumber]      NVARCHAR(MAX),
        [AssessedBy]       NVARCHAR(MAX),
        [IsSerious]        NVARCHAR(MAX),
        [Resolution]       NVARCHAR(MAX),
        [Duration_Days]    NVARCHAR(MAX),
        [DrugDose_mg]      NVARCHAR(MAX),
        [CausalityRating]  NVARCHAR(MAX),
        [OutcomeCode]      NVARCHAR(MAX),
        [OnStudyDrug]      NVARCHAR(MAX),
        [ActionTaken]      NVARCHAR(MAX),
        [ReportedDate]     NVARCHAR(MAX)
    );
END
GO

-- ── 2. Topics table ──────────────────────────────────────────────────────────
-- Source file: 344_bi_nerandomilast_targets_<date>_<date>_topics.txt

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'nerandomilast_topics')
BEGIN
    CREATE TABLE [nerandomilast_topics] (
        [TopicId]             NVARCHAR(MAX),
        [TopicName]           NVARCHAR(MAX),
        [Category]            NVARCHAR(MAX),
        [Priority]            NVARCHAR(MAX),
        [AssignedTo]          NVARCHAR(MAX),
        [DueDate]             NVARCHAR(MAX),
        [Status]              NVARCHAR(MAX),
        [CreatedDate]         NVARCHAR(MAX),
        [LastModifiedDate]    NVARCHAR(MAX),
        [DiscussionCount]     NVARCHAR(MAX),
        [Resolution]          NVARCHAR(MAX),
        [Tags]                NVARCHAR(MAX),
        [RelatedProtocol]     NVARCHAR(MAX),
        [ImpactLevel]         NVARCHAR(MAX),
        [Notes]               NVARCHAR(MAX)
    );
END
GO

-- ── 3. Diseases table ────────────────────────────────────────────────────────
-- Source file: 344_bi_nerandomilast_targets_<date>_<date>_diseases.txt

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'nerandomilast_diseases')
BEGIN
    CREATE TABLE [nerandomilast_diseases] (
        [DiseaseCode]             NVARCHAR(MAX),
        [DiseaseName]             NVARCHAR(MAX),
        [ICD10Code]               NVARCHAR(MAX),
        [Indication]              NVARCHAR(MAX),
        [Stage]                   NVARCHAR(MAX),
        [Prevalence_Rate_per_100k] NVARCHAR(MAX),
        [StudyInclusion]          NVARCHAR(MAX),
        [Excluded]                NVARCHAR(MAX),
        [PatientCount]            NVARCHAR(MAX),
        [MeanAge]                 NVARCHAR(MAX),
        [FemalePct]               NVARCHAR(MAX),
        [MalePct]                 NVARCHAR(MAX),
        [MedianFVC_Pct]           NVARCHAR(MAX),
        [MedianDLCO_Pct]          NVARCHAR(MAX),
        [BaselineMRC_Score]       NVARCHAR(MAX),
        [TrialVariant]            NVARCHAR(MAX)
    );
END
GO

-- ── 4. Control table ─────────────────────────────────────────────────────────
-- Source file: 344_bi_nerandomilast_targets_<date>_<date>_control.txt

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'nerandomilast_control')
BEGIN
    CREATE TABLE [nerandomilast_control] (
        [FileName]          NVARCHAR(MAX),
        [RecordCount]       NVARCHAR(MAX),
        [GeneratedTimestamp] NVARCHAR(MAX),
        [GeneratedBy]       NVARCHAR(MAX),
        [DataVersion]       NVARCHAR(MAX),
        [SourceSystem]      NVARCHAR(MAX),
        [Checksum_MD5]      NVARCHAR(MAX),
        [Status]            NVARCHAR(MAX),
        [ValidationStatus]  NVARCHAR(MAX),
        [Notes]             NVARCHAR(MAX)
    );
END
GO
