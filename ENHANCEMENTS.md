# SOPMSApp – Enhancement Suggestions

This document suggests improvements to the application, with a focus on **admin-maintainable reference data** (areas, departments, document types) and related features.

---

## 1. Admin Section – Reference Data Maintenance (High value)

Currently, **Areas**, **Departments**, and **Document Types** are maintained only via SQL scripts (`Scripts/SeedTestData_*.sql`) or external systems (TTSAP). An admin UI would let authorized users manage these without touching the database.

### 1.1 Admin Area (ASP.NET Core Area)

- Add an **Admin** Area (e.g. `Areas/Admin/`) restricted to `User.IsInRole("Admin")`.
- Add an **Admin** link in the sidebar (only for Admin users), e.g. under “Document Management” or in its own “Administration” section.
- Use a single **AdminController** (or separate controllers per entity) under `Areas/Admin/Controllers/`.

### 1.2 Areas Maintenance (DocRet / DefaultConnection)

- **Table:** `Areas` (ApplicationDbContext) – columns `Id`, `AreaName`.
- **Pages:** List (with optional search), Create, Edit, Delete (soft or hard).
- **Behaviour:** Standard CRUD via ApplicationDbContext; validate duplicate names; consider “in use” check (e.g. DocRegisters or StructuredSops referencing the area) before delete.
- **Benefit:** Areas used in structured SOPs and filters can be updated without running SQL or redeploying.

### 1.3 Departments Maintenance (entTTSAP)

- **Table:** `department` (entTTSAP) – `DepartmentID`, `DepartmentName`, `SupervisorName`, `active`.
- **Pages:** List (filter by active/inactive), Create, Edit, Toggle active.
- **Behaviour:** Use raw SQL or a dedicated DbContext for entTTSAP; avoid changing `department` if it is the system of record from TTSAP (in that case, make the UI read-only or “override” only with a clear note). If SOPMS is the owner of this data, full CRUD is appropriate.
- **Benefit:** Upload and filter dropdowns stay in sync without script runs.

### 1.4 Document Types (Bulletin) Maintenance (entTTSAP)

- **Table:** `Bulletin` (entTTSAP) – at least `BulletinName`, `UDFChar1` (acronym for SOP numbers).
- **Pages:** List, Create, Edit, Delete (with “in use” check if possible).
- **Behaviour:** Ensure new document types get a sensible `UDFChar1` (e.g. SOP, WI, PRC) so SOP number generation and display stay correct.
- **Benefit:** New doc types (e.g. “Checklist”, “Template”) without DB scripts.

### 1.5 Areas from TTSAP (asset table) – Optional

- **Table:** `asset` (entTTSAP) – `assetname`, `udfbit5`, `isup` (used in area dropdowns in FileUpload, StructuredSop, etc.).
- **Options:**
  - **Read-only list** in Admin: show which areas are currently used for dropdowns (`udfbit5 = 1 AND isup = 1`).
  - **Lightweight maintenance:** Add/Edit/Deactivate entries used only by SOPMS (if your policy allows writing to `asset`). If TTSAP owns `asset`, keep this read-only or hide it.
- **Benefit:** Transparency and, if applicable, control over area list without SQL.

### 1.6 Single “Admin” Landing Page

- **Admin/Index:** Dashboard with cards/links to:
  - Maintain Areas (DocRet)
  - Maintain Departments (entTTSAP)
  - Maintain Document Types (Bulletin)
  - (Optional) View/Manage asset areas
  - Link to existing **Deleted Files / Document Archives** (already Admin-only).
- Keeps all reference-data and admin-only actions in one place.

---

## 2. Other Suggested Enhancements

### 2.1 Dashboard and Reporting

- **Home/Index:** Add summary cards (e.g. total SOPs, pending approvals, documents due for review, recent activity).
- **Review due soon:** List or alert for documents where `LastReviewDate` or similar is within the next 30/60/90 days (if you have that field).
- **Department/Area stats:** Reuse or extend existing department/area views with counts (e.g. documents per department, per area).

### 2.2 Search and Filtering

- **Global search:** Search by SOP number, title, department, area, or document type across DocRegisters and optionally StructuredSops; results page with links to view/edit.
- **Meta / Master Table:** Stronger filters (by status, date range, department, area, document type) and export (CSV/Excel) for reporting.

