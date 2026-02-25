using ExcelDataReader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;
using SOPMSApp.Services;
using Dapper;


namespace SOPMSApp.Controllers
{
    public class MetaController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FileUploadController> _logger;
        private readonly IDocumentAuditLogService _auditLog;


        public MetaController(ApplicationDbContext context, IWebHostEnvironment env, IConfiguration configuration, ILogger<FileUploadController> logger, IDocumentAuditLogService auditLog)
        {
            _context = context;
            _env = env;
            _configuration = configuration;
            _logger = logger;
            _auditLog = auditLog;
        }
        public async Task<IActionResult> Index()
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync("EXEC UpdateReviewStatus");
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                if (ex.Number == 2812)
                    _logger.LogWarning("UpdateReviewStatus stored procedure not found. Create it in the database to enable review status updates.");
                else
                    throw;
            }

            var documents = _context.DocRegisters
                .Where(d => d.Status == "Approved" && d.IsArchived == false)
                .ToList();

            // Precompute all the view data
            var viewModels = documents.Select(doc => CreateViewModel(doc)).ToList();

            return View(viewModels);
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
                .Where(a => a.DocRegisterId == docRegisterId || a.SopNumber == document.SopNumber)
                .OrderByDescending(a => a.PerformedAtUtc)
                .ToListAsync();

            ViewBag.SopNumber = document.SopNumber;
            ViewBag.Title = $"{document.SopNumber} - Revision History";
            ViewBag.AuditLogs = auditLogs;

            return View(historyList);
        }

        public IActionResult Privacy()
        {
            var metadata = _context.DocRegisters.ToList();
            return View(metadata);
        }


        // DELETE FILE FUNCTION
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string sopNumber = null, string title = null, int? id = null)
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
                // Audit log before we archive (so we still have doc.Id and SopNumber)
                if (doc != null)
                {
                    await _auditLog.LogAsync(doc.Id, doc.SopNumber ?? "", "Archived", deletedBy,
                        doc.DeletionReason ?? "Administrative deletion", doc.OriginalFile);
                }

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

            return RedirectToAction("Index");
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
            var basePath = _configuration["StorageSettings:BasePath"];
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


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestDelete(int id, string deletionReason)
        {
            if (id <= 0)
            {
                TempData["Error"] = "Invalid document ID.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(deletionReason))
            {
                TempData["Error"] = "Deletion reason is required.";
                return RedirectToAction("Index");
            }

            // Sanitize the deletion reason
            deletionReason = deletionReason.Trim();
            if (deletionReason.Length > 500) // Optional: limit length
            {
                deletionReason = deletionReason.Substring(0, 500);
            }

            var doc = await _context.DocRegisters.FindAsync(id);
            if (doc == null)
            {
                TempData["Error"] = "Document not found.";
                return RedirectToAction("Index");
            }

            // Check if document is already pending deletion or archived
            if (doc.Status == "Pending Deletion")
            {
                TempData["Warning"] = "This document is already pending deletion.";
                return RedirectToAction("Index");
            }

            if (doc.IsArchived != false)
            {
                TempData["Warning"] = "This document has already been archived.";
                return RedirectToAction("Index");
            }

            try
            {
                // Get user information
                var laborName = User.FindFirst("LaborName")?.Value;
                var userName = User.Identity?.Name ?? "Unknown User";
                var userEmail = User.FindFirst("Email")?.Value ?? "N/A";

                var requesterInfo = !string.IsNullOrEmpty(laborName)
                    ? $"{laborName} ({userName})"
                    : userName;

                // Update document status
                doc.Status = "Pending Deletion";
                doc.DeletionReason = deletionReason;
                doc.DeletionRequestedBy = $"{requesterInfo}, Email: {userEmail}";
                doc.DeletionRequestedOn = DateTime.Now;

                _context.DocRegisters.Update(doc);
                await _context.SaveChangesAsync();

                // Log the deletion request
                _logger.LogInformation(
                    "Deletion requested for SOP {SopNumber} (ID: {DocumentId}) by {User}. Reason: {DeletionReason}",
                    doc.SopNumber, doc.Id, requesterInfo, deletionReason
                );

                TempData["Success"] = "Deletion request submitted successfully. Awaiting administrator approval.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting deletion request for document ID {DocumentId}", id);
                TempData["Error"] = "An error occurred while submitting the deletion request. Please try again.";
            }

            return RedirectToAction("Index");
        }

        public IActionResult PreviewExcel(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest("File name is required.");
            }

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "upload", fileName);

            if (!System.IO.File.Exists(path))
            {
                return NotFound("The file does not exist.");
            }

            using var stream = System.IO.File.Open(path, FileMode.Open, FileAccess.Read);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            var result = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = (_) => new ExcelDataTableConfiguration
                {
                    UseHeaderRow = true
                }
            });

            var table = result.Tables[0]; // Only first sheet
            return View(table);
        }


        private DocRegisterViewModel CreateViewModel(DocRegister doc)
        {
            var fileInfo = GetFileInfo(doc);

            return new DocRegisterViewModel
            {
                Document = doc,
                FileInfo = fileInfo,
                PdfUrl = GetPdfUrl(doc, fileInfo),
                VideoUrl = GetVideoUrl(doc, fileInfo),
                OtherFileUrl = GetOtherFileUrl(doc, fileInfo),
                DownloadUrl = Url.Action("Download", "FileUpload", new { id = doc.Id }),
                OriginalDownloadUrl = !string.IsNullOrWhiteSpace(doc.OriginalFile) && !string.IsNullOrWhiteSpace(doc.DocType)
                    ? Url.Action("Originals", "FileAccess", new { docType = doc.DocType, fileName = doc.OriginalFile })
                    : null,
                StatusClass = GetStatusClass(doc.ReviewStatus)
            };
        }

        // Optimized GetFileInfo - no file system operations
        private Models.FileInfoResult GetFileInfo(DocRegister doc)
        {
            var displayName = !string.IsNullOrWhiteSpace(doc.FileName) && doc.FileName != "N/A"
                ? doc.FileName
                : doc.OriginalFile;

            var extension = System.IO.Path.GetExtension(displayName)?.ToLower() ?? "";

            return new Models.FileInfoResult
            {
                DisplayName = displayName,
                IsPdf = extension == ".pdf",
                IsVideo = extension is ".mp4" or ".mov" or ".avi" or ".webm" or ".ogg",
                FileExtension = extension
            };
        }

        // Precompute URLs
        /// <summary>
        /// Generates a direct URL to a PDF in the FileAccessController, guaranteed to work from any controller/area.
        /// </summary>
        private string GetPdfUrl(DocRegister doc, Models.FileInfoResult fileInfo)
        {
            // Prefer stored PDF filename; otherwise try original base name + .pdf so GetPdf can find converted PDFs
            var fileName = !string.IsNullOrWhiteSpace(doc.FileName) && doc.FileName != "N/A"
                ? doc.FileName
                : null;
            if (string.IsNullOrEmpty(fileName) && !string.IsNullOrWhiteSpace(doc.OriginalFile))
            {
                var ext = Path.GetExtension(doc.OriginalFile);
                if (!string.IsNullOrEmpty(ext))
                    fileName = Path.GetFileNameWithoutExtension(doc.OriginalFile) + ".pdf";
            }
            if (string.IsNullOrEmpty(fileName))
                return "#";

            var url = Url.Action(
                action: "GetPdf",
                controller: "FileAccess",
                values: new { fileName = fileName, docType = doc.DocType, area = "" },
                protocol: Request.Scheme
            );
            return string.IsNullOrEmpty(url) ? "#" : url;
        }



        private string? GetVideoUrl(DocRegister doc, Models.FileInfoResult fileInfo)
        {
            return fileInfo.IsVideo
                ? Url.Action("Videos", "FileAccess", new { fileName = doc.OriginalFile })
                : "#";
        }

        private string? GetOtherFileUrl(DocRegister doc, Models.FileInfoResult fileInfo)
        {
            return !fileInfo.IsPdf && !fileInfo.IsVideo
                ? Url.Action("Originals", "FileAccess", new { docType = doc.DocType, fileName = doc.OriginalFile })
                : "#";
        }

        private string GetStatusClass(string status)
        {
            return status switch
            {
                "Active" => "bg-success",
                "Renew" => "bg-warning",
                "Expired" => "bg-danger",
                _ => "bg-secondary"
            };
        }

        [HttpGet]
        public async Task<IActionResult> ExportMetadataXlsx()
        {
            try
            {
                using var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                string query = @"
                    SELECT 
                           [DepartmentSupervisor] as [Document Manager],
                           [SopNumber] as [Document Number],
                           [OriginalFile] as [Name of document],
                           [UploadDate] as [Date Approved],
                           [Department],
                           [LastReviewDate] as [Date Reviewed],
                           DATEDIFF(MONTH, [LastReviewDate], [EffectiveDate]) as [Review Period (Months)],
                           [EffectiveDate] as [Next Review],
                           [Revision] as [Version #],
                           [Status],
                           [Author] as [Uploaded by],	
                           [DocType] as [Document Type],
                           [DocumentType] as [Description],
                           [Area] as [Applicable Areas]
                    FROM DocRet.dbo.DocRegisters
                    WHERE IsArchived = 0;
                ";

                var rows = (await conn.QueryAsync(query)).ToList();
                if (!rows.Any())
                {
                    TempData["Error"] = "No data available.";
                    return RedirectToAction("Index");
                }

                using var workbook = new ClosedXML.Excel.XLWorkbook();
                var sheet = workbook.Worksheets.Add("SOP Metadata");

                // Write header
                var firstRow = (IDictionary<string, object>)rows.First();
                var headers = firstRow.Keys.ToList();

                for (int i = 0; i < headers.Count; i++)
                {
                    sheet.Cell(1, i + 1).Value = headers[i];
                    sheet.Cell(1, i + 1).Style.Font.SetBold();
                    sheet.Cell(1, i + 1).Style.Fill.SetBackgroundColor(ClosedXML.Excel.XLColor.LightGray);
                }

                // Write rows
                int rowIndex = 2;
                foreach (var row in rows)
                {
                    var dict = (IDictionary<string, object>)row;
                    int col = 1;

                    foreach (var val in dict.Values)
                    {
                        sheet.Cell(rowIndex, col).Value = val?.ToString() ?? "";
                        col++;
                    }

                    rowIndex++;
                }

                sheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                var fileName = $"SOP_Metadata_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(
                    stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting metadata XLSX.");
                TempData["Error"] = "Export failed.";
                return RedirectToAction("Index");
            }
        }


    }
}
