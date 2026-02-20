-- Document audit trail table (DocRet / DefaultConnection)
-- Run if migration is not used.
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
    CREATE INDEX [IX_DocumentAuditLogs_DocRegisterId] ON [dbo].[DocumentAuditLogs] ([DocRegisterId]);
    CREATE INDEX [IX_DocumentAuditLogs_SopNumber] ON [dbo].[DocumentAuditLogs] ([SopNumber]);
    CREATE INDEX [IX_DocumentAuditLogs_PerformedAtUtc] ON [dbo].[DocumentAuditLogs] ([PerformedAtUtc]);
    PRINT 'Created table DocumentAuditLogs';
END
GO
