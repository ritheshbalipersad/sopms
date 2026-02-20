using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.Blazor;
using SOPMSApp.Data;
using SOPMSApp.Extensions;
using SOPMSApp.Models;
using SOPMSApp.Services;
using SOPMSApp.ViewModels;
using System.Data;
using System.Security.Claims;

namespace SOPMSApp.Controllers
{
    public class ApprovalsController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IConverter _pdfConverter;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly ILogger<StructuredSopController> _logger;
        private readonly IDocumentAuditLogService _auditLog;
        private readonly string _storageRoot;


        public ApprovalsController(IConfiguration config, ApplicationDbContext context, IWebHostEnvironment hostingEnvironment, IConverter pdfConverter, IConfiguration configuration, ILogger<StructuredSopController> logger, IDocumentAuditLogService auditLog)
        {
            _config = config;
            _logger = logger;
            _context = context;
            _configuration = configuration;
            _pdfConverter = pdfConverter;
            _hostingEnvironment = hostingEnvironment;
            _auditLog = auditLog;
            _storageRoot = config["StorageSettings:BasePath"];
        }

        // View all pending approvals and deletions
        public async Task<IActionResult> Approvals()
        {
            try
            {
                var userDept = User.FindFirst("Department")?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value ??
                               User.FindFirst("Role")?.Value ?? "User";


                bool isManager = userRole.Equals("Manager", StringComparison.OrdinalIgnoreCase);
                bool isAdmin = userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);

                // Run one query at a time ✅
                var pendingApprovals = await _context.DocRegisters
                    .Where(d => d.IsArchived != true)
                    .Where(d =>
                        (isManager && (d.Status == "Pending Approval" ||
                                       d.Status == "Pending Manager Approval" ||
                                       d.Status == "Under Review")) ||
                        (isAdmin && (d.Status == "Pending Admin Approval" ||
                                     d.Status == "Pending Approval" ||
                                     d.Status == "Under Review")) ||
                        (!isManager && !isAdmin && User.IsInRole("Approver") &&
                         (d.Status == "Pending Approval" || d.Status == "Under Review"))
                    )
                    .OrderBy(d => d.UploadDate)
                    .ToListAsync();

                var pendingSopApprovals = await _context.StructuredSops
                    .Where(d => d.ArchivedOn == null)
                    .Where(d =>
                        (isManager && (d.Status == "Pending Approval" ||
                                       d.Status == "Pending Manager Approval" ||
                                       d.Status == "Under Review")) ||
                        (isAdmin && (d.Status == "Pending Admin Approval" ||
                                     d.Status == "Pending Approval" ||
                                     d.Status == "Under Review")) ||
                        (!isManager && !isAdmin && User.IsInRole("Approver") &&
                         (d.Status == "Pending Approval" || d.Status == "Under Review"))
                    )
                    .Include(s => s.Steps)
                    .OrderBy(d => d.CreatedAt)
                    .ToListAsync();

                var pendingDeletions = await _context.DocRegisters
                    .Where(d => d.Status == "Pending Deletion" && d.IsArchived != true)
                    .Where(d => isAdmin || isManager)
                    .OrderBy(d => d.UploadDate)
                    .ToListAsync();

                var model = new ApprovalViewModel
                {
                    PendingApprovals = pendingApprovals,
                    PendingSopApprovals = pendingSopApprovals,
                    PendingDeletions = pendingDeletions
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading approvals page for user {UserName}", User.Identity?.Name);
                return Content($"Error: {ex.Message}");
            }
        }

        // View single document (e.g. modal or detail view)
        public IActionResult IndexView(int id)
        {
            var sop = _context.DocRegisters.FirstOrDefault(d => d.Id == id);

            if (sop == null)
            {
                return NotFound();
            }
            
            // Define both file paths
            var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "upload", sop.FileName);
            var originalsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Originals", sop.FileName);

            // Check file existence
            if (!System.IO.File.Exists(uploadPath) && !System.IO.File.Exists(originalsPath))
            {
                ViewBag.FileError = "The document file was not found in either the 'upload' or 'Originals' folder.";
            }

            return View("IndexView", sop); // optionally pass as a ViewModel if needed
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveDocument(string sopNumber)
        {
            if (string.IsNullOrWhiteSpace(sopNumber))
            {
                TempData["Error"] = "Invalid SOP Number.";
                return RedirectToAction("Approvals");
            }

            try
            {
                var user = await GetUserInfoAsync();
                var sop = await GetSopDataAsync(sopNumber);

                if (sop == null)
                {
                    TempData["Error"] = $"SOP '{sopNumber}' not found.";
                    return RedirectToAction("Approvals");
                }

                // 🔐 Authorization check
                if (!IsAuthorizedToApprove(user, sop))
                {
                    string supervisor = sop.StructuredSop?.DepartmentSupervisor ?? sop.DocRegister?.DepartmentSupervisor ?? "N/A";
                    TempData["Error"] = $"You are not authorized to approve this document. Only the assigned Department Manager : ({supervisor}) may approve.";
                    _logger.LogWarning("Unauthorized approval attempt by {UserName} for SOP {SopNumber}", user.Name, sopNumber);
                    return RedirectToAction("Approvals");
                }

                // ⚡ Execute approval
                var result = await ExecuteApprovalAsync(sop, user);

                if (result.IsSuccess)
                    TempData["Success"] = result.Message;
                else
                    TempData["Warning"] = result.Message;
            }
            catch (NotificationException nex)
            {
                // Admin notification failed but approval might still be applied
                _logger.LogError(nex, "Notification error approving SOP {SopNumber}", sopNumber);
                TempData["Warning"] = "Approval recorded, but admin notification failed.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving SOP {SopNumber}", sopNumber);
                TempData["Error"] = "An unexpected error occurred during approval. Please try again or contact IT.";
            }

