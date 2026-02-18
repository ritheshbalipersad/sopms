# SOPMSApp – Databases and Tables

This document lists the **databases** and **tables** (and stored procedures) used by SOPMSApp. Database names are set in **appsettings.json** under **ConnectionStrings**; the table below shows the connection key and the typical database name.

---

## Connection strings (appsettings.json)

| Connection key         | Typical database   | Used by                          |
|------------------------|--------------------|-----------------------------------|
| **DefaultConnection**  | DocRet or entTTSAP | ApplicationDbContext, some raw SQL |
| **entTTSAPConnection** | entTTSAP           | entTTSAPDbContext, raw SQL       |
| **LoginConnection**    | MCRegistrationSA   | Login/auth stored procedure      |

---

## 1. DefaultConnection database

**Typical name:** `DocRet` (or `entTTSAP` if you point this connection at the same server as entTTSAP.)

**DbContext:** `ApplicationDbContext`

### Tables

| Table name            | Model / usage |
|-----------------------|----------------|
| **Areas**             | Area – SOP areas (e.g. HR, Production, Maintenance). |
| **DocArchives**       | DocArchive – archived document metadata. |
| **DocRegisterHistories** | DocRegisterHistory – revision history for document registers. |
| **DocRegisters**      | DocRegister – main document/SOP register (metadata, file paths, status). |
| **DeletedFileLogs**   | DeletedFileLog – log of soft-deleted files. |
| **SopSteps**          | SopStep – steps within a structured SOP. |
| **SopStepHistory**    | SopStepHistories – history for SOP step changes. |
| **StructuredSops**     | StructuredSop – structured SOP definitions. |
| **StructuredSopHistory** | StructuredSopHistories – history for structured SOP changes. |

### Stored procedures

| Procedure name           | Usage |
|--------------------------|--------|
| **UpdateReviewStatus**   | Called from MetaController to update review status. |
| **sp_InsertPendingSOPEmail** | Called from DocRegisterService when syncing a structured SOP to DocRegister (pending approval email). |

---

## 2. entTTSAPConnection database

**Typical name:** `entTTSAP`

**DbContext:** `entTTSAPDbContext` (and raw SQL in several controllers)

Used for labor/department/areas and document-type lookups from the TTSAP system.

### Tables

| Table name              | Model / usage |
|-------------------------|----------------|
| **labor**               | Labor user info (LaborID, LaborName, user_guid, department, email, access group, etc.). Used for login role and user details. |
| **department**          | Departments (DepartmentID, DepartmentName, SupervisorName, active). Used for dropdowns and filters. |
| **accessgroupactions**  | Access/permissions (AccessGroupPK, moduleid, actionid, enabled). Used to derive role (Admin, Manager, Technician, User). |
| **Bulletin**            | Document types (BulletinName, UDFChar1, etc.). Referenced as `Documents` in entTTSAPDbContext. |
| **asset**               | Areas from TTSAP (assetname, udfbit5, isup). Used for area dropdowns in some flows. |

### Stored procedures

None in this database are called by the app; all usage is table access (EF or raw SQL).

---

## 3. LoginConnection database

**Typical name:** `MCRegistrationSA`

**DbContext:** `AppDbContext` (DbSet: `users`)

Used only for authentication.

### Tables

| Table name | Model / usage |
|------------|----------------|
| **users** (or equivalent) | User/account data. The app does not query this directly; authentication is done via the stored procedure below. |

*Exact table names depend on the implementation of `PSPT_WebAuthenticate`.*

### Stored procedures

| Procedure name          | Usage |
|-------------------------|--------|
| **PSPT_WebAuthenticate** | Called from AccountController with @Username, @Password, @UserGuid. Returns result code and user_guid for valid logins. |

---

## Summary by database

| Database (typical name) | Tables | Stored procedures |
|-------------------------|--------|--------------------|
| **DocRet** (DefaultConnection) | Areas, DocArchives, DocRegisterHistories, DocRegisters, DeletedFileLogs, SopSteps, SopStepHistory, StructuredSops, StructuredSopHistory | UpdateReviewStatus, sp_InsertPendingSOPEmail |
| **entTTSAP** (entTTSAPConnection) | labor, department, accessgroupactions, Bulletin, asset | — |
| **MCRegistrationSA** (LoginConnection) | users (or as used by PSPT_WebAuthenticate) | PSPT_WebAuthenticate |

---

## Test data scripts

To populate base data for testing (departments, areas, document types), run these SQL scripts against the correct database:

| Script | Database | Contents |
|--------|----------|----------|
| **Scripts/SeedTestLogin_entTTSAP.sql** | entTTSAP | Test user (test/test), one department, accessgroupactions. Run first for Development login. |
| **Scripts/SeedTestData_entTTSAP.sql** | entTTSAP | Extra departments (Engineering, QA, HR, Maintenance, Operations, Safety), **asset** (areas), **Bulletin** (document types: SOP, Work Instruction, Procedures, Form, Policy, General). |
| **Scripts/SeedTestData_DocRet.sql** | DocRet (DefaultConnection) | **Areas** table (if missing) and area rows: HR, Production, Maintenance, Engineering, Quality, Safety, Warehouse, Lab, Operations. |

Run **SeedTestData_entTTSAP.sql** and **SeedTestData_DocRet.sql** after migrations (and after **SeedTestLogin_entTTSAP.sql** if using the test login). All scripts are idempotent.

---

## Notes

- **DefaultConnection** may be set to the same database as **entTTSAP** (e.g. `entTTSAP`) in some environments; in that case both ApplicationDbContext tables and entTTSAP tables live in one database. The list above is by *connection*, not by physical server.
- EF Core migrations in this solution apply only to **ApplicationDbContext** (see `Migrations/ApplicationDb`). Tables in **entTTSAP** and **MCRegistrationSA** are assumed to exist (created by TTSAP or the registration system).
- For a development/test login that bypasses MCRegistrationSA, see the test login setup (e.g. `Scripts/SeedTestLogin_entTTSAP.sql` and Development-only bypass in AccountController).
