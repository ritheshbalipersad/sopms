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

        public DeletedFilesController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            ILogger<DeletedFilesController> logger,
            FileRestoreService fileRestoreService,
            FilePermanentDeleteService filePermanentDeleteService)
        {
            _context = context;
            _env = env;
            _logger = logger;
            _fileRestoreService = fileRestoreService;
            _filePermanentDeleteService = filePermanentDeleteService;
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
                    return View(deletedFiles26);
                }
                catch
                {
                    return View(new List<DeletedFileLog>());
                }
            }
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

            var archiveRoot = Path.Combine(_env.WebRootPath, "Archive", "Deleted");
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreDocument(int id)
        {
            try
            {
                var deletedLog = await _context.DeletedFileLogs.FindAsync(id);
                if (deletedLog == null)
                {
                    TempData["Error"] = "Document not found in archive.";
                    return RedirectToAction(nameof(Index));
                }

                await _fileRestoreService.RestoreDocumentAsync(deletedLog);

                TempData["Success"] = $"Document '{deletedLog.OriginalFileName}' successfully restored.";
                _logger.LogInformation("Document restored: {FileName} by {User}", deletedLog.OriginalFileName, User.Identity.Name);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error restoring document: {ex.Message}";
                _logger.LogError(ex, "Error restoring document ID: {DocumentId}", id);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePermanent(int id)
        {
            try
            {
                var deletedLog = await _context.DeletedFileLogs.FindAsync(id);
                if (deletedLog == null)
                {
                    TempData["Error"] = "Document not found in archive.";
                    return RedirectToAction(nameof(Index));
                }

                await _filePermanentDeleteService.PermanentlyDeleteAsync(deletedLog);

                TempData["Success"] = $"Document '{deletedLog.OriginalFileName}' permanently deleted.";
                _logger.LogInformation("Document permanently deleted: {FileName} by {User}", deletedLog.OriginalFileName, User.Identity.Name);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error permanently deleting document: {ex.Message}";
                _logger.LogError(ex, "Error permanently deleting document ID: {DocumentId}", id);
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