            return RedirectToAction("Approvals");
        }


        // ===================== HELPER METHODS =====================
        private bool IsAuthorizedToApprove(UserInfo user, SopData sop)
        {
            string role = user.Role?.Trim() ?? "User";

            // 🔐 Admin can approve everything
            if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                return true;

            // 🔐 Manager can approve only if they are the assigned Department Supervisor
            if (role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
            {
                string supervisor = sop.StructuredSop?.DepartmentSupervisor
                                    ?? sop.DocRegister?.DepartmentSupervisor
                                    ?? "";

                // If no supervisor is assigned or marked as N/A, optionally allow approval
                if (string.IsNullOrWhiteSpace(supervisor) || supervisor.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                    return true;

                return string.Equals(user.Name?.Trim(), supervisor.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            // ❌ Everyone else cannot approve
            return false;
        }

        private async Task<UserInfo> GetUserInfoAsync()
        {
            return new UserInfo
            {
                Name = User.FindFirst("LaborName")?.Value ?? User.Identity?.Name ?? "Unknown User",
                Department = User.FindFirst("Department")?.Value?.Trim(),
                Role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("Role")?.Value ?? "User"
            };
        }

        private async Task<SopData> GetSopDataAsync(string sopNumber)
        {
            if (string.IsNullOrWhiteSpace(sopNumber))
                return null;

            var normalized = sopNumber.Trim().ToLower();

            var structuredSop = await _context.StructuredSops
                .Include(s => s.Steps)
                .FirstOrDefaultAsync(s => s.SopNumber.ToLower().Trim() == normalized);

            var docRegister = await _context.DocRegisters
                .FirstOrDefaultAsync(d => d.SopNumber.ToLower().Trim() == normalized
                                       && d.Status != "Approved"
                                       && (d.IsArchived == null || d.IsArchived == false));

            if (structuredSop == null && docRegister == null)
                return null;

            return new SopData
            {
                StructuredSop = structuredSop,
                DocRegister = docRegister,
                Department = docRegister?.Department ?? structuredSop?.ControlledBy
            };
        }

        private async Task<ApprovalResult> ExecuteApprovalAsync(SopData sop, UserInfo user)
        {
            var now = DateTime.Now;

            try
            {
                if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
                    return await HandleManagerApprovalAsync(sop, user, now);

                if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    return await HandleAdminApprovalAsync(sop, user, now);

                return ApprovalResult.Failure("You do not have approval permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing approval for SOP {SopNumber}", sop.StructuredSop?.SopNumber ?? sop.DocRegister?.SopNumber);
                throw;
            }
        }

        private async Task<ApprovalResult> HandleManagerApprovalAsync(SopData sop, UserInfo user, DateTime time)
        {
            try
            {
                if (sop.StructuredSop != null)
                {
                    sop.StructuredSop.Status = "Pending Admin Approval";
                    sop.StructuredSop.ApprovalStage = "Manager";
                    sop.StructuredSop.ManagerApproved = true;
                    sop.StructuredSop.ManagerApprovedDate = time;
                    sop.StructuredSop.ReviewedBy = user.Name;
                    _context.StructuredSops.Update(sop.StructuredSop);
                }

                if (sop.DocRegister != null)
                {
                    sop.DocRegister.Status = "Pending Admin Approval";
                    sop.DocRegister.ApprovalStage = "Manager";
                    sop.DocRegister.ManagerApproved = true;
                    sop.DocRegister.ManagerApprovedDate = time;
                    sop.DocRegister.ReviewedBy = user.Name;
                    _context.DocRegisters.Update(sop.DocRegister);
                }

                await _context.SaveChangesAsync();

                // Notify admins (wrap in try/catch)
                try
                {
                    var sopNumber = sop.StructuredSop?.SopNumber ?? sop.DocRegister?.SopNumber;
                    await NotifyAdminsForApprovalAsync(sopNumber, sop, user.Name, time);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Admin notification failed for SOP {SopNumber}", sop.StructuredSop?.SopNumber ?? sop.DocRegister?.SopNumber);
                }

                return ApprovalResult.Success("Manager approval recorded. Awaiting admin approval.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling manager approval");
                return ApprovalResult.Failure("Failed to record manager approval. " + ex.Message);
            }
        }


        private async Task<ApprovalResult> HandleAdminApprovalAsync(SopData sop, UserInfo user, DateTime time)
        {
            sop.SetAdminApproval(user.Name, time);

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                if (sop.StructuredSop != null)
                    _context.StructuredSops.Update(sop.StructuredSop);

                if (sop.DocRegister != null)
                    _context.DocRegisters.Update(sop.DocRegister);

                // PDF generation (optional: generate before save or after; ensure basePath is valid)
                if (sop.StructuredSop != null)
                {
                    var basePath = _configuration["StorageSettings:BasePath"];
                    string pdfFile = await GenerateAndSavePdfAsync(sop.StructuredSop, basePath);

                    if (sop.DocRegister != null)
                        sop.DocRegister.FileName = pdfFile;
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                var sopNum = sop.StructuredSop?.SopNumber ?? sop.DocRegister?.SopNumber ?? "";
                await _auditLog.LogAsync(sop.DocRegister?.Id, sopNum, "Approved", user.Name, null, sop.DocRegister?.OriginalFile ?? sop.StructuredSop?.Title);

                // 🔔 Send Final Approval Email to Uploader/Author
                try
                {
                    await NotifyAuthorFinalApprovalAsync(sopNum, sop, user.Name, time);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send final approval author notification for SOP {SopNumber}", sopNum);
                }

                return ApprovalResult.Success("SOP fully approved, PDF generated, and author notified.");

            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error handling admin approval for SOP {SopNumber}", sop.StructuredSop?.SopNumber ?? sop.DocRegister?.SopNumber);
                throw;
            }
        }

        private async Task UpdateForManagerApprovalAsync(SopData sopData, string reviewer, DateTime approvalTime)
        {
            if (sopData.StructuredSop != null)
            {
                sopData.StructuredSop.Status = "Pending Admin Approval";
                sopData.StructuredSop.ApprovalStage = "Manager";
                sopData.StructuredSop.ManagerApproved = true;
                sopData.StructuredSop.ManagerApprovedDate = approvalTime;
                sopData.StructuredSop.ReviewedBy = reviewer;
                _context.StructuredSops.Update(sopData.StructuredSop);
            }

            if (sopData.DocRegister != null)
            {
                sopData.DocRegister.Status = "Pending Admin Approval";
                sopData.DocRegister.ApprovalStage = "Manager";
                sopData.DocRegister.ManagerApproved = true;
                sopData.DocRegister.ManagerApprovedDate = approvalTime;
                sopData.DocRegister.ReviewedBy = reviewer;
                _context.DocRegisters.Update(sopData.DocRegister);
            }

            await _context.SaveChangesAsync();
        }

        private async Task NotifyAdminsForApprovalAsync(string sopNumber, SopData sopData, string approvedBy, DateTime approvalDate)
        {
            try
            {
                using var conn = _context.Database.GetDbConnection();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "sp_InsertAdminApprovalNotification";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                cmd.Parameters.Add(new SqlParameter("@SopNumber", sopNumber ?? ""));
                cmd.Parameters.Add(new SqlParameter("@Department", sopData.Department ?? ""));
                cmd.Parameters.Add(new SqlParameter("@FileName", sopData.StructuredSop?.Title ?? sopData.DocRegister?.FileName ?? ""));
                cmd.Parameters.Add(new SqlParameter("@Author", sopData.StructuredSop?.Signatures ?? sopData.DocRegister?.Author ?? ""));
                cmd.Parameters.Add(new SqlParameter("@EffectiveDate", sopData.StructuredSop?.EffectiveDate ?? sopData.DocRegister?.EffectiveDate ?? approvalDate));
                cmd.Parameters.Add(new SqlParameter("@ApprovedBy", approvedBy));
                cmd.Parameters.Add(new SqlParameter("@ApprovalDate", approvalDate));
                cmd.Parameters.Add(new SqlParameter("@Status", "Manager Approved"));

                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending admin notification for SOP {SopNumber}", sopNumber);
                // decide: swallow, log, or throw — current approach throws so caller can rollback
                throw new NotificationException("Failed to send admin notification", ex);
            }
        }

        private async Task NotifyAuthorFinalApprovalAsync(string sopNumber, SopData sopData, string approvedBy, DateTime approvalDate)
        {
            try
            {
                using var conn = _context.Database.GetDbConnection();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "sp_InsertAuthorFinalApprovalNotification";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                var structured = sopData.StructuredSop;
                var doc = sopData.DocRegister;

                cmd.Parameters.Add(new SqlParameter("@SopNumber", sopNumber ?? ""));
                cmd.Parameters.Add(new SqlParameter("@Department", sopData.Department ?? ""));

                // FileName / Title
                cmd.Parameters.Add(new SqlParameter("@FileName",
                    structured?.Title ?? doc?.OriginalFile ?? ""
                ));

                // Author name
                cmd.Parameters.Add(new SqlParameter("@Author",
                    structured?.Signatures ?? doc?.Author ?? ""
                ));

                // Author email
                cmd.Parameters.Add(new SqlParameter("@AuthorEmail",
                    structured?.UserEmail ?? doc?.UserEmail ?? ""
                ));

                // Effective date
                cmd.Parameters.Add(new SqlParameter("@EffectiveDate",
                    structured?.EffectiveDate ?? doc?.EffectiveDate ?? approvalDate
                ));

                // Approved By + Date
                cmd.Parameters.Add(new SqlParameter("@ApprovedBy", approvedBy));
                cmd.Parameters.Add(new SqlParameter("@ApprovalDate", approvalDate));

                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Author Final Approval Notification for SOP {SopNumber}", sopNumber);
                throw new NotificationException("Failed to notify author of final approval", ex);
            }
        }


        // ==================== SUPPORTING CLASSES ====================
        public class UserInfo
        {
            public string Name { get; set; }
            public string Department { get; set; }
            public string Role { get; set; }
        }

        public class SopData
        {
            public StructuredSop StructuredSop { get; set; }
            public DocRegister DocRegister { get; set; }
            public string Department { get; set; }
        }

        public class ApprovalResult
        {
            public bool IsSuccess { get; set; }
            public string Message { get; set; }

            public static ApprovalResult Success(string message) => new() { IsSuccess = true, Message = message };
            public static ApprovalResult Failure(string message) => new() { IsSuccess = false, Message = message };
        }

        public class NotificationException : Exception
        {
            public NotificationException(string message, Exception innerException) : base(message, innerException) { }
        }

       
        public async Task<string> GenerateAndSavePdfAsync(StructuredSop sop, string basePath)
        {
            string tempFooterPath = null;
            try
            {
                if (_pdfConverter == null)
                    throw new InvalidOperationException("PDF generation service is not available");

                // 1️⃣ Render main SOP HTML
                var htmlContent = await this.RenderViewToStringAsync("StructuredSopTemplate", sop);

                // 2️⃣ Render footer HTML (fallback if view is missing)
                try
                {
                    tempFooterPath = Path.Combine(Path.GetTempPath(), $"footer_{Guid.NewGuid()}.html");
                    var footerHtml = await this.RenderViewToStringAsync("_SopPdfFooter", sop);
                    await System.IO.File.WriteAllTextAsync(tempFooterPath, footerHtml);
                }
                catch
                {
                    var fallbackFooter = $@"
                <div style='text-align: center; font-size: 10px; color: #666; padding-top: 10px;'>
                    Page <span class='page'></span> of <span class='topage'></span> | 
                    SOP: {sop.SopNumber} | 
                    Effective: {sop.EffectiveDate:yyyy-MM-dd} | 
                    Approved by: {sop.ApprovedBy ?? "N/A"} ({sop.ApprovalStage ?? "N/A"})
                </div>";
                    tempFooterPath = Path.Combine(Path.GetTempPath(), $"footer_{Guid.NewGuid()}.html");
                    await System.IO.File.WriteAllTextAsync(tempFooterPath, fallbackFooter);
                }

                // 3️⃣ Build PDF document
                var pdfDoc = new HtmlToPdfDocument
                {
                    GlobalSettings = new GlobalSettings
                    {
                        ColorMode = ColorMode.Color,
                        Orientation = Orientation.Portrait,
                        PaperSize = PaperKind.A4,
                        Margins = new MarginSettings { Top = 20, Bottom = 40, Left = 15, Right = 15 },
                        DocumentTitle = $"{sop.SopNumber}_{sop.Title}"
                    },
                    Objects = {
                new ObjectSettings
                {
                    HtmlContent = htmlContent,
                    WebSettings = {
                        DefaultEncoding = "utf-8",
                        LoadImages = true,
                        EnableIntelligentShrinking = true
                    },
                    FooterSettings = new FooterSettings
                    {
                        HtmUrl = tempFooterPath,
                        Line = true,
                        FontSize = 9,
                        Spacing = 5,
                        Right = "Page [page] of [toPage]"
                    }
                }
            }
                };

                // 4️⃣ Convert to PDF bytes
                byte[] pdfBytes = _pdfConverter.Convert(pdfDoc);

                // 5️⃣ Save PDF to Originals/{DocType}/ folder
                string sanitizedDocType = string.IsNullOrWhiteSpace(sop.DocType)
                    ? "General"
                    : string.Join("_", sop.DocType.Split(Path.GetInvalidFileNameChars()));

                string folderPath = Path.Combine(basePath, "Originals", sanitizedDocType);
                Directory.CreateDirectory(folderPath);

                string CleanFileName(string name)
                {
                    foreach (char c in Path.GetInvalidFileNameChars())
                        name = name.Replace(c, '_');
                    return name;
                }

                string cleanTitle = CleanFileName(sop.Title);
                string fileName = $"{sop.SopNumber}_{cleanTitle}.pdf";
                string filePath = Path.Combine(folderPath, fileName);

                await System.IO.File.WriteAllBytesAsync(filePath, pdfBytes);

                // 6️⃣ Return relative web path for DB storage
                return Path.Combine("Documents", "Originals", sanitizedDocType, fileName).Replace("\\", "/");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating PDF for SOP {SopNumber}", sop.SopNumber);
                throw;
            }
            finally
            {
                // 7️⃣ Clean up temp footer file
                if (tempFooterPath != null && System.IO.File.Exists(tempFooterPath))
                {
                    try { System.IO.File.Delete(tempFooterPath); } catch { /* Ignore cleanup errors */ }
                }
            }
        }


        public async Task<IActionResult> ExportSopToPdf(int id)
        {
            if (_pdfConverter == null)
            {
                TempData["Error"] = "PDF generation service is not available";
                return RedirectToAction(nameof(Details), new { id });
            }

            var sop = await _context.StructuredSops
                                    .Include(s => s.Steps)
                                    .FirstOrDefaultAsync(s => s.Id == id);

            if (sop == null) return NotFound();

            string tempFooterPath = null;

            try
            {
                // Get storage base path
                var basePath = _configuration["StorageSettings:BasePath"];
                if (string.IsNullOrEmpty(basePath))
                {
                    TempData["Error"] = "Storage configuration is missing";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // 1. Render main HTML
                var htmlContent = await this.RenderViewToStringAsync("StructuredSopTemplate", sop);

                // 2. Render footer HTML
                var footerHtml = await this.RenderViewToStringAsync("_SopPdfFooter", sop);

                // 3. Save footer to temp file (needed by wkhtmltopdf)
                tempFooterPath = Path.Combine(Path.GetTempPath(), $"footer_{Guid.NewGuid()}.html");
                System.IO.File.WriteAllText(tempFooterPath, footerHtml);

                // 4. Build PDF doc
                var pdfDoc = new HtmlToPdfDocument
                {
                    GlobalSettings = new GlobalSettings
                    {
                        ColorMode = ColorMode.Color,
                        Orientation = Orientation.Portrait,
                        PaperSize = PaperKind.A4,
                        Margins = new MarginSettings { Top = 20, Bottom = 40, Left = 10, Right = 10 },
                        DocumentTitle = $"{sop.SopNumber}_{sop.Title}"
                    },
                    Objects = {
                    new ObjectSettings
                    {
                        HtmlContent = htmlContent,
                        WebSettings = {
                            DefaultEncoding = "utf-8",
                            LoadImages = true,
                            EnableIntelligentShrinking = true
                        },
                        FooterSettings = new FooterSettings
                        {
                            HtmUrl = tempFooterPath,
                            Line = true,
                            FontSize = 10,
                            Spacing = 8,
                            Right = "Page [page] of [toPage]"
                        }
                    }
                }
                };

                var pdfBytes = _pdfConverter.Convert(pdfDoc);

                // 5. Save to Originals/{DocType}/ folder
                string sanitizedDocType = string.Join("_", sop.DocType?.Split(Path.GetInvalidFileNameChars()) ?? new[] { "General" });
                string folderPath = Path.Combine(basePath, "Originals", sanitizedDocType);
                Directory.CreateDirectory(folderPath);

                string fileName = $"{sop.SopNumber}_{sop.Title}.pdf";
                string fullPath = Path.Combine(folderPath, fileName);
                await System.IO.File.WriteAllBytesAsync(fullPath, pdfBytes);

                // 6. Clean up temp file
                if (System.IO.File.Exists(tempFooterPath))
                    System.IO.File.Delete(tempFooterPath);

                // 7. Stream inline to browser (open in new tab)
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating PDF");
                return StatusCode(500, $"Error generating PDF: {ex.Message}");
            }
        }

        /*[HttpPost]
        public async Task<IActionResult> ApproveSop(int id)
        {
            try
            {
                var reviewer = User.FindFirst("LaborName")?.Value ?? "Unknown User";
                var approver = User.FindFirst("LaborName")?.Value ?? "Admin";
                // Find both documents in a single query if possible, or at least in a transaction
                var sop = await _context.StructuredSops.FindAsync(id);

                if (sop == null)
                {
                    return NotFound();
                }

                sop.Status = "Approved";
                sop.ReviewedBy = reviewer;
                sop.ApprovedBy = approver;

                // Single save operation
                await _context.SaveChangesAsync();
                TempData["Success"] = "Document approved successfully!";
                return RedirectToAction("Approvals");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred while approving the document.";
                return RedirectToAction("Approvals");
            }
        }*/



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectDocument(string sopNumber, string reason)
        {
            if (string.IsNullOrWhiteSpace(sopNumber))
            {
                TempData["Error"] = "SOP Number is required.";
                return RedirectToAction("Approvals");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Error"] = "Rejection reason is required.";
                return RedirectToAction("Approvals");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                string reviewer = User.FindFirst("LaborName")?.Value ?? User.Identity?.Name ?? "Unknown User";
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("Role")?.Value;
                bool isAdmin = userRole?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

                // Fetch SOP data
                var doc = await _context.DocRegisters.FirstOrDefaultAsync(d => d.SopNumber == sopNumber && d.IsArchived == false);
                var structured = await _context.StructuredSops
                    .Include(s => s.Steps)
                    .FirstOrDefaultAsync(s => s.SopNumber == sopNumber && s.ArchivedOn == null);

                if (structured != null)
                {
                    string uploaderName = string.IsNullOrWhiteSpace(structured.Signatures) ? "the uploader" : structured.Signatures;
                    string uploaderEmail = string.IsNullOrWhiteSpace(structured.UserEmail) ? "N/A" : structured.UserEmail;

                    MarkAsReturnedForReview(structured, reviewer, reason);
                    _context.StructuredSops.Update(structured);

                    if (doc != null)
                    {
                        MarkAsReturnedForReview(doc, reviewer, reason);
                        _context.DocRegisters.Update(doc);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await _auditLog.LogAsync(doc?.Id, sopNumber, "Returned for Review", reviewer, reason, doc?.OriginalFile ?? structured?.Title);

                    // 🔔 Notify author about rejection
                    try
                    {
                        await NotifyAuthorOfRejectionAsync(
                            sopNumber,
                            uploaderName,
                            uploaderEmail,
                            reason,
                            reviewer
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to notify author for rejected Structured SOP {SopNumber}", sopNumber);
                    }

                    TempData["Warning"] = $"Structured SOP '{sopNumber}' was returned for review. Please notify {uploaderName} ({uploaderEmail}) to revise and resubmit.";
                    _logger.LogInformation("Structured SOP {SopNumber} returned for review by {Reviewer}. Uploader: {Uploader}, Email: {Email}", sopNumber, reviewer, uploaderName, uploaderEmail);
                }
                else if (doc != null)
                {
                    string uploaderName = string.IsNullOrWhiteSpace(doc.Author) ? "the uploader" : doc.Author;
                    string uploaderEmail = string.IsNullOrWhiteSpace(doc.UserEmail) ? "N/A" : doc.UserEmail;

                    if (isAdmin)
                    {
                        // Admin: delete uploaded SOP
                        try
                        {
                            var basePath = _config["StorageSettings:BasePath"];
                            if (!string.IsNullOrEmpty(basePath))
                                DeleteDocumentFiles(doc, basePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete files for SOP {SopNumber}, proceeding with DB removal.", sopNumber);
                        }

                        _context.DocRegisters.Remove(doc);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        // 🔔 Notify author about rejection + deletion
                        try
                        {
                            await NotifyAuthorOfRejectionAsync(
                                sopNumber,
                                uploaderName,
                                uploaderEmail,
                                reason,
                                reviewer
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to notify author for rejected/deleted SOP {SopNumber}", sopNumber);
                        }

                        TempData["Warning"] = $"SOP '{sopNumber}' and related files have been rejected and deleted. Please notify {uploaderName} ({uploaderEmail}) with the reason for rejection.";
                        _logger.LogInformation("Uploaded SOP {SopNumber} rejected and deleted by {Reviewer}. Uploader: {Uploader}, Email: {Email}", sopNumber, reviewer, uploaderName, uploaderEmail);
                    }
                    else
                    {
                        // Manager: return for review only
                        MarkAsReturnedForReview(doc, reviewer, reason);
                        _context.DocRegisters.Update(doc);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        await _auditLog.LogAsync(doc.Id, sopNumber, "Returned for Review", reviewer, reason, doc.OriginalFile);

                        // Notify author about rejection
                        try
                        {
                            await NotifyAuthorOfRejectionAsync(
                                sopNumber,
                                uploaderName,
                                uploaderEmail,
                                reason,
                                reviewer
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to notify author for rejected Structured SOP {SopNumber}", sopNumber);
                        }

                        TempData["Warning"] = $"SOP '{sopNumber}' was returned for review. Please notify '{uploaderName}' to revise and resubmit.";
                        _logger.LogInformation("Uploaded SOP {SopNumber} returned for review by {Reviewer}. Uploader: {Uploader}, Email: {Email}", sopNumber, reviewer, uploaderName, uploaderEmail);
                    }
                }
                else
                {
                    TempData["Error"] = $"No SOP found with number '{sopNumber}'.";
                    await transaction.RollbackAsync();
                }

                return RedirectToAction("Approvals");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error rejecting SOP {SopNumber}", sopNumber);
                TempData["Error"] = "An unexpected error occurred during rejection. Please try again or contact IT.";
                return RedirectToAction("Approvals");
            }
        }

        // ==================== HELPER ====================

        private void MarkAsReturnedForReview(dynamic entity, string reviewer, string reason)
        {
            entity.Status = "Returned for Review";
            entity.ReviewedBy = reviewer;
            entity.RejectionReason = $"Rejected by ({reviewer}) with the reason: {reason}";
            entity.ReturnedDate = DateTime.Now;
        }

        private void DeleteDocumentFiles(DocRegister doc, string basePath)
        {
            // Delete files based on the stored paths in database
            if (!string.IsNullOrWhiteSpace(doc.DocumentPath))
            {
                var fullDocumentPath = Path.Combine(basePath, doc.DocumentPath);
                DeleteFileSafe(fullDocumentPath);
            }

            if (!string.IsNullOrWhiteSpace(doc.VideoPath))
            {
                var fullVideoPath = Path.Combine(basePath, doc.VideoPath);
                DeleteFileSafe(fullVideoPath);
            }

            // Legacy cleanup - handle files that might exist in old locations
            if (!string.IsNullOrWhiteSpace(doc.OriginalFile))
            {
                var fileName = Path.GetFileName(doc.OriginalFile);

                // Current storage structure locations
                var currentPossiblePaths = new List<string>
                {
                    // Original file in Originals folder (current structure)
                    Path.Combine(basePath, "Originals", doc.DocType ?? "", fileName),
            
                    // PDF file in PDFs folder (if PDF was uploaded separately)
                    Path.Combine(basePath, "PDFs", doc.DocType ?? "", fileName),
            
                    // Video file in Videos folder
                    Path.Combine(basePath, "Videos", doc.DocType ?? "", fileName)
                };

                // Legacy locations (for migration period)
                var legacyPossiblePaths = new List<string>
                {
                    Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Uploads", fileName),
                    Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Videos", fileName),
                    Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Originals", doc.DocType ?? "", fileName)
                };

                foreach (var path in currentPossiblePaths.Concat(legacyPossiblePaths))
                {
                    DeleteFileSafe(path);
                }
            }

            // Handle PDF file name if different from original (legacy support)
            if (!string.IsNullOrWhiteSpace(doc.FileName) &&
                doc.FileName.ToLower() != "n/a" &&
                doc.FileName != doc.OriginalFile)
            {
                var pdfPaths = new List<string>
        {
            // Current structure
            Path.Combine(basePath, "PDFs", doc.DocType ?? "", doc.FileName),
            
            // Legacy structure
            Path.Combine(basePath, "Uploads", doc.FileName),
            Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Uploads", doc.FileName)
        };

                foreach (var path in pdfPaths)
                {
                    DeleteFileSafe(path);
                }
            }

            // Clean up any files with timestamp patterns for this SOP number
            if (!string.IsNullOrWhiteSpace(doc.SopNumber))
            {
                CleanupTimestampedFiles(doc, basePath);
            }
        }

        private void CleanupTimestampedFiles(DocRegister doc, string basePath)
        {
            try
            {
                // Clean up PDF files with timestamp pattern
                var pdfDirectory = Path.Combine(basePath, "PDFs", doc.DocType ?? "");
                if (Directory.Exists(pdfDirectory))
                {
                    var pdfPattern = $"{doc.SopNumber}_*.pdf";
                    foreach (var file in Directory.GetFiles(pdfDirectory, pdfPattern))
                    {
                        DeleteFileSafe(file);
                    }
                }

                // Clean up video files with timestamp pattern  
                var videoDirectory = Path.Combine(basePath, "Videos", doc.DocType ?? "");
                if (Directory.Exists(videoDirectory))
                {
                    var videoPattern = $"{doc.SopNumber}_*.*";
                    foreach (var file in Directory.GetFiles(videoDirectory, videoPattern))
                    {
                        DeleteFileSafe(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up timestamped files for SOP: {SopNumber}", doc.SopNumber);
            }
        }

        private void DeleteFileSafe(string filePath)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    _logger.LogInformation("Deleted file: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file: {FilePath}", filePath);
            }
        }

        private void DeleteStructuredSopFiles(StructuredSop structured, string basePath)
        {
            foreach (var step in structured.Steps)
            {
                if (!string.IsNullOrWhiteSpace(step.ImagePath))
                {
                    var imagePaths = new List<string>
                    {
                        Path.Combine(basePath, "Steps", step.ImagePath),
                        Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Steps", step.ImagePath)
                    };

                    foreach (var path in imagePaths)
                    {
                        DeleteFileSafe(path);
                    }
                }

                if (!string.IsNullOrWhiteSpace(step.KeyPointImagePath))
                {
                    var keyPointPaths = new List<string>
                    {
                        Path.Combine(basePath, "Steps", step.KeyPointImagePath),
                        Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Steps", step.KeyPointImagePath)
                    };

                    foreach (var path in keyPointPaths)
                    {
                        DeleteFileSafe(path);
                    }
                }
            }
        }

        private async Task NotifyAuthorOfRejectionAsync( string sopNumber, string authorName, string authorEmail, string rejectionReason, string rejectedBy)
        {
            try
            {
                using var conn = _context.Database.GetDbConnection();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "sp_InsertAuthorRejectionNotification";
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add(new SqlParameter("@SopNumber", sopNumber));
                cmd.Parameters.Add(new SqlParameter("@Author", authorName ?? "N/A"));
                cmd.Parameters.Add(new SqlParameter("@AuthorEmail", authorEmail ?? "N/A"));
                cmd.Parameters.Add(new SqlParameter("@RejectedBy", rejectedBy));
                cmd.Parameters.Add(new SqlParameter("@RejectionReason", rejectionReason ?? ""));
                cmd.Parameters.Add(new SqlParameter("@RejectionDate", DateTime.Now));

                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error sending rejection notification for SOP {SopNumber}. Author: {Author}",
                    sopNumber, authorName);
                throw; // Let caller decide whether to swallow or log
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectDeletion(int id)
        {
            var document = await _context.DocRegisters.FindAsync(id);

            if (document == null)
            {
                TempData["Error"] = "Document not found.";
                return RedirectToAction("Approvals");
            }

            document.Status = "Approved";
            document.DeletionReason = null;
            document.DeletionRequestedBy = null;
            document.DeletionRequestedOn = null;

            try
            {
                _context.Update(document);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Deletion request rejected. Document {document.FileName} (SOP: {document.SopNumber}) status reverted to Approved.";

            }
            catch (DbUpdateException ex)
            {
                TempData["Error"] = "An error occurred while rejecting deletion: " + ex.Message;
            }

            return RedirectToAction("Approvals");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveDeletion(string sopNumber = null, string title = null, int? id = null)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var deletedBy = User.FindFirst("LaborName")?.Value ?? User.Identity?.Name ?? "System";

            // Locate documents using the helper method
            var (doc, sop) = await LocateDocumentsAsync(id, sopNumber, title);
            if (doc == null && sop == null)
            {
                TempData["Error"] = "No matching document found.";
                return RedirectToAction("Index");
            }

            try
            {
                if (doc != null)
                    await _auditLog.LogAsync(doc.Id, doc.SopNumber ?? "", "Archived", deletedBy, doc.DeletionReason ?? "Approved for deletion", doc.OriginalFile);

                // Archive files - multiple folder search using new storage location
                var (originalArchived, pdfArchived, videoArchived) = await ArchiveFilesAsync(doc, timestamp);

                // Update database records
                await UpdateDatabaseRecordsAsync(doc, sop, deletedBy);

                // Log deletion with enhanced reason
                await LogDeletionAsync(doc, deletedBy, originalArchived, pdfArchived);

                TempData["Success"] = "Document successfully archived.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error during deletion: {ex.Message}";
                _logger.LogError(ex, "Error archiving document SOP: {SopNumber}", sopNumber);
            }

            return RedirectToAction("Approvals");
        }

        private async Task<(DocRegister doc, StructuredSop sop)> LocateDocumentsAsync(int? id, string sopNumber, string title)
        {
            DocRegister doc = null;
            StructuredSop sop = null;

            if (id.HasValue)
            {
                doc = await _context.DocRegisters.FindAsync(id.Value);
                if (doc != null)
                {
                    sopNumber = doc.SopNumber?.Trim();
                    title = doc.OriginalFile?.Trim();
                }
            }

            if (!string.IsNullOrEmpty(sopNumber) && !string.IsNullOrEmpty(title))
            {
                // Search for StructuredSop with trimmed values
                sop = await _context.StructuredSops.FirstOrDefaultAsync(s => s.SopNumber == sopNumber && s.Title == title && s.Status != "Archived");

                // Search for DocRegister if not already found
                doc ??= await _context.DocRegisters.FirstOrDefaultAsync(d => d.SopNumber == sopNumber && d.OriginalFile == title && d.IsArchived == false);
            }

            return (doc, sop);
        }

        private async Task<(string originalArchived, string pdfArchived, string videoArchived)> ArchiveFilesAsync(DocRegister doc, string timestamp)
        {
            if (doc == null) return (null, null, null);

            // Get storage base path from configuration
            var basePath = _config["StorageSettings:BasePath"];
            if (string.IsNullOrEmpty(basePath))
            {
                throw new InvalidOperationException("Storage configuration is missing.");
            }

            var archivePaths = new ArchivePaths(basePath);
            var fileArchiver = new FileArchiver(archivePaths, basePath);

            string originalArchived = null;
            string videoArchived = null;
            string pdfArchived = null;

            if (!string.IsNullOrWhiteSpace(doc.OriginalFile))
            {
                originalArchived = await fileArchiver.ArchiveOriginalFileAsync(doc.OriginalFile, doc.DocType, timestamp);

                var ext = Path.GetExtension(doc.OriginalFile)?.ToLowerInvariant();
                if (new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" }.Contains(ext))
                {
                    videoArchived = await fileArchiver.ArchiveVideoFileAsync(doc.OriginalFile, timestamp);
                }
            }

            if (!string.IsNullOrWhiteSpace(doc.FileName) && doc.FileName.ToLower() != "n/a")
            {
                pdfArchived = await fileArchiver.ArchivePdfFileAsync(doc.FileName, timestamp);
            }

            return (originalArchived, pdfArchived, videoArchived);
        }

        private async Task UpdateDatabaseRecordsAsync(DocRegister doc, StructuredSop sop, string deletedBy)
        {
            if (sop != null)
            {
                sop.Status = "Archived";
                sop.ArchivedOn = DateTime.Now;
                _context.StructuredSops.Update(sop);
            }

            if (doc != null)
            {
                doc.IsArchived = true;
                doc.Status = "Archived";
                doc.ArchivedOn = DateTime.Now;
                _context.DocRegisters.Update(doc);
            }

            await _context.SaveChangesAsync();
        }

        private async Task LogDeletionAsync(DocRegister doc, string deletedBy, string originalArchived, string pdfArchived)
        {
            if (doc == null) return;

            var log = new DeletedFileLog
            {
                SOPNumber = doc.SopNumber,
                FileName = pdfArchived ?? doc.FileName,
                OriginalFileName = originalArchived ?? doc.OriginalFile,
                DeletedBy = deletedBy,
                DeletedOn = DateTime.Now,
                Reason = doc.DeletionReason ?? "Administrative deletion",
                DocType = doc.DocType,
                Department = doc.Department,
                Area = doc.Area,
                Revision = doc.Revision, 
                //uniqueNumber = doc.uniqueNumber,
                ContentType = doc.ContentType,
                FileSize = doc.FileSize,
                Author = doc.Author,
                UserEmail = doc.UserEmail,
                DepartmentSupervisor = doc.DepartmentSupervisor,
                SupervisorEmail = doc.SupervisorEmail,
                Status = doc.Status,
                EffectiveDate = doc.EffectiveDate,
                UploadDate = doc.UploadDate,
                ArchivedOn = DateTime.Now,
                WasApproved = doc.AdminApproved == true,
                OriginalDocRegisterId = doc.Id
            };

            _context.DeletedFileLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        // Supporting Classes - Updated for new storage system
        public class ArchivePaths
        {
            public string Originals { get; }
            public string PDFs { get; }
            public string Videos { get; }
            public string Steps { get; }

            public ArchivePaths(string basePath)
            {
                // Archive within the main storage directory
                var archiveBase = Path.Combine(basePath, "Archive", "Deleted");

                Originals = Path.Combine(archiveBase, "Originals");
                PDFs = Path.Combine(archiveBase, "PDFs");
                Videos = Path.Combine(archiveBase, "Videos");
                Steps = Path.Combine(archiveBase, "Steps");

                Directory.CreateDirectory(Originals);
                Directory.CreateDirectory(PDFs);
                Directory.CreateDirectory(Videos);
                Directory.CreateDirectory(Steps);
            }
        }

        public class FileArchiver
        {
            private readonly ArchivePaths _paths;
            private readonly string _basePath;

            public FileArchiver(ArchivePaths paths, string basePath)
            {
                _paths = paths;
                _basePath = basePath;
            }

            public async Task<string> ArchiveOriginalFileAsync(string originalFile, string docType, string timestamp)
            {
                var searchFolders = new List<string>();

                // Add document type specific folder
                if (!string.IsNullOrEmpty(docType))
                {
                    searchFolders.Add(Path.Combine(_basePath, "Originals", docType));
                }

                // Add general originals folder
                searchFolders.Add(Path.Combine(_basePath, "Originals"));

                // Add legacy locations for migration
                searchFolders.Add(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Documents", "Originals", docType ?? ""));
                searchFolders.Add(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Documents", "Originals"));

                return await ArchiveFileAsync(originalFile, timestamp, _paths.Originals, searchFolders.ToArray());
            }

            public async Task<string> ArchiveVideoFileAsync(string videoFile, string timestamp)
            {
                var searchFolders = new[]
                {
                    Path.Combine(_basePath, "Videos"),
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Documents", "Videos") // Legacy
                };

                return await ArchiveFileAsync(videoFile, timestamp, _paths.Videos, searchFolders);
            }

            public async Task<string> ArchivePdfFileAsync(string pdfFile, string timestamp)
            {
                var searchFolders = new[]
                {
                    Path.Combine(_basePath, "PDFs"),
                    Path.Combine(_basePath, "Uploads"), // Alternative location
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Documents", "Uploads") // Legacy
                };

                return await ArchiveFileAsync(pdfFile, timestamp, _paths.PDFs, searchFolders);
            }

            public async Task<string> ArchiveStepFileAsync(string stepFile, string timestamp)
            {
                var searchFolders = new[]
                {
                    Path.Combine(_basePath, "Steps"),
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Documents", "Steps") // Legacy
                };

                return await ArchiveFileAsync(stepFile, timestamp, _paths.Steps, searchFolders);
            }

            private async Task<string> ArchiveFileAsync(string sourceFile, string timestamp, string targetFolder, string[] searchFolders)
            {
                var sourcePath = FindFile(searchFolders, sourceFile);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    // File not found in any location
                    return null;
                }

                var ext = Path.GetExtension(sourcePath);
                var newFileName = $"{timestamp}_{Path.GetFileNameWithoutExtension(sourceFile)}{ext}";
                var targetPath = Path.Combine(targetFolder, newFileName);

                // Ensure target directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                await Task.Run(() => System.IO.File.Move(sourcePath, targetPath));
                return newFileName;
            }

            private string FindFile(string[] folders, string fileName)
            {
                foreach (var folder in folders)
                {
                    try
                    {
                        if (!Directory.Exists(folder)) continue;

                        // First try exact filename match
                        var exactMatch = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories)
                            .FirstOrDefault();

                        if (exactMatch != null) return exactMatch;

                        // Fallback to case-insensitive search by filename only
                        var fileNameOnly = Path.GetFileName(fileName);
                        var caseInsensitiveMatch = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                            .FirstOrDefault(f => Path.GetFileName(f).Equals(fileNameOnly, StringComparison.OrdinalIgnoreCase));

                        if (caseInsensitiveMatch != null) return caseInsensitiveMatch;
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue searching other folders
                        System.Diagnostics.Debug.WriteLine($"Error searching folder {folder}: {ex.Message}");
                        continue;
                    }
                }
                return null;
            }
        }

        public async Task<IActionResult> History(int docRegisterId)
        {
            if (docRegisterId <= 0)
                return BadRequest("Invalid document ID.");

            var document = await _context.DocRegisters
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == docRegisterId);

            if (document == null)
                return NotFound("Document not found.");

            var historyList = await _context.DocRegisterHistories
                .Where(h => h.DocRegisterId == docRegisterId)
                .OrderByDescending(h => h.RevisedOn)
                .ToListAsync();

            var auditLogs = await _context.DocumentAuditLogs
                .AsNoTracking()
                .Where(a => a.DocRegisterId == docRegisterId || (a.SopNumber == document.SopNumber && a.DocRegisterId == null))
                .OrderByDescending(a => a.PerformedAtUtc)
                .ToListAsync();

            ViewBag.SopNumber = document.SopNumber;
            ViewBag.Title = $"{document.SopNumber} - Revision History";
            ViewBag.AuditLogs = auditLogs;

            return View(historyList);
        }

    }
}
