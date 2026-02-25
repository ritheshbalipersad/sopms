# SOPMSApp – SQL Databases and Table Scripts

This document describes the databases, tables, and stored procedures required by SOPMSApp, and provides SQL scripts to create them from scratch.

---

## 1. Overview

| Connection Key         | Database        | Purpose                                        |
|------------------------|-----------------|------------------------------------------------|
| **DefaultConnection**  | DocRet          | Main application data (DocRegisters, SOPs, etc.) |
| **entTTSAPConnection** | entTTSAP        | Labor, departments, document types, areas      |
| **LoginConnection**    | MCRegistrationSA| User authentication                            |

---

## 2. Create Databases

Run the following to create all three databases:

```sql
-- Run as a user with dbcreator permission
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'DocRet')
    CREATE DATABASE [DocRet];
GO
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'entTTSAP')
    CREATE DATABASE [entTTSAP];
GO
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'MCRegistrationSA')
    CREATE DATABASE [MCRegistrationSA];
GO
```

---

## 3. DocRet Database (DefaultConnection)

**Context:** `ApplicationDbContext`

### 3.1 Tables

| Table                  | Purpose |
|------------------------|---------|
| DocRegisters           | Main document/SOP metadata (file paths, status, revision) |
| DocArchives            | Archived document metadata |
| DocRegisterHistories   | Revision history for documents |
| DocumentAuditLogs      | Audit trail (upload, approve, delete, archive, etc.) |
| DeletedFileLogs        | Log of soft-deleted files |
| StructuredSops         | Structured SOP definitions |
| SopSteps               | Steps within a structured SOP |
| StructuredSopHistory   | History for structured SOP changes |
| SopStepHistory         | History for SOP step changes |
| Areas                  | Applicable areas (HR, Production, Maintenance, etc.) |

### 3.2 Create DocRet Tables

