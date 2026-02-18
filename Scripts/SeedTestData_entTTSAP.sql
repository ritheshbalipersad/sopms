-- ============================================================
-- Test data for SOPMSApp (entTTSAP database)
-- Run against: entTTSAP (entTTSAPConnection)
-- Adds: departments, areas (asset), document types (Bulletin)
-- Idempotent: safe to run multiple times.
-- ============================================================
USE [entTTSAP];
GO

-- ========== DEPARTMENTS ==========
-- Ensure additional test departments exist (GetDepartmentsAsync uses active = 1)
IF NOT EXISTS (SELECT 1 FROM dbo.department WHERE DepartmentID = 'ENG')
    INSERT INTO dbo.department (DepartmentID, DepartmentName, SupervisorName, active)
    VALUES ('ENG', 'Engineering', 'Engineering Supervisor', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.department WHERE DepartmentID = 'QA')
    INSERT INTO dbo.department (DepartmentID, DepartmentName, SupervisorName, active)
    VALUES ('QA', 'Quality Assurance', 'QA Supervisor', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.department WHERE DepartmentID = 'HR')
    INSERT INTO dbo.department (DepartmentID, DepartmentName, SupervisorName, active)
    VALUES ('HR', 'Human Resources', 'HR Supervisor', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.department WHERE DepartmentID = 'MNT')
    INSERT INTO dbo.department (DepartmentID, DepartmentName, SupervisorName, active)
    VALUES ('MNT', 'Maintenance', 'Maintenance Supervisor', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.department WHERE DepartmentID = 'OPS')
    INSERT INTO dbo.department (DepartmentID, DepartmentName, SupervisorName, active)
    VALUES ('OPS', 'Operations', 'Operations Supervisor', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.department WHERE DepartmentID = 'SAF')
    INSERT INTO dbo.department (DepartmentID, DepartmentName, SupervisorName, active)
    VALUES ('SAF', 'Safety', 'Safety Supervisor', 1);
PRINT 'Departments seeded.';

-- ========== ASSET (Areas) ==========
-- FileUploadController / StructuredSopController / SopAreaController / HomeController:
-- SELECT assetname AS AreaName FROM asset WHERE udfbit5 = 1 AND isup = 1
IF OBJECT_ID(N'dbo.asset', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.asset (
        assetid INT IDENTITY(1,1) NOT NULL,
        assetname NVARCHAR(200) NULL,
        udfbit5 BIT NOT NULL DEFAULT 0,
        isup BIT NOT NULL DEFAULT 1,
        CONSTRAINT PK_asset PRIMARY KEY (assetid)
    );
    PRINT 'Created table dbo.asset';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.asset') AND name = 'udfbit5')
    BEGIN
        ALTER TABLE dbo.asset ADD udfbit5 BIT NOT NULL DEFAULT 0;
        PRINT 'Added column dbo.asset.udfbit5';
    END
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.asset') AND name = 'isup')
    BEGIN
        ALTER TABLE dbo.asset ADD isup BIT NOT NULL DEFAULT 1;
        PRINT 'Added column dbo.asset.isup';
    END
END

-- Insert test areas (only if not already present by name)
IF NOT EXISTS (SELECT 1 FROM dbo.asset WHERE assetname = N'Production Floor')
    INSERT INTO dbo.asset (assetname, udfbit5, isup) VALUES (N'Production Floor', 1, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.asset WHERE assetname = N'Warehouse')
    INSERT INTO dbo.asset (assetname, udfbit5, isup) VALUES (N'Warehouse', 1, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.asset WHERE assetname = N'Lab')
    INSERT INTO dbo.asset (assetname, udfbit5, isup) VALUES (N'Lab', 1, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.asset WHERE assetname = N'Office')
    INSERT INTO dbo.asset (assetname, udfbit5, isup) VALUES (N'Office', 1, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.asset WHERE assetname = N'Maintenance Shop')
    INSERT INTO dbo.asset (assetname, udfbit5, isup) VALUES (N'Maintenance Shop', 1, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.asset WHERE assetname = N'Receiving')
    INSERT INTO dbo.asset (assetname, udfbit5, isup) VALUES (N'Receiving', 1, 1);
PRINT 'Areas (asset) seeded.';

-- ========== BULLETIN (Document types) ==========
-- GetDistinctDocumentsAsync: SELECT BulletinName AS DocumentType FROM Bulletin
-- GetSopAcronymAsync: SELECT UDFChar1 FROM Bulletin WHERE BulletinName = @docType
IF OBJECT_ID(N'dbo.Bulletin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bulletin (
        BulletinID INT IDENTITY(1,1) NOT NULL,
        BulletinName NVARCHAR(200) NOT NULL,
        UDFChar1 NVARCHAR(50) NULL,
        CONSTRAINT PK_Bulletin PRIMARY KEY (BulletinID)
    );
    PRINT 'Created table dbo.Bulletin';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Bulletin') AND name = 'UDFChar1')
    BEGIN
        ALTER TABLE dbo.Bulletin ADD UDFChar1 NVARCHAR(50) NULL;
        PRINT 'Added column dbo.Bulletin.UDFChar1';
    END
END

-- Insert document types (BulletinName = DocType in app; UDFChar1 = acronym for SOP numbers)
IF NOT EXISTS (SELECT 1 FROM dbo.Bulletin WHERE BulletinName = N'SOP')
    INSERT INTO dbo.Bulletin (BulletinName, UDFChar1) VALUES (N'SOP', N'SOP');
IF NOT EXISTS (SELECT 1 FROM dbo.Bulletin WHERE BulletinName = N'Work Instruction')
    INSERT INTO dbo.Bulletin (BulletinName, UDFChar1) VALUES (N'Work Instruction', N'WI');
IF NOT EXISTS (SELECT 1 FROM dbo.Bulletin WHERE BulletinName = N'Procedures')
    INSERT INTO dbo.Bulletin (BulletinName, UDFChar1) VALUES (N'Procedures', N'PRC');
IF NOT EXISTS (SELECT 1 FROM dbo.Bulletin WHERE BulletinName = N'Form')
    INSERT INTO dbo.Bulletin (BulletinName, UDFChar1) VALUES (N'Form', N'FRM');
IF NOT EXISTS (SELECT 1 FROM dbo.Bulletin WHERE BulletinName = N'Policy')
    INSERT INTO dbo.Bulletin (BulletinName, UDFChar1) VALUES (N'Policy', N'POL');
IF NOT EXISTS (SELECT 1 FROM dbo.Bulletin WHERE BulletinName = N'General')
    INSERT INTO dbo.Bulletin (BulletinName, UDFChar1) VALUES (N'General', N'GEN');
PRINT 'Document types (Bulletin) seeded.';

GO
PRINT 'entTTSAP test data script completed.';
