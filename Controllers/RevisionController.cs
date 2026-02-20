using DocumentFormat.OpenXml.Bibliography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SOPMSApp.Data;
using SOPMSApp.Models;
using SOPMSApp.Services;
using SOPMSApp.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SOPMSApp.Controllers
{
    public class RevisionController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RevisionController> _logger;
        private readonly DocRegisterService _docRegisterService;
        private readonly IDocumentAuditLogService _auditLog;

        public RevisionController(ApplicationDbContext context, IWebHostEnvironment env, IConfiguration configuration, ILogger<RevisionController> logger, DocRegisterService docRegisterService, IDocumentAuditLogService auditLog)
        {
            _env = env;
            _logger = logger;
            _context = context;
            _configuration = configuration;
            _docRegisterService = docRegisterService;
            _auditLog = auditLog;
        }

        // GET: Revision/Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                // ✅ ADD THIS: Populate departments for the dropdown
                ViewBag.Departments = await _context.DocRegisters
                    .Select(d => d.Department)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToListAsync();

                // ✅ ADD THIS: Initialize filter values
                ViewBag.SearchTerm = "";
                ViewBag.SelectedDepartment = "";
                ViewBag.ExpiryFilter = "";
                ViewBag.ShowReset = false;

                var today = DateTime.Today;

                var expiredDocuments = await _context.DocRegisters
                    .Where(d => (d.Status == "Approved" || d.Status == "Returned for Review") &&
                                d.IsArchived != true &&
                                d.ReviewStatus != "Active" &&
                                d.EffectiveDate.HasValue &&
                               (
                                   // Expired (any past date)
                                   d.EffectiveDate.Value < today
                                   ||
                                   // About to expire (within 30 days from now)
                                   (d.EffectiveDate.Value >= today &&
                                    d.EffectiveDate.Value <= today.AddDays(30))
                               ))
                    .OrderBy(d => d.LastReviewDate)
                    .ToListAsync();

                if (!expiredDocuments.Any())
                {
                    ViewBag.Message = "No expired or about-to-expire documents found.";
                }

                return View(expiredDocuments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading expired documents");
                TempData["Error"] = "An error occurred while loading documents.";

                // ✅ ADD THIS: Even in error case, populate dropdowns
                ViewBag.Departments = new List<string>();
                ViewBag.SearchTerm = "";
                ViewBag.SelectedDepartment = "";
                ViewBag.ExpiryFilter = "";
                ViewBag.ShowReset = false;

                return View(new List<DocRegister>());
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

        // POST: Revision/Index
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(string searchTerm, string department, string expiryFilter)
        {
            try
            {
                // Prepare dropdown data
                ViewBag.Departments = await _context.DocRegisters
                    .Where(d => !string.IsNullOrEmpty(d.Department))
                    .Select(d => d.Department)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToListAsync();

                // Store filter selections for the view
                ViewBag.SearchTerm = searchTerm;
                ViewBag.SelectedDepartment = department;
                ViewBag.ExpiryFilter = expiryFilter;

                // Start query (non-archived approved or returned docs)
                var query = _context.DocRegisters
                    .Where(d => (d.Status == "Approved" || d.Status == "Returned for Review") &&
                                d.IsArchived != true)
                    .AsQueryable();

                // 🔍 Search filter (SOP number or file name, ignore spaces)
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    string normalizedSearch = searchTerm.Replace(" ", "").Trim().ToLower();

                    query = query.Where(d =>
                        d.SopNumber.Replace(" ", "").ToLower().Contains(normalizedSearch) ||
                        d.OriginalFile.ToLower().Contains(normalizedSearch) ||
                        d.FileName.ToLower().Contains(normalizedSearch));
                }

                // 🏢 Department filter
                if (!string.IsNullOrWhiteSpace(department))
                {
                    query = query.Where(d => d.Department == department);
                }

                // ⏰ Expiry status filter
                var today = DateTime.Today;
                if (!string.IsNullOrWhiteSpace(expiryFilter))
                {
                    query = expiryFilter switch
                    {
                        "active" => query.Where(d => d.EffectiveDate > today.AddDays(30)),
                        "expiring" => query.Where(d => d.EffectiveDate <= today.AddDays(30) && d.EffectiveDate >= today),
                        "expired" => query.Where(d => d.EffectiveDate < today),
                        _ => query
                    };
                }

                // Execute query
                var results = await query
                    .OrderByDescending(d => d.LastReviewDate)
                    .ToListAsync();

                // Message handling
                if (!results.Any())
                {
                    ViewBag.Message = string.IsNullOrWhiteSpace(searchTerm)
                        ? "No documents found."
                        : $"No documents matching \"{searchTerm}\" found.";
                }

                // Indicate whether to show Reset button
                ViewBag.ShowReset = !string.IsNullOrWhiteSpace(searchTerm)
                                    || !string.IsNullOrWhiteSpace(department)
                                    || !string.IsNullOrWhiteSpace(expiryFilter);

                return View(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error filtering documents");
                TempData["Error"] = "An error occurred during filtering.";

                // ✅ ADD THIS: Even in error case, populate dropdowns
                ViewBag.Departments = new List<string>();
                ViewBag.SearchTerm = searchTerm;
                ViewBag.SelectedDepartment = department;
                ViewBag.ExpiryFilter = expiryFilter;
                ViewBag.ShowReset = !string.IsNullOrWhiteSpace(searchTerm)
                                    || !string.IsNullOrWhiteSpace(department)
                                    || !string.IsNullOrWhiteSpace(expiryFilter);

                return View(new List<DocRegister>());
            }
        }

        
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var sop = await _context.DocRegisters.FindAsync(id);
                if (sop == null)
                {
                    _logger.LogWarning("Document with ID {Id} not found", id);
                    return NotFound();
                }

                // Map entity to ViewModel
                var viewModel = new DocRegisterRevisionViewModel
                {
                    Id = sop.Id,
                    SopNumber = sop.SopNumber,
                    OriginalFile = sop.OriginalFile,
                    FileName = sop.FileName,
                    Department = sop.Department,
                    Revision = IncrementRevision(sop.Revision),
                    EffectiveDate = sop.EffectiveDate,
                    Author = sop.Author,
                    DocType = sop.DocType,
                    DocumentType = sop.DocumentType,
                    ChangeDescription = ""
                };

                // Generate PDF URL for preview
                string pdfUrl = null;
                if (!string.IsNullOrEmpty(sop.FileName) && Path.GetExtension(sop.FileName).ToLower() == ".pdf")
                {
                    pdfUrl = Url.Action("GetPdf", "FileAccess", new { fileName = sop.FileName, docType = sop.DocType });
                }
                ViewBag.PdfUrl = pdfUrl;

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading document {Id} for edit", id);
                TempData["Error"] = "An error occurred while loading the document.";
                return RedirectToAction(nameof(Index));
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DocRegisterRevisionViewModel model)
        {
            try
            {
                if (id != model.Id) return NotFound();

                var sop = await _context.DocRegisters.FindAsync(id);
                if (sop == null) return NotFound();

                // Validate original file
                if (model.RevisedOriginalFile == null || model.RevisedOriginalFile.Length == 0)
                {
                    ModelState.AddModelError("RevisedOriginalFile", "Please select a file to upload.");
                    return View(model);
                }

                var safeOriginalFileName = Path.GetFileName(model.RevisedOriginalFile.FileName);
                if (!string.Equals(safeOriginalFileName, sop.OriginalFile, StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError("RevisedOriginalFile", $"Uploaded file name must match existing: {sop.OriginalFile}");
                    return View(model);
                }

                // PDF validation
                bool hadPdfBefore = !string.IsNullOrEmpty(sop.FileName) && sop.FileName.ToLower() != "n/a";
                if (hadPdfBefore && (model.RevisedPdfFile == null || model.RevisedPdfFile.Length == 0))
                {
                    ModelState.AddModelError("RevisedPdfFile", "A revised PDF file is required because a PDF exists.");
                    return View(model);
                }
                if (model.RevisedPdfFile != null && Path.GetExtension(model.RevisedPdfFile.FileName).ToLower() != ".pdf")
                {
                    ModelState.AddModelError("RevisedPdfFile", "PDF must have .pdf extension.");
                    return View(model);
                }

                // File size checks
                if (model.RevisedOriginalFile.Length > 10 * 1024 * 1024)
                    ModelState.AddModelError("RevisedOriginalFile", "File size must not exceed 10MB.");
                if (model.RevisedPdfFile != null && model.RevisedPdfFile.Length > 10 * 1024 * 1024)
                    ModelState.AddModelError("RevisedPdfFile", "PDF size must not exceed 10MB.");

                if (!ModelState.IsValid) return View(model);

                var loggedInUser = $"{User.FindFirst("LaborName")?.Value?.Trim() ?? "System"}";

                // Track revision history
                await _docRegisterService.TrackRevisionHistoryAsync(sop, loggedInUser, model.ChangeDescription);

                // Increment revision
                sop.Revision = IncrementRevision(sop.Revision);

                // Storage paths
                var basePath = _configuration["StorageSettings:BasePath"];
                var originalsDir = Path.Combine(basePath, "Originals", sop.DocType);
                var pdfsDir = Path.Combine(basePath, "PDFs");
                var archiveDir = Path.Combine(basePath, "RevisionArchives");
                Directory.CreateDirectory(originalsDir);
                Directory.CreateDirectory(pdfsDir);
                Directory.CreateDirectory(archiveDir);

                // Archive old original file
                var existingOriginalPath = Path.Combine(originalsDir, sop.OriginalFile);
                if (System.IO.File.Exists(existingOriginalPath))
                {
                    var archiveName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{sop.OriginalFile}";
                    System.IO.File.Move(existingOriginalPath, Path.Combine(archiveDir, archiveName));
                }

                // Save new original file
                using (var stream = new FileStream(Path.Combine(originalsDir, safeOriginalFileName), FileMode.Create))
                    await model.RevisedOriginalFile.CopyToAsync(stream);

                // Handle PDF
                if (model.RevisedPdfFile != null && model.RevisedPdfFile.Length > 0)
                {
                    var pdfFileName = Path.GetFileName(model.RevisedPdfFile.FileName);
                    var pdfPath = Path.Combine(pdfsDir, pdfFileName);

                    // Archive old PDF
                    if (hadPdfBefore)
                    {
                        var existingPdfPath = Path.Combine(pdfsDir, sop.FileName);
                        if (System.IO.File.Exists(existingPdfPath))
                        {
                            var archiveName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{sop.FileName}";
                            System.IO.File.Move(existingPdfPath, Path.Combine(archiveDir, archiveName));
                        }
                    }

                    using (var stream = new FileStream(pdfPath, FileMode.Create))
                        await model.RevisedPdfFile.CopyToAsync(stream);

                    sop.FileName = pdfFileName;
                }
                else if (!hadPdfBefore)
                {
                    sop.FileName = "N/A";
                }

                // Update metadata
                sop.DocumentType = model.DocType;
                sop.OriginalFile = safeOriginalFileName;
                sop.Author = loggedInUser;
                sop.EffectiveDate = model.EffectiveDate;
                sop.LastReviewDate = DateTime.Now;
                sop.Status = "Pending Approval";

                _context.Update(sop);
                await _context.SaveChangesAsync();

                await _auditLog.LogAsync(sop.Id, sop.SopNumber ?? "", "Revised", loggedInUser, model.ChangeDescription, sop.OriginalFile);

                TempData["Success"] = "File(s) revised successfully! Awaiting approval.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating document {Id}", id);
                ModelState.AddModelError("", "An unexpected error occurred. Please try again.");
                return View(model);
            }
        }


        private string IncrementRevision(string currentRevision)
        {
            const string prefix = "Rev: ";
            if (string.IsNullOrEmpty(currentRevision) || !currentRevision.StartsWith(prefix))
                return $"{prefix}1";

            if (int.TryParse(currentRevision.Substring(prefix.Length), out int rev))
                return $"{prefix}{rev + 1}";

            return $"{prefix}1";
        }

        private bool ArchiveFile(string sourcePath, string archiveDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    _logger.LogWarning("Source path is null or empty");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(archiveDir))
                {
                    _logger.LogWarning("Archive directory is null or empty");
                    return false;
                }

                if (!System.IO.File.Exists(sourcePath))
                {
                    _logger.LogWarning("Source file not found for archiving: {Path}", sourcePath);
                    return false;
                }

                Directory.CreateDirectory(archiveDir);

                var fileName = Path.GetFileName(sourcePath);
                var fileExtension = Path.GetExtension(fileName);
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

                var archivedFileName = $"{fileNameWithoutExtension}_Rev{timestamp}{fileExtension}";
                var archivePath = Path.Combine(archiveDir, archivedFileName);

                const int bufferSize = 81920; // 80KB buffer
                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize))
                using (var destinationStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize))
                {
                    sourceStream.CopyTo(destinationStream, bufferSize);
                }

                _logger.LogInformation("Successfully archived file from {Source} to {Archive}", sourcePath, archivePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error archiving file {Path}", sourcePath);
                return false;
            }
        }
    }
}