```sql
USE [DocRet];
GO

-- Areas
IF OBJECT_ID(N'dbo.Areas', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Areas] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [AreaName] NVARCHAR(MAX) NOT NULL,
        CONSTRAINT [PK_Areas] PRIMARY KEY ([Id])
    );
END

-- DocRegisters
IF OBJECT_ID(N'dbo.DocRegisters', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DocRegisters] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [SopNumber] NVARCHAR(MAX) NOT NULL,
        [DocumentType] NVARCHAR(MAX) NULL,
        [uniqueNumber] NVARCHAR(MAX) NOT NULL,
        [DocType] NVARCHAR(MAX) NULL,
        [Department] NVARCHAR(MAX) NOT NULL,
        [Area] NVARCHAR(MAX) NULL,
        [Revision] NVARCHAR(MAX) NOT NULL,
        [FileName] NVARCHAR(MAX) NOT NULL,
        [OriginalFile] NVARCHAR(MAX) NOT NULL,
        [DocumentPath] NVARCHAR(MAX) NULL,
        [VideoPath] NVARCHAR(MAX) NULL,
        [ContentType] NVARCHAR(MAX) NOT NULL,
        [FileSize] BIGINT NOT NULL,
        [Author] NVARCHAR(MAX) NOT NULL,
        [UserEmail] NVARCHAR(MAX) NULL,
        [DepartmentSupervisor] NVARCHAR(MAX) NULL,
        [SupervisorEmail] NVARCHAR(MAX) NULL,
        [Changed] NVARCHAR(MAX) NULL,
        [Status] NVARCHAR(MAX) NOT NULL DEFAULT N'Pending Approval',
        [ReviewStatus] NVARCHAR(MAX) NULL,
        [ApprovalStage] NVARCHAR(MAX) NULL,
        [ManagerApproved] BIT NULL,
        [ManagerApprovedDate] DATETIME2 NULL,
        [AdminApproved] BIT NULL,
        [AdminApprovedDate] DATETIME2 NULL,
        [ApprovedBy] NVARCHAR(MAX) NULL,
        [ReviewedBy] NVARCHAR(MAX) NULL,
        [UploadDate] DATETIME2 NOT NULL,
        [LastReviewDate] DATETIME2 NULL,
        [EffectiveDate] DATETIME2 NULL,
        [RejectionReason] NVARCHAR(MAX) NULL,
        [ReturnedDate] DATETIME2 NULL,
        [DeletionReason] NVARCHAR(MAX) NULL,
        [DeletionRequestedBy] NVARCHAR(MAX) NULL,
        [DeletionRequestedOn] DATETIME2 NULL,
        [IsArchived] BIT NULL DEFAULT 0,
        [ArchivedOn] DATETIME2 NULL,
        [IsStructured] BIT NULL,
        [StructuredSopId] INT NULL,
        CONSTRAINT [PK_DocRegisters] PRIMARY KEY ([Id])
    );
END

-- DocArchives
IF OBJECT_ID(N'dbo.DocArchives', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DocArchives] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [SopNumber] NVARCHAR(100) NOT NULL,
        [Title] NVARCHAR(255) NOT NULL,
        [Revision] NVARCHAR(10) NULL,
        [FileName] NVARCHAR(255) NOT NULL,
        [ContentType] NVARCHAR(100) NOT NULL,
        [Department] NVARCHAR(100) NOT NULL,
        [Author] NVARCHAR(100) NOT NULL,
        [UserEmail] NVARCHAR(MAX) NULL,
        [DocType] NVARCHAR(100) NOT NULL,
        [Area] NVARCHAR(500) NOT NULL,
        [EffectiveDate] DATETIME2 NULL,
        [ArchivedOn] DATETIME2 NOT NULL,
        [ArchivedBy] NVARCHAR(100) NOT NULL,
        [SourceTable] NVARCHAR(50) NOT NULL,
        [SourceId] INT NULL,
        [Notes] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_DocArchives] PRIMARY KEY ([Id])
    );
END

-- DocRegisterHistories
IF OBJECT_ID(N'dbo.DocRegisterHistories', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DocRegisterHistories] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [DocRegisterId] INT NOT NULL,
        [SopNumber] NVARCHAR(MAX) NOT NULL,
        [OriginalFile] NVARCHAR(MAX) NOT NULL,
        [FileName] NVARCHAR(MAX) NOT NULL,
        [Department] NVARCHAR(MAX) NOT NULL,
        [Revision] NVARCHAR(MAX) NOT NULL,
        [EffectiveDate] DATETIME2 NULL,
        [LastReviewDate] DATETIME2 NULL,
        [UploadDate] DATETIME2 NOT NULL,
        [Status] NVARCHAR(MAX) NOT NULL,
        [DocumentType] NVARCHAR(MAX) NOT NULL,
        [RevisedBy] NVARCHAR(MAX) NOT NULL,
        [RevisedOn] DATETIME2 NOT NULL,
        [ChangeDescription] NVARCHAR(MAX) NOT NULL,
        CONSTRAINT [PK_DocRegisterHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocRegisterHistories_DocRegisters_DocRegisterId] 
            FOREIGN KEY ([DocRegisterId]) REFERENCES [dbo].[DocRegisters]([Id])
    );
    CREATE INDEX [IX_DocRegisterHistories_DocRegisterId] ON [dbo].[DocRegisterHistories]([DocRegisterId]);
END

-- DocumentAuditLogs
IF OBJECT_ID(N'dbo.DocumentAuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DocumentAuditLogs] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [DocRegisterId] INT NULL,
        [SopNumber] NVARCHAR(100) NOT NULL,
        [Action] NVARCHAR(64) NOT NULL,
        [PerformedBy] NVARCHAR(256) NOT NULL,
        [PerformedAtUtc] DATETIME2 NOT NULL,
        [Details] NVARCHAR(2000) NULL,
        [DocumentTitle] NVARCHAR(500) NULL,
        CONSTRAINT [PK_DocumentAuditLogs] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_DocumentAuditLogs_DocRegisterId] ON [dbo].[DocumentAuditLogs]([DocRegisterId]);
    CREATE INDEX [IX_DocumentAuditLogs_SopNumber] ON [dbo].[DocumentAuditLogs]([SopNumber]);
    CREATE INDEX [IX_DocumentAuditLogs_PerformedAtUtc] ON [dbo].[DocumentAuditLogs]([PerformedAtUtc]);
END

-- DeletedFileLogs
IF OBJECT_ID(N'dbo.DeletedFileLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DeletedFileLogs] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [SOPNumber] NVARCHAR(100) NOT NULL,
        [FileName] NVARCHAR(255) NOT NULL,
        [OriginalFileName] NVARCHAR(255) NOT NULL,
        [DeletedBy] NVARCHAR(100) NOT NULL,
        [DeletedOn] DATETIME NOT NULL,
        [Reason] NVARCHAR(500) NOT NULL,
        [UserEmail] NVARCHAR(500) NULL,
        [DocType] NVARCHAR(100) NULL,
        [Department] NVARCHAR(100) NULL,
        [Area] NVARCHAR(100) NULL,
        [Revision] NVARCHAR(50) NULL,
        [UniqueNumber] NVARCHAR(100) NULL,
        [ContentType] NVARCHAR(100) NULL,
        [FileSize] BIGINT NOT NULL,
        [Author] NVARCHAR(150) NULL,
        [DepartmentSupervisor] NVARCHAR(150) NULL,
        [SupervisorEmail] NVARCHAR(150) NULL,
        [Status] NVARCHAR(50) NULL DEFAULT N'Archived',
        [EffectiveDate] DATETIME NULL,
        [UploadDate] DATETIME NULL,
        [ArchivedOn] DATETIME NULL,
        [WasApproved] BIT NULL,
        [OriginalDocRegisterId] INT NULL,
        CONSTRAINT [PK_DeletedFileLogs] PRIMARY KEY ([Id])
    );
END

-- StructuredSops (must exist before SopSteps due to FK)
IF OBJECT_ID(N'dbo.StructuredSops', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[StructuredSops] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [SopNumber] NVARCHAR(50) NOT NULL,
        [Title] NVARCHAR(500) NOT NULL,
        [DocRegisterId] INT NULL,
        [Revision] NVARCHAR(10) NOT NULL DEFAULT N'0',
        [EffectiveDate] DATE NOT NULL,
        [ControlledBy] NVARCHAR(MAX) NOT NULL,
        [ApprovedBy] NVARCHAR(MAX) NULL,
        [UserEmail] NVARCHAR(MAX) NULL,
        [DepartmentSupervisor] NVARCHAR(MAX) NULL,
        [SupervisorEmail] NVARCHAR(MAX) NULL,
        [Signatures] NVARCHAR(MAX) NOT NULL,
        [CreatedDate] DATETIME2 NOT NULL,
        [DocType] NVARCHAR(MAX) NOT NULL,
        [Status] NVARCHAR(MAX) NOT NULL DEFAULT N'Draft',
        [ReviewStatus] NVARCHAR(MAX) NULL,
        [Area] NVARCHAR(MAX) NULL,
        [ReviewedBy] NVARCHAR(MAX) NULL DEFAULT N'Pending',
        [CreatedAt] DATETIME2 NOT NULL,
        [ArchivedOn] DATETIME2 NULL,
        [ReturnedDate] DATETIME2 NULL,
        [RejectionReason] NVARCHAR(MAX) NULL,
        [ApprovalStage] NVARCHAR(MAX) NULL,
        [ManagerApprovedDate] DATETIME2 NULL,
        [ManagerApproved] BIT NULL,
        [AdminApprovedDate] DATETIME2 NULL,
        [AdminApproved] BIT NULL,
        [IsSyncedToDocRegister] BIT NULL DEFAULT 0,
        [SyncedDate] DATETIME2 NULL,
        CONSTRAINT [PK_StructuredSops] PRIMARY KEY ([Id])
    );
END

-- SopSteps
IF OBJECT_ID(N'dbo.SopSteps', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SopSteps] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [StepNumber] INT NOT NULL,
        [Instructions] NVARCHAR(MAX) NOT NULL,
        [KeyPoints] NVARCHAR(MAX) NULL,
        [ImagePath] NVARCHAR(MAX) NULL,
        [KeyPointImagePath] NVARCHAR(MAX) NULL,
        [StructuredSopId] INT NOT NULL,
        CONSTRAINT [PK_SopSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SopSteps_StructuredSops_StructuredSopId] 
            FOREIGN KEY ([StructuredSopId]) REFERENCES [dbo].[StructuredSops]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_SopSteps_StructuredSopId] ON [dbo].[SopSteps]([StructuredSopId]);
END

-- StructuredSopHistory
IF OBJECT_ID(N'dbo.StructuredSopHistory', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[StructuredSopHistory] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [SopId] INT NULL,
        [PropertyName] NVARCHAR(MAX) NULL,
        [OldValue] NVARCHAR(MAX) NULL,
        [NewValue] NVARCHAR(MAX) NULL,
        [ChangedBy] NVARCHAR(MAX) NULL,
        [ChangedByEmail] NVARCHAR(MAX) NULL,
        [ChangedAt] DATETIME2 NULL,
        CONSTRAINT [PK_StructuredSopHistory] PRIMARY KEY ([Id])
    );
END

-- SopStepHistory
IF OBJECT_ID(N'dbo.SopStepHistory', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SopStepHistory] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [StepId] INT NULL,
        [SopId] INT NULL,
        [PropertyName] NVARCHAR(MAX) NULL,
        [OldValue] NVARCHAR(MAX) NULL,
        [NewValue] NVARCHAR(MAX) NULL,
        [ChangedBy] NVARCHAR(MAX) NULL,
        [ChangedByEmail] NVARCHAR(MAX) NULL,
        [ChangedAt] DATETIME2 NULL,
        CONSTRAINT [PK_SopStepHistory] PRIMARY KEY ([Id])
    );
END

PRINT 'DocRet tables created.';
GO
```

