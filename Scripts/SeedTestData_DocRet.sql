-- ============================================================
-- Test Areas for SOPMSApp (DocRet / DefaultConnection database)
-- Run against: DocRet (DefaultConnection)
-- ApplicationDbContext.Areas - used for structured SOPs and area assignments.
-- Idempotent: safe to run multiple times.
-- ============================================================
USE [DocRet];
GO

-- Ensure Areas table exists (matches Program.cs / migrations)
IF OBJECT_ID(N'dbo.Areas', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Areas] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [AreaName] NVARCHAR(MAX) NOT NULL,
        CONSTRAINT [PK_Areas] PRIMARY KEY ([Id])
    );
    PRINT 'Created table dbo.Areas';
END

-- Seed additional areas if not already present (app may have already seeded HR, Production, Maintenance)
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'HR')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'HR');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Production')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Production');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Maintenance')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Maintenance');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Engineering')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Engineering');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Quality')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Quality');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Safety')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Safety');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Warehouse')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Warehouse');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Lab')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Lab');
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE AreaName = N'Operations')
    INSERT INTO dbo.Areas (AreaName) VALUES (N'Operations');
PRINT 'Areas seeded.';

GO
PRINT 'DocRet test data script completed.';
