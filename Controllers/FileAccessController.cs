using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SOPMSApp.Controllers;

public class FileAccessController : Controller
{
    private readonly StorageSettings _storageSettings;
    private readonly ILogger<FileUploadController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _hostingEnvironment;

    public FileAccessController(IOptions<StorageSettings> storageSettings, ILogger<FileUploadController> logger, IConfiguration configuration, IWebHostEnvironment hostingEnvironment)
    {
        _storageSettings = storageSettings.Value;
        _logger = logger;
        _configuration = configuration;
        _hostingEnvironment = hostingEnvironment;
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
    public IActionResult Originals(string docType, string fileName)
    {
        return ServeFile(System.IO.Path.Combine("Originals", docType), fileName);
    }

    [HttpGet]
    public IActionResult Videos(string fileName)
    {
        return ServeFile("Videos", fileName);
    }

    [HttpGet]
    public IActionResult GetPdf(string fileName, string docType = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return NotFound("File name missing.");

        var safeFile = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(safeFile))
            return NotFound("File name is invalid.");

        // Always expect PDF
        var targetPdf = safeFile + ".pdf";

        var basePath = _storageSettings.BasePath;
        if (string.IsNullOrEmpty(basePath))
            return StatusCode(500, "Storage base path missing.");

        // Build all primary search roots
        var searchRoots = new List<string>
        {
            Path.Combine(basePath, "PDFs"),
            Path.Combine(basePath, "Uploads"),
            Path.Combine(basePath, "Originals")
        };

        // If docType is provided, search inside Originals/{docType} first
        if (!string.IsNullOrEmpty(docType))
        {
            var typedFolder = Path.Combine(basePath, "Originals", docType);
            searchRoots.Insert(0, typedFolder);
        }

        string foundFile = null;

        foreach (var root in searchRoots.Distinct())
        {
            if (!Directory.Exists(root))
                continue;

            // Search ANYWHERE under this root
            var files = Directory.GetFiles(root, "*.pdf", SearchOption.AllDirectories);

            foundFile = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(targetPdf, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(f).Equals(safeFile, StringComparison.OrdinalIgnoreCase)
            );

            if (foundFile != null)
                break;
        }

        if (foundFile == null)
        {
            _logger.LogWarning($"PDF '{targetPdf}' not found. Roots: {string.Join(", ", searchRoots)}");
            return NotFound($"PDF '{fileName}' was not found in the storage system.");
        }

        var fileStream = new FileStream(foundFile, FileMode.Open, FileAccess.Read);
        return new FileStreamResult(fileStream, "application/pdf")
        {
            FileDownloadName = null
        };
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