---

## 4. entTTSAP Database (entTTSAPConnection)

**Context:** `entTTSAPDbContext` and raw SQL in `AccountController`, `FileUploadController`, etc.

### 4.1 Tables

| Table               | Purpose |
|---------------------|---------|
| labor               | Labor/user info (LaborID, LaborName, user_guid, department, email, access group). Used for login role and user details. |
| department          | Departments (DepartmentID, DepartmentName, SupervisorName, active). |
| accessgroupactions  | Access permissions (AccessGroupPK, moduleid, actionid, enabled). Used to derive role (Admin, Manager, Technician, User). |
| Bulletin            | Document types (BulletinName, UDFChar1). Used for doc type dropdowns and SOP number acronyms. |
| asset               | Areas from TTSAP (assetname, udfbit5, isup). Used for area dropdowns. |

### 4.2 Create entTTSAP Tables

```sql
USE [entTTSAP];
GO

-- labor
IF OBJECT_ID(N'dbo.labor', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.labor (
        LaborID NVARCHAR(50) NOT NULL,
        LaborName NVARCHAR(100) NOT NULL,
        user_guid UNIQUEIDENTIFIER NULL,
        labortype NVARCHAR(20) NOT NULL DEFAULT 'EMP',
        active INT NOT NULL DEFAULT 1,
        departmentname NVARCHAR(100) NULL,
        DepartmentID NVARCHAR(50) NULL,
        email NVARCHAR(255) NULL,
        craftname NVARCHAR(50) NULL,
        SupervisorID NVARCHAR(50) NULL,
        SupervisorName NVARCHAR(100) NULL,
        ShopID NVARCHAR(50) NULL,
        ShopName NVARCHAR(100) NULL,
        accessgrouppk NVARCHAR(20) NULL,
        [Access] NVARCHAR(10) NULL DEFAULT '1',
        CONSTRAINT PK_labor PRIMARY KEY (LaborID)
    );
END

-- department
IF OBJECT_ID(N'dbo.department', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.department (
        DepartmentID NVARCHAR(50) NOT NULL,
        DepartmentName NVARCHAR(100) NULL,
        SupervisorName NVARCHAR(100) NULL,
        active INT NOT NULL DEFAULT 1,
        CONSTRAINT PK_department PRIMARY KEY (DepartmentID)
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.department') AND name = 'active')
        ALTER TABLE dbo.department ADD active INT NOT NULL DEFAULT 1;
END

-- accessgroupactions
IF OBJECT_ID(N'dbo.accessgroupactions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.accessgroupactions (
        AccessGroupPK NVARCHAR(20) NOT NULL,
        moduleid NVARCHAR(20) NOT NULL,
        actionid NVARCHAR(20) NOT NULL,
        enabled INT NOT NULL DEFAULT 1
    );
    CREATE INDEX IX_accessgroupactions_lookup ON dbo.accessgroupactions (AccessGroupPK, moduleid, actionid);
END

-- Bulletin (document types)
IF OBJECT_ID(N'dbo.Bulletin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bulletin (
        BulletinID INT IDENTITY(1,1) NOT NULL,
        BulletinName NVARCHAR(200) NOT NULL,
        UDFChar1 NVARCHAR(50) NULL,
        CONSTRAINT PK_Bulletin PRIMARY KEY (BulletinID)
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bulletin') AND name = 'UDFChar1')
        ALTER TABLE dbo.Bulletin ADD UDFChar1 NVARCHAR(50) NULL;
END

-- asset (areas)
IF OBJECT_ID(N'dbo.asset', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.asset (
        assetid INT IDENTITY(1,1) NOT NULL,
        assetname NVARCHAR(200) NULL,
        udfbit5 BIT NOT NULL DEFAULT 0,
        isup BIT NOT NULL DEFAULT 1,
        CONSTRAINT PK_asset PRIMARY KEY (assetid)
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.asset') AND name = 'udfbit5')
        ALTER TABLE dbo.asset ADD udfbit5 BIT NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.asset') AND name = 'isup')
        ALTER TABLE dbo.asset ADD isup BIT NOT NULL DEFAULT 1;
END

PRINT 'entTTSAP tables created.';
GO
```