### 2.3 Notifications and Reminders

- **Review reminders:** Optional email or in-app list for “documents due for review” (if DB supports it or you add a background job).
- **Approval notifications:** You already have flows for pending and final approval; ensure stored procedures (`sp_InsertPendingSOPEmail`, `sp_InsertAuthorFinalApprovalNotification`) exist in the DB or that the app degrades gracefully when they are missing (as with `UpdateReviewStatus`).

### 2.4 Audit and Compliance

- **Audit log:** Table (e.g. `AuditLog`) recording who created/updated/approved/deleted which document or SOP and when. Admin page to view/filter and export.
- **Change history:** You have DocRegisterHistory and StructuredSopHistory; expose a “History” tab or page per document so users can see revisions and approval steps without going to DB.

### 2.5 UX and Performance

- **Bulk actions:** On Meta or list views: multi-select and bulk status change, bulk “assign for review”, or bulk export.
- **Caching:** Cache reference data (areas, departments, document types) for a short period (e.g. 5–10 minutes) to reduce repeated SQL when many users use dropdowns.
- **Stored procedure fallbacks:** Where procedures are optional (e.g. UpdateReviewStatus), keep try/catch and log; consider same pattern for notification procedures so missing procs don’t break workflows.

### 2.6 Security and Configuration

- **Admin-only by role:** Ensure all new Admin endpoints and pages check `User.IsInRole("Admin")` (or use `[Authorize(Roles = "Admin")]`).
- **Configuration:** Optional admin page or config file to toggle features (e.g. “Allow department maintenance in SOPMS” vs “Read-only from TTSAP”).

---

## 3. Implementation Order (Suggested)

| Priority | Item | Effort | Notes |
|----------|------|--------|--------|
| 1 | Admin Area + landing page (Admin/Index) | Small | Route and menu for Admin only. |
| 2 | Areas CRUD (DocRet) | Small | Uses ApplicationDbContext; single table. |
| 3 | Document Types (Bulletin) CRUD (entTTSAP) | Small | Raw SQL or entTTSAP DbContext. |
| 4 | Departments list + edit (entTTSAP) | Small–Medium | Decide read-only vs CRUD based on TTSAP ownership. |
| 5 | asset (areas) read-only or light edit | Small | Optional; depends on who owns `asset`. |
| 6 | Dashboard stats on Home | Small | Counts and “due for review” if you have the data. |
| 7 | Global search | Medium | Cross-table search and results view. |
| 8 | Audit log table + admin view | Medium | New table + write from key actions. |

---

## 4. Quick Win: Add “Administration” to the sidebar

Minimal change to expose a future Admin section:

In **Views/Shared/_Layout.cshtml**, inside the block where `User.IsInRole("Admin")` is true (e.g. after “Document Archives” or “Batch Upload”), add:

```html
<li class="sidebar-menu-item">
    <a asp-area="Admin" asp-controller="Home" asp-action="Index" class="d-flex justify-content-between align-items-center hover-lift">
        <span><i class="fas fa-cog me-2"></i> Administration</span>
        <i class="fas fa-chevron-right small"></i>
    </a>
</li>
```

Then add an **Admin** Area with a simple **HomeController** that returns a view with cards linking to “Maintain Areas”, “Maintain Departments”, “Maintain Document Types” (and optionally “Document Archives”). Each link can point to placeholder actions that you replace with real CRUD as you implement them.

---

## 5. Data Source Summary (for implementation)

| Data | Database | Table | Context / Access |
|------|----------|--------|-------------------|
| Areas (app) | DocRet (DefaultConnection) | Areas | ApplicationDbContext |
| Departments | entTTSAP | department | Raw SQL or entTTSAPDbContext |
| Document types | entTTSAP | Bulletin | Raw SQL or entTTSAPDbContext |
| Areas (dropdown) | entTTSAP | asset | Raw SQL (`udfbit5=1`, `isup=1`) |

Use **ConnectionStrings:DefaultConnection** for DocRet and **ConnectionStrings:entTTSAPConnection** for entTTSAP when implementing services or repositories for the Admin CRUD pages.
