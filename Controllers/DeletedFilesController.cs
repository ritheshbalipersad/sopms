using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using SOPMSApp.Services;

namespace SOPMSApp.Controllers
{
    [Authorize(Roles = "Admin")]
    public class DeletedFilesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DeletedFilesController> _logger;
        private readonly FileRestoreService _fileRestoreService;
        private readonly FilePermanentDeleteService _filePermanentDeleteService;

        private readonly IConfiguration _config;

        public DeletedFilesController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            ILogger<DeletedFilesController> logger,
            FileRestoreService fileRestoreService,
            FilePermanentDeleteService filePermanentDeleteService,
            IConfiguration config)
        {
            _context = context;
            _env = env;
            _logger = logger;
            _fileRestoreService = fileRestoreService;
            _filePermanentDeleteService = filePermanentDeleteService;
            _config = config;
        }

        // Check if Server 26 actually has data
        public async Task<IActionResult> Index()
        {
            try
            {
                // Use raw SQL with CAST for compatibility
                var sql = @"
                        SELECT 
                            Id,
                            CAST(SOPNumber AS NVARCHAR(MAX)) as SOPNumber,
                            CAST(FileName AS NVARCHAR(MAX)) as FileName,
                            CAST(OriginalFileName AS NVARCHAR(MAX)) as OriginalFileName,
                            CAST(DeletedBy AS NVARCHAR(MAX)) as DeletedBy,
                            DeletedOn,
                            CAST(Reason AS NVARCHAR(MAX)) as Reason,
                            CAST(UserEmail AS NVARCHAR(500)) as UserEmail,
                            CAST(DocType AS NVARCHAR(100)) as DocType,
                            CAST(Department AS NVARCHAR(100)) as Department,
                            CAST(Area AS NVARCHAR(100)) as Area,
                            CAST(Revision AS NVARCHAR(50)) as Revision,
                            CAST(UniqueNumber AS NVARCHAR(100)) as UniqueNumber,
                            CAST(ContentType AS NVARCHAR(100)) as ContentType,
                            FileSize,
                            CAST(Author AS NVARCHAR(150)) as Author,
                            CAST(DepartmentSupervisor AS NVARCHAR(150)) as DepartmentSupervisor,
                            CAST(SupervisorEmail AS NVARCHAR(150)) as SupervisorEmail,
                            CAST(Status AS NVARCHAR(50)) as Status,
                            EffectiveDate,
                            UploadDate,
                            ArchivedOn,
                            WasApproved,
                            OriginalDocRegisterId
                        FROM DeletedFileLogs
                        ORDER BY DeletedOn DESC";

                var deletedFiles = await _context.DeletedFileLogs
                    .FromSqlRaw(sql)
                    .ToListAsync();

                var restorableIds = new HashSet<int>();
                foreach (var item in deletedFiles)
                {
                    if (_fileRestoreService.CanRestore(item))
                        restorableIds.Add(item.Id);
                }
                ViewBag.RestorableIds = restorableIds;

                return View(deletedFiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving deleted files from database");

                // Try alternative query for Server 26
                try
                {
                    var sql26 = "SELECT * FROM DeletedFileLogs ORDER BY DeletedOn DESC";
                    var deletedFiles26 = await _context.DeletedFileLogs
                        .FromSqlRaw(sql26)
                        .ToListAsync();
                    var restorableIds26 = new HashSet<int>();
                    foreach (var item in deletedFiles26)
                    {
                        if (_fileRestoreService.CanRestore(item))
                            restorableIds26.Add(item.Id);
                    }
                    ViewBag.RestorableIds = restorableIds26;
                    return View(deletedFiles26);
                }
                catch
                {
                    return View(new List<DeletedFileLog>());
                }
            }
        }

        private string GetArchiveRootPath()
        {
            var basePath = _config["StorageSettings:BasePath"];
            if (!string.IsNullOrEmpty(basePath))
                return Path.Combine(basePath, "Archive", "Deleted");
            return Path.Combine(_env.WebRootPath, "Archive", "Deleted");
        }

        // GET: /DeletedFiles/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var deletedDoc = await _context.DeletedFileLogs.FindAsync(id);
            if (deletedDoc == null)
            {
                TempData["Error"] = "Deleted document not found.";
                return RedirectToAction(nameof(Index));
            }

            var archiveRoot = GetArchiveRootPath();
            ViewBag.CanRestore = _fileRestoreService.CanRestore(deletedDoc);
            string displayFile = null;
            string fileUrl = null;
            bool isPdf = false;

            // Try by exact matches
            (displayFile, fileUrl, isPdf) = TryFindExactFile(deletedDoc, archiveRoot);

            // Try fuzzy match if still not found
            if (string.IsNullOrEmpty(fileUrl))
            {
                (displayFile, fileUrl, isPdf) = TryFindFuzzyFile(deletedDoc, archiveRoot);
            }

            if (!string.IsNullOrEmpty(fileUrl))
            {
                ViewBag.FileUrl = fileUrl;
                ViewBag.DisplayFileName = displayFile;
                ViewBag.IsPdf = isPdf;
                var archiveRootForFlags = GetArchiveRootPath();
                ViewBag.HasPdf = TryGetArchivedPdfPath(deletedDoc, archiveRootForFlags).physicalPath != null;
                ViewBag.HasOriginal = TryGetArchivedOriginalPath(deletedDoc, archiveRootForFlags).physicalPath != null;
            }
            else
            {
                ViewBag.FileError = "The document file was not found in the archive folders.";
            }

            return View(deletedDoc);
        }

        private (string fileName, string fileUrl, bool isPdf) TryFindExactFile(DeletedFileLog sop, string rootPath)
        {
            var pdfFolder = Path.Combine(rootPath, "PDFs");
            var originalFolder = Path.Combine(rootPath, "Originals");
            var videoFolder = Path.Combine(rootPath, "Videos");

            // Ensure directories exist
            Directory.CreateDirectory(pdfFolder);
            Directory.CreateDirectory(originalFolder);
            Directory.CreateDirectory(videoFolder);

            if (!string.IsNullOrWhiteSpace(sop.FileName))
            {
                var pdfPath = Path.Combine(pdfFolder, sop.FileName);
                if (System.IO.File.Exists(pdfPath))
                {
                    return (sop.FileName, $"/Archive/Deleted/PDFs/{sop.FileName}", true);
                }
            }

            if (!string.IsNullOrWhiteSpace(sop.OriginalFileName))
            {
                var originalPath = Path.Combine(originalFolder, sop.OriginalFileName);
                if (System.IO.File.Exists(originalPath))
                {
                    var isPdf = Path.GetExtension(sop.OriginalFileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                    return (sop.OriginalFileName, $"/Archive/Deleted/Originals/{sop.OriginalFileName}", isPdf);
                }

                var videoPath = Path.Combine(videoFolder, sop.OriginalFileName);
                if (System.IO.File.Exists(videoPath))
                {
                    return (sop.OriginalFileName, $"/Archive/Deleted/Videos/{sop.OriginalFileName}", false);
                }
            }

            return (null, null, false);
        }

        private (string fileName, string fileUrl, bool isPdf) TryFindFuzzyFile(DeletedFileLog sop, string rootPath)
        {
            var pdfFolder = Path.Combine(rootPath, "PDFs");
            var originalFolder = Path.Combine(rootPath, "Originals");
            var videoFolder = Path.Combine(rootPath, "Videos");

            string pdfBase = Path.GetFileNameWithoutExtension(sop.FileName ?? "");
            string originalBase = Path.GetFileNameWithoutExtension(sop.OriginalFileName ?? "");

            // Search in PDF folder
            if (!string.IsNullOrEmpty(pdfBase))
            {
                var matchedPdf = Directory.EnumerateFiles(pdfFolder)
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(pdfBase, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(matchedPdf))
                {
                    var name = Path.GetFileName(matchedPdf);
                    return (name, $"/Archive/Deleted/PDFs/{name}", true);
                }
            }

            // Search in Originals folder
            if (!string.IsNullOrEmpty(originalBase))
            {
                var matchedOriginal = Directory.EnumerateFiles(originalFolder)
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(originalBase, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(matchedOriginal))
                {
                    var name = Path.GetFileName(matchedOriginal);
                    var isPdf = Path.GetExtension(matchedOriginal).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                    return (name, $"/Archive/Deleted/Originals/{name}", isPdf);
                }
            }

            // Search in Videos folder
            if (!string.IsNullOrEmpty(originalBase))
            {
                var matchedVideo = Directory.EnumerateFiles(videoFolder)
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(originalBase, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(matchedVideo))
                {
                    var name = Path.GetFileName(matchedVideo);
                    return (name, $"/Archive/Deleted/Videos/{name}", false);
                }
            }

            return (null, null, false);
        }

        /// <summary>Stream the archived file for preview/download. type = "pdf" | "original" to choose which file; omit for default (PDF first).</summary>
        [HttpGet]
        public async Task<IActionResult> ServeArchivedFile(int id, string type = null, bool download = false)
        {
            var deletedDoc = await _context.DeletedFileLogs.FindAsync(id);
            if (deletedDoc == null)
                return NotFound("Document not found in archive.");

            var archiveRoot = GetArchiveRootPath();
            var (physicalPath, contentType, displayName) = string.Equals(type, "original", StringComparison.OrdinalIgnoreCase)
                ? TryGetArchivedOriginalPath(deletedDoc, archiveRoot)
                : string.Equals(type, "pdf", StringComparison.OrdinalIgnoreCase)
                    ? TryGetArchivedPdfPath(deletedDoc, archiveRoot)
                    : TryGetArchivedFilePath(deletedDoc, archiveRoot);

            return ServeFileResult(physicalPath, contentType, displayName, download);
        }

        /// <summary>Serve only the PDF from archive. Use this URL for "Download PDF" to avoid any mix-up with original.</summary>
        [HttpGet]
        public async Task<IActionResult> ServeArchivedPdf(int id, bool download = false)
        {
            var deletedDoc = await _context.DeletedFileLogs.FindAsync(id);
            if (deletedDoc == null)
                return NotFound("Document not found in archive.");
            var archiveRoot = GetArchiveRootPath();
            var (physicalPath, contentType, displayName) = TryGetArchivedPdfPath(deletedDoc, archiveRoot);
            return ServeFileResult(physicalPath, contentType, displayName, download);
        }

        /// <summary>Serve only the original file from archive. Use this URL for "Download original file".</summary>
        [HttpGet]
        public async Task<IActionResult> ServeArchivedOriginal(int id, bool download = false)
        {
            var deletedDoc = await _context.DeletedFileLogs.FindAsync(id);
            if (deletedDoc == null)
                return NotFound("Document not found in archive.");
            var archiveRoot = GetArchiveRootPath();
            var (physicalPath, contentType, displayName) = TryGetArchivedOriginalPath(deletedDoc, archiveRoot);
            return ServeFileResult(physicalPath, contentType, displayName, download);
        }

        private IActionResult ServeFileResult(string physicalPath, string contentType, string displayName, bool download)
        {
            if (string.IsNullOrEmpty(physicalPath) || !System.IO.File.Exists(physicalPath))
                return NotFound("File not found in archive.");
            var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (download)
                Response.Headers["Content-Disposition"] = $"attachment; filename=\"{displayName}\"";
            else
                Response.Headers["Content-Disposition"] = "inline";
            return File(stream, contentType ?? "application/octet-stream", enableRangeProcessing: true);
        }

        private (string physicalPath, string contentType, string displayName) TryGetArchivedPdfPath(DeletedFileLog sop, string rootPath)
        {
            var pdfFolder = Path.Combine(rootPath, "PDFs");
            if (!string.IsNullOrWhiteSpace(sop.FileName))
            {
                var pdfPath = Path.Combine(pdfFolder, sop.FileName);
                if (System.IO.File.Exists(pdfPath))
                    return (pdfPath, "application/pdf", sop.FileName);
            }
            string pdfBase = Path.GetFileNameWithoutExtension(sop.FileName ?? "");
            if (!string.IsNullOrEmpty(pdfBase))
            {
                var matched = Directory.EnumerateFiles(pdfFolder).FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Equals(pdfBase, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matched))
                    return (matched, "application/pdf", Path.GetFileName(matched));
            }
            return (null, null, null);
        }

        private (string physicalPath, string contentType, string displayName) TryGetArchivedOriginalPath(DeletedFileLog sop, string rootPath)
        {
            var originalFolder = Path.Combine(rootPath, "Originals");
            var videoFolder = Path.Combine(rootPath, "Videos");
            if (!string.IsNullOrWhiteSpace(sop.OriginalFileName))
            {
                var originalPath = Path.Combine(originalFolder, sop.OriginalFileName);
                if (System.IO.File.Exists(originalPath))
                {
                    var ext = Path.GetExtension(sop.OriginalFileName)?.ToLower();
                    return (originalPath, GetContentType(ext), sop.OriginalFileName);
                }
                var videoPath = Path.Combine(videoFolder, sop.OriginalFileName);
                if (System.IO.File.Exists(videoPath))
                {
                    var ext = Path.GetExtension(sop.OriginalFileName)?.ToLower();
                    return (videoPath, GetContentType(ext), sop.OriginalFileName);
                }
            }
            string originalBase = Path.GetFileNameWithoutExtension(sop.OriginalFileName ?? "");
            if (!string.IsNullOrEmpty(originalBase))
            {
                var matched = Directory.EnumerateFiles(originalFolder).FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Equals(originalBase, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matched))
                {
                    var ext = Path.GetExtension(matched)?.ToLower();
                    return (matched, GetContentType(ext), Path.GetFileName(matched));
                }
                var matchedVideo = Directory.EnumerateFiles(videoFolder).FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Equals(originalBase, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matchedVideo))
                {
                    var ext = Path.GetExtension(matchedVideo)?.ToLower();
                    return (matchedVideo, GetContentType(ext), Path.GetFileName(matchedVideo));
                }
            }
            return (null, null, null);
        }

        private (string physicalPath, string contentType, string displayName) TryGetArchivedFilePath(DeletedFileLog sop, string rootPath)
        {
            var pdf = TryGetArchivedPdfPath(sop, rootPath);
            if (!string.IsNullOrEmpty(pdf.physicalPath))
                return pdf;
            return TryGetArchivedOriginalPath(sop, rootPath);
        }

        private static string GetContentType(string extension)
        {
            return extension?.ToLower() switch
            {
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" or ".xlt" => "application/vnd.ms-excel",
                ".xlsx" or ".xltx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".wmv" => "video/x-ms-wmv",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreDocument(int id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                var deletedLog = await _context.DeletedFileLogs.FindAsync(id);
                if (deletedLog == null)
                {
                    var msg = "Document not found in archive.";
                    if (isAjax) return Json(new { success = false, error = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction(nameof(Index));
                }

                var restored = await _fileRestoreService.RestoreDocumentAsync(deletedLog);
                if (!restored)
                {
                    var msg = "Document could not be restored. It may already exist in active documents, or files could not be moved.";
                    if (isAjax) return Json(new { success = false, error = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction(nameof(Index));
                }

                if (isAjax) return Json(new { success = true, message = $"Document '{deletedLog.OriginalFileName}' successfully restored." });
                TempData["Success"] = $"Document '{deletedLog.OriginalFileName}' successfully restored.";
                _logger.LogInformation("Document restored: {FileName} by {User}", deletedLog.OriginalFileName, User.Identity?.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring document ID: {DocumentId}", id);
                var msg = $"Error restoring document: {ex.Message}";
                if (isAjax) return StatusCode(500, new { success = false, error = msg });
                TempData["Error"] = msg;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePermanent(int id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                var deletedLog = await _context.DeletedFileLogs.FindAsync(id);
                if (deletedLog == null)
                {
                    var msg = "Document not found in archive.";
                    if (isAjax) return Json(new { success = false, error = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction(nameof(Index));
                }

                await _filePermanentDeleteService.PermanentlyDeleteAsync(deletedLog);

                if (isAjax) return Json(new { success = true, message = $"Document '{deletedLog.OriginalFileName}' permanently deleted." });
                TempData["Success"] = $"Document '{deletedLog.OriginalFileName}' permanently deleted.";
                _logger.LogInformation("Document permanently deleted: {FileName} by {User}", deletedLog.OriginalFileName, User.Identity?.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error permanently deleting document ID: {DocumentId}", id);
                var msg = $"Error permanently deleting document: {ex.Message}";
                if (isAjax) return StatusCode(500, new { success = false, error = msg });
                TempData["Error"] = msg;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkRestoreDocuments([FromForm] List<int> documentIds)
        {
            try
            {
                if (documentIds == null || !documentIds.Any())
                {
                    TempData["Error"] = "No documents selected for restoration.";
                    return RedirectToAction(nameof(Index));
                }

                var deletedLogs = await _context.DeletedFileLogs
                    .Where(d => documentIds.Contains(d.Id))
                    .ToListAsync();

                int successCount = 0;
                foreach (var doc in deletedLogs)
                {
                    try
                    {
                        await _fileRestoreService.RestoreDocumentAsync(doc);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error restoring document {DocumentId}", doc.Id);
                    }
                }

                TempData["Success"] = $"Successfully restored {successCount} out of {documentIds.Count} documents.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error during bulk restoration: {ex.Message}";
                _logger.LogError(ex, "Error in bulk restore operation");
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeletePermanent([FromForm] List<int> documentIds)
        {
            try
            {
                if (documentIds == null || !documentIds.Any())
                {
                    TempData["Error"] = "No documents selected for permanent deletion.";
                    return RedirectToAction(nameof(Index));
                }

                var deletedLogs = await _context.DeletedFileLogs
                    .Where(d => documentIds.Contains(d.Id))
                    .ToListAsync();

                int successCount = 0;
                foreach (var doc in deletedLogs)
                {
                    try
                    {
                        await _filePermanentDeleteService.PermanentlyDeleteAsync(doc);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error permanently deleting document {DocumentId}", doc.Id);
                    }
                }

                TempData["Success"] = $"Successfully permanently deleted {successCount} out of {documentIds.Count} documents.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error during bulk permanent deletion: {ex.Message}";
                _logger.LogError(ex, "Error in bulk permanent delete operation");
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmptyTrash()
        {
            try
            {
                var allDeleted = await _context.DeletedFileLogs.ToListAsync();
                int totalCount = allDeleted.Count;

                if (totalCount == 0)
                {
                    TempData["Info"] = "Trash is already empty.";
                    return RedirectToAction(nameof(Index));
                }

                int successCount = 0;
                foreach (var doc in allDeleted)
                {
                    try
                    {
                        await _filePermanentDeleteService.PermanentlyDeleteAsync(doc);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error emptying trash for document {DocumentId}", doc.Id);
                    }
                }

                TempData["Success"] = $"Trash emptied successfully. {successCount} out of {totalCount} documents permanently deleted.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error emptying trash: {ex.Message}";
                _logger.LogError(ex, "Error emptying trash");
            }

            return RedirectToAction(nameof(Index));
        }
    }
}