---

## 5. MCRegistrationSA Database (LoginConnection)

**Context:** Used by `AccountController` for authentication via stored procedure.

### 5.1 Tables and Procedures

| Object                | Purpose |
|-----------------------|---------|
| users                 | User accounts (logon, Password, user_guid, Role). |
| PSPT_WebAuthenticate  | Stored procedure: validates username/password, returns result code and user_guid. |

### 5.2 Create MCRegistrationSA Tables and Stored Procedure

```sql
USE [MCRegistrationSA];
GO

-- users table
IF OBJECT_ID(N'dbo.users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.users (
        Id INT IDENTITY(1,1) NOT NULL,
        logon NVARCHAR(256) NOT NULL,
        Password NVARCHAR(256) NOT NULL,
        user_guid NVARCHAR(100) NOT NULL,
        Role NVARCHAR(50) NOT NULL,
        CONSTRAINT PK_users PRIMARY KEY (Id)
    );
    CREATE UNIQUE INDEX IX_users_logon ON dbo.users(logon);
END

-- PSPT_WebAuthenticate stored procedure
-- Called by AccountController with @Username, @Password, @UserGuid (input)
-- Returns result set: result code (col 0), user_guid (col "user_guid")
-- -600 or -2300 = invalid credentials; otherwise success
IF OBJECT_ID(N'dbo.PSPT_WebAuthenticate', N'P') IS NOT NULL
    DROP PROCEDURE dbo.PSPT_WebAuthenticate;
GO
CREATE PROCEDURE dbo.PSPT_WebAuthenticate
    @Username NVARCHAR(256),
    @Password NVARCHAR(256),
    @UserGuid NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @uid NVARCHAR(100);
    DECLARE @ResultCode NVARCHAR(20) = N'-600'; -- default: invalid

    SELECT @uid = user_guid
    FROM dbo.users
    WHERE logon = @Username AND Password = @Password;

    IF @uid IS NOT NULL
    BEGIN
        SET @ResultCode = N'0';
    END
    ELSE
    BEGIN
        SET @ResultCode = N'-2300'; -- wrong username or password
    END

    SELECT @ResultCode AS result_code, ISNULL(@uid, @UserGuid) AS user_guid;
END
GO

PRINT 'MCRegistrationSA tables and PSPT_WebAuthenticate created.';
```

