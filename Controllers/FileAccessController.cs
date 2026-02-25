using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SOPMSApp.Controllers;
using SOPMSApp.Data;
using SOPMSApp.Services;

public class FileAccessController : Controller
{
    private readonly StorageSettings _storageSettings;
    private readonly ILogger<FileUploadController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly ApplicationDbContext _context;
    private readonly IDocumentAuditLogService _auditLog;

    public FileAccessController(IOptions<StorageSettings> storageSettings, ILogger<FileUploadController> logger, IConfiguration configuration, IWebHostEnvironment hostingEnvironment, ApplicationDbContext context, IDocumentAuditLogService auditLog)
    {
        _storageSettings = storageSettings.Value;
        _logger = logger;
        _configuration = configuration;
        _hostingEnvironment = hostingEnvironment;
        _context = context;
        _auditLog = auditLog;
    }

    [HttpGet]
    public IActionResult DownloadFile(string fileName, string docType = null)
    {
        if (string.IsNullOrEmpty(fileName))
            return NotFound();

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName))
            return NotFound();

        var basePath = _storageSettings.BasePath;
        if (string.IsNullOrEmpty(basePath))
            return StatusCode(500, "Storage configuration is missing.");

        // 🔍 Search for file in all possible locations
        var possiblePaths = new List<string>
        {
            Path.Combine(basePath, "PDFs", safeFileName),
            Path.Combine(basePath, "Originals", safeFileName),
            Path.Combine(basePath, "Originals", docType ?? "", safeFileName),
            Path.Combine(basePath, "Uploads", safeFileName),
            Path.Combine(basePath, "Videos", safeFileName)
        };

        string actualFilePath = possiblePaths.FirstOrDefault(System.IO.File.Exists);

        if (actualFilePath == null)
            return NotFound($"File '{fileName}' not found.");

        var contentType = GetContentType(Path.GetExtension(actualFilePath).ToLower());
        return PhysicalFile(actualFilePath, contentType, safeFileName);
    }

    [HttpGet]
    public IActionResult PDFs(string fileName)
    {
        return ServeFile("PDFs", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> Originals(string docType, string fileName)
    {
        var safeFileName = System.IO.Path.GetFileName(fileName);
        if (!string.IsNullOrEmpty(safeFileName) && !string.IsNullOrEmpty(docType))
        {
            var doc = await _context.DocRegisters
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.OriginalFile == safeFileName && d.DocType == docType && (d.IsArchived != true));
            if (doc != null)
            {
                var performedBy = User.FindFirst("LaborName")?.Value ?? User.Identity?.Name ?? "System";
                await _auditLog.LogAsync(doc.Id, doc.SopNumber ?? "", "Downloaded", performedBy, "Original file downloaded", doc.OriginalFile);
            }
        }
        return ServeFile(System.IO.Path.Combine("Originals", docType), fileName);
    }

    [HttpGet]
    public IActionResult Videos(string fileName)
    {
        return ServeFile("Videos", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> GetPdf(string fileName, string docType = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return NotFound("File name missing.");

        var safeFile = Path.GetFileName(fileName);
        var baseNoExt = Path.GetFileNameWithoutExtension(safeFile);
        if (string.IsNullOrEmpty(baseNoExt))
            return NotFound("File name is invalid.");

        var targetPdf = safeFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? safeFile : baseNoExt + ".pdf";

        var basePath = _storageSettings.BasePath;
        if (string.IsNullOrEmpty(basePath))
            return StatusCode(500, "Storage base path missing.");

        string foundFile = null;

        bool MatchPdf(string fullPath)
        {
            var name = Path.GetFileName(fullPath);
            var nameNoExt = Path.GetFileNameWithoutExtension(fullPath);
            return name.Equals(targetPdf, StringComparison.OrdinalIgnoreCase) ||
                   nameNoExt.Equals(baseNoExt, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(docType))
        {
            var pdfInDocType = Path.Combine(basePath, "PDFs", docType, targetPdf);
            if (System.IO.File.Exists(pdfInDocType))
                foundFile = pdfInDocType;
            if (foundFile == null)
            {
                var basePdfInDocType = Path.Combine(basePath, "PDFs", docType, baseNoExt + ".pdf");
                if (System.IO.File.Exists(basePdfInDocType))
                    foundFile = basePdfInDocType;
            }
            if (foundFile == null)
            {
                var originalPdfInDocType = Path.Combine(basePath, "Originals", docType, targetPdf);
                if (System.IO.File.Exists(originalPdfInDocType))
                    foundFile = originalPdfInDocType;
            }
        }

        if (foundFile == null)
        {
            var pdfRoot = Path.Combine(basePath, "PDFs");
            if (Directory.Exists(pdfRoot))
            {
                var pdfs = Directory.GetFiles(pdfRoot, "*.pdf", SearchOption.AllDirectories);
                foundFile = pdfs.FirstOrDefault(f => MatchPdf(f));
            }
        }

        if (foundFile == null)
        {
            var uploads = Path.Combine(basePath, "Uploads");
            if (Directory.Exists(uploads))
            {
                var pdfs = Directory.GetFiles(uploads, "*.pdf", SearchOption.AllDirectories);
                foundFile = pdfs.FirstOrDefault(f => MatchPdf(f));
            }
        }

        if (foundFile == null)
        {
            var originalsRoot = Path.Combine(basePath, "Originals");
            if (Directory.Exists(originalsRoot))
            {
                var pdfs = Directory.GetFiles(originalsRoot, "*.pdf", SearchOption.AllDirectories);
                foundFile = pdfs.FirstOrDefault(f => MatchPdf(f));
            }
        }

        if (foundFile == null && _hostingEnvironment?.WebRootPath != null)
        {
            var webRootPaths = new[]
            {
                Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "PDFs"),
                Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Uploads"),
                Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Originals"),
                Path.Combine(_hostingEnvironment.WebRootPath, "PDFs"),
                Path.Combine(_hostingEnvironment.WebRootPath, "upload")
            };
            if (!string.IsNullOrWhiteSpace(docType))
            {
                var withDocType = Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "PDFs", docType);
                if (Directory.Exists(withDocType))
                {
                    var direct = Path.Combine(withDocType, targetPdf);
                    if (System.IO.File.Exists(direct)) foundFile = direct;
                }
                if (foundFile == null)
                {
                    var origDocType = Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Originals", docType);
                    if (Directory.Exists(origDocType))
                    {
                        var direct = Path.Combine(origDocType, targetPdf);
                        if (System.IO.File.Exists(direct)) foundFile = direct;
                    }
                }
            }
            foreach (var dir in webRootPaths)
            {
                if (foundFile != null) break;
                if (!Directory.Exists(dir)) continue;
                var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories);
                foundFile = pdfs.FirstOrDefault(f => MatchPdf(f));
            }
        }

        if (foundFile == null)
        {
            _logger.LogWarning("PDF '{Target}' not found. BasePath: {BasePath}, DocType: {DocType}", targetPdf, basePath, docType ?? "(none)");
            return NotFound("PDF was not found in the storage system.");
        }

        var docForAudit = await _context.DocRegisters
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                (d.FileName == targetPdf || d.OriginalFile == targetPdf) &&
                (string.IsNullOrEmpty(docType) || d.DocType == docType) &&
                (d.IsArchived != true));
        if (docForAudit != null)
        {
            var performedBy = User.FindFirst("LaborName")?.Value ?? User.Identity?.Name ?? "System";
            await _auditLog.LogAsync(docForAudit.Id, docForAudit.SopNumber ?? "", "Downloaded", performedBy, "PDF viewed/downloaded", docForAudit.FileName ?? docForAudit.OriginalFile);
        }

        Response.Headers["Content-Disposition"] = "inline";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        var fileStream = new FileStream(foundFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new FileStreamResult(fileStream, "application/pdf");
    }


    [HttpGet]
    public IActionResult GetVideo(string videoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(videoPath))
                return NotFound("Video path is required.");

            // Extract only the filename (security measure)
            var fileName = Path.GetFileName(videoPath);
            if (string.IsNullOrEmpty(fileName))
                return NotFound("Invalid video file name.");

            // ✅ Use configured base path
            var basePath = _storageSettings.BasePath;
            if (string.IsNullOrEmpty(basePath))
                return StatusCode(500, "Storage configuration is missing.");

            // Define full base video directory
            var videosBasePath = Path.Combine(basePath, "Videos");

            if (!Directory.Exists(videosBasePath))
            {
                _logger.LogError($"Videos base directory not found at: {videosBasePath}");
                return StatusCode(500, "Video storage directory not found.");
            }

            // 🔍 Search for the video file recursively under Videos\<DocType>\
            var matchingFiles = Directory.GetFiles(videosBasePath, fileName, SearchOption.AllDirectories);

            if (matchingFiles.Length == 0)
            {
                _logger.LogWarning($"Video '{fileName}' not found under '{videosBasePath}'.");
                return NotFound($"Video '{fileName}' not found.");
            }

            // ✅ Use the first found match
            var fullVideoPath = matchingFiles.First();

            // Detect MIME type
            var extension = Path.GetExtension(fullVideoPath).ToLowerInvariant();
            var contentType = GetVideoContentType(extension);

            // Stream file (enable seeking for large files)
            var stream = new FileStream(fullVideoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

            _logger.LogInformation($"Streaming video: {fullVideoPath}");

            return File(stream, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving video at path: {videoPath}", videoPath);
            return StatusCode(500, "An error occurred while loading the video.");
        }
    }
    
    [HttpGet]
    public IActionResult GetStructuredImage(string fileName, string type)
    {
        if (string.IsNullOrEmpty(fileName))
            return NotFound();

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName))
            return NotFound();

        var basePath = _storageSettings.BasePath;
        if (string.IsNullOrEmpty(basePath))
            return StatusCode(500, "Storage configuration is missing.");

        string subFolder = type?.ToLower() switch
        {
            "instructions" => "StructuredSop/instructions",
            "keypoints" => "StructuredSop/keypoints",
            _ => "StructuredSop"
        };

        var filePath = Path.Combine(basePath, subFolder, safeFileName);

        if (!System.IO.File.Exists(filePath))
        {
            var legacyPath = Path.Combine(_hostingEnvironment.WebRootPath, "Documents", subFolder, safeFileName);
            if (System.IO.File.Exists(legacyPath))
            {
                filePath = legacyPath;
            }
            else
            {
                return NotFound();
            }
        }

        var extension = Path.GetExtension(filePath).ToLower();
        var contentType = GetContentType(extension);

        return PhysicalFile(filePath, contentType, safeFileName);
    }

    // check file availability
    [HttpGet]
    public IActionResult CheckFileAvailability(string originalFile, string fileName, string videoPath)
    {
        var result = new FileAvailabilityResult();

        result.HasPdf = CheckPdfAvailability(originalFile, fileName);
        result.HasVideo = CheckVideoAvailability(videoPath);

        if (result.HasPdf)
            result.PdfUrl = Url.Action("GetPdf", "FileAccess", new { fileName = Path.GetFileName(fileName) }, Request.Scheme);

        if (result.HasVideo)
            result.VideoUrl = Url.Action("GetVideo", "FileAccess", new { videoPath = videoPath }, Request.Scheme);

        return Ok(result);
    }


    private string GetContentType(string extension)
    {
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".wmv" => "video/x-ms-wmv",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".ogg" => "video/ogg",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    private IActionResult ServeFile(string subFolder, string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return NotFound();

        // Security: Ensure fileName doesn't contain path traversal
        var safeFileName = System.IO.Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName))
            return NotFound();

        var filePath = System.IO.Path.Combine(_storageSettings.BasePath, subFolder, safeFileName);

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        // Determine content type based on file extension
        var extension = System.IO.Path.GetExtension(filePath).ToLower();
        var contentType = GetContentType(extension);

        // Return the physical file
        return PhysicalFile(filePath, contentType, safeFileName);
    }

    private string GetVideoContentType(string extension)
    {
        return extension switch
        {
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }

    private bool CheckPdfAvailability(string originalFile, string fileName)
    {
        if (string.IsNullOrEmpty(originalFile) && string.IsNullOrEmpty(fileName))
            return false;

        var basePath = _storageSettings.BasePath;
        if (string.IsNullOrEmpty(basePath))
            return false;

        // All PDF directories that may contain department folders
        var pdfDirectories = new[]
        {
            Path.Combine(basePath, "PDFs"),
            Path.Combine(basePath, "ArchivedPDFs"),
            Path.Combine(basePath, "Originals")
        };

        // Determine the actual PDF filename
        var pdfCandidate = Path.GetFileName(
            !string.IsNullOrEmpty(originalFile) && originalFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? originalFile
            : fileName
        );

        if (string.IsNullOrEmpty(pdfCandidate))
            return false;

        // Recursively search through all subfolders (e.g., Maintenance, Production)
        foreach (var dir in pdfDirectories)
        {
            if (!Directory.Exists(dir))
                continue;

            var matches = Directory.GetFiles(dir, pdfCandidate, SearchOption.AllDirectories);
            if (matches.Any())
                return true;
        }

        return false;
    }

    private bool CheckVideoAvailability(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath))
            return false;

        var basePath = _storageSettings.BasePath;
        if (string.IsNullOrEmpty(basePath))
            return false;

        var videosBasePath = Path.Combine(basePath, "Videos");
        if (!Directory.Exists(videosBasePath))
            return false;

        var videoFileName = Path.GetFileName(videoPath);
        if (string.IsNullOrEmpty(videoFileName))
            return false;

        // Recursively search inside all department folders under Videos
        var matchingFiles = Directory.GetFiles(videosBasePath, videoFileName, SearchOption.AllDirectories);
        return matchingFiles.Any();
    }


}

public class StorageSettings
{
    public string BasePath { get; set; }
}

public class FileAvailabilityResult
{
    public bool HasPdf { get; set; }
    public bool HasVideo { get; set; }
    public string PdfUrl { get; set; }
    public string VideoUrl { get; set; }
}