-- ============================================================
-- Test login seed for SOPMSApp (entTTSAP database)
-- Run this against the database used by entTTSAPConnection
-- (e.g. Server=localhost; Database=entTTSAP)
--
-- After running, use Development test login:
--   Username: test
--   Password: test
-- ============================================================
USE [entTTSAP];
GO

-- Test user GUID must match AccountController (Development bypass)
DECLARE @TestUserGuid UNIQUEIDENTIFIER = 'A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D';

-- Create labor table if it does not exist (minimal columns for GetLaborUserInfoAsync)
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
    PRINT 'Created table dbo.labor';
END

-- Create department table if it does not exist (for LEFT JOIN and GetDepartmentsAsync)
IF OBJECT_ID(N'dbo.department', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.department (
        DepartmentID NVARCHAR(50) NOT NULL,
        DepartmentName NVARCHAR(100) NULL,
        SupervisorName NVARCHAR(100) NULL,
        active INT NOT NULL DEFAULT 1,
        CONSTRAINT PK_department PRIMARY KEY (DepartmentID)
    );
    PRINT 'Created table dbo.department';
END
ELSE
BEGIN
    -- Ensure active column exists (required by FileUploadController.GetDepartmentsAsync)
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.department') AND name = 'active')
    BEGIN
        ALTER TABLE dbo.department ADD active INT NOT NULL DEFAULT 1;
        PRINT 'Added column dbo.department.active';
    END
END

-- Create accessgroupactions if it does not exist (for Role = Admin)
IF OBJECT_ID(N'dbo.accessgroupactions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.accessgroupactions (
        AccessGroupPK NVARCHAR(20) NOT NULL,
        moduleid NVARCHAR(20) NOT NULL,
        actionid NVARCHAR(20) NOT NULL,
        enabled INT NOT NULL DEFAULT 1
    );
    CREATE INDEX IX_accessgroupactions_lookup ON dbo.accessgroupactions (AccessGroupPK, moduleid, actionid);
    PRINT 'Created table dbo.accessgroupactions';
END

-- Insert test department if not exists
IF NOT EXISTS (SELECT 1 FROM dbo.department WHERE DepartmentID = 'TEST')
BEGIN
    INSERT INTO dbo.department (DepartmentID, DepartmentName, SupervisorName, active)
    VALUES ('TEST', 'Test Department', 'Test Supervisor', 1);
    PRINT 'Inserted test department';
END

-- Insert accessgroupactions so test user gets Admin role (AccessGroupPK 19 = Admin in app logic)
IF NOT EXISTS (SELECT 1 FROM dbo.accessgroupactions WHERE AccessGroupPK = '19' AND moduleid = 'ZS')
BEGIN
    INSERT INTO dbo.accessgroupactions (AccessGroupPK, moduleid, actionid, enabled)
    VALUES ('19', 'ZS', 'SOP20', 1),
           ('19', 'ZS', 'SOP30', 1);
    PRINT 'Inserted accessgroupactions for Admin role';
END

-- Insert or update test labor user (idempotent)
IF NOT EXISTS (SELECT 1 FROM dbo.labor WHERE user_guid = @TestUserGuid)
BEGIN
    INSERT INTO dbo.labor (
        LaborID, LaborName, user_guid, labortype, active,
        departmentname, DepartmentID, email, craftname,
        SupervisorID, SupervisorName, ShopID, ShopName,
        accessgrouppk, [Access]
    )
    VALUES (
        'TEST01', 'Test User', @TestUserGuid, 'EMP', 1,
        'Test Department', 'TEST', 'test@example.com', 'Test',
        NULL, 'Test Supervisor', NULL, NULL,
        '19', '1'
    );
    PRINT 'Inserted test labor user (user_guid = ' + CAST(@TestUserGuid AS NVARCHAR(36)) + ')';
END
ELSE
BEGIN
    PRINT 'Test labor user already exists.';
END

GO
PRINT 'Done. Use username: test, password: test when running in Development.';