---

## 6. Seed Data Scripts

After creating the schema, run the seed scripts to add test data:

| Script                         | Database   | Contents |
|--------------------------------|------------|----------|
| `Scripts/SeedTestLogin_entTTSAP.sql` | entTTSAP   | labor, department, accessgroupactions + test user for Development login (test/test) |
| `Scripts/SeedTestData_entTTSAP.sql`  | entTTSAP   | Extra departments, asset (areas), Bulletin (document types) |
| `Scripts/SeedTestData_DocRet.sql`    | DocRet     | Areas (HR, Production, Maintenance, etc.) |

**Development login:** In Development mode, the app allows `test` / `test` as a bypass that uses a fixed GUID. For this to work with entTTSAP, run `SeedTestLogin_entTTSAP.sql` so the labor table has a row with that GUID (AccessGroupPK 19 = Admin).

**Production login:** Add a user to `MCRegistrationSA.dbo.users` with matching `user_guid` in `entTTSAP.dbo.labor` so the login flow works end-to-end.

---

## 7. Connection String Example

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=DocRet;Trusted_Connection=True;TrustServerCertificate=True;",
    "entTTSAPConnection": "Server=.;Database=entTTSAP;Trusted_Connection=True;TrustServerCertificate=True;",
    "LoginConnection": "Server=.;Database=MCRegistrationSA;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

Adjust `Server=` for your SQL Server instance (e.g. `Server=SERVERNAME\\SQLEXPRESS`).
