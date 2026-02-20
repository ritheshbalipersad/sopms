using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;

public class FileRestoreService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<FileRestoreService> _logger;

    public FileRestoreService(ApplicationDbContext context, ILogger<FileRestoreService> logger, IConfiguration config)
    {
        _context = context;
        _logger = logger;
        _config = config;
    }

    public async Task<bool> RestoreDocumentAsync(DeletedFileLog deletedLog)
    {
        try
        {
            _logger.LogInformation("Starting restoration of document: {FileName}", deletedLog.OriginalFileName);

            // 1. Validate that files exist in archive before proceeding
            if (!CanRestore(deletedLog))
            {
                _logger.LogWarning("Cannot restore document - files not found in archive: {FileName}", deletedLog.OriginalFileName);
                return false;
            }

            // 2. Check if this deleted file is linked to a Structured SOP
            var structuredSop = await _context.StructuredSops
                .Include(s => s.Steps)
                .FirstOrDefaultAsync(s => s.SopNumber == deletedLog.SOPNumber);

            var isStructured = structuredSop != null;

            if (isStructured)
            {
                // Restore the SOP record if it was soft-deleted or archived
                if (structuredSop.ArchivedOn.HasValue)
                {
                    structuredSop.ArchivedOn = null;
                    structuredSop.Status = "Restored";
                    _context.StructuredSops.Update(structuredSop);
                    _logger.LogInformation("Structured SOP restored in DB: {SopNumber}", structuredSop.SopNumber);
                }
            }


            // 3. Get the document type for directory structure (use Bulletin match or safe fallback)
            var docType = await GetDocumentTypeForRestoreAsync(deletedLog.DocType);
            if (string.IsNullOrEmpty(docType))
            {
                _logger.LogWarning("Could not determine document type for restoration: {DocType}", deletedLog.DocType);
                return false;
            }

            // 3. Move files from archive back to original locations
            var filesMoved = await MoveFilesToOriginalLocations(deletedLog, docType);
            if (!filesMoved)
            {
                _logger.LogError("Failed to move files for document: {FileName}", deletedLog.OriginalFileName);
                return false;
            }

            // 4. Update database records
            var dbUpdated = await UpdateDatabaseRecords(deletedLog);
            if (!dbUpdated)
            {
                // If database update fails, move files back to archive
                await RollbackFileMovement(deletedLog, docType);
                return false;
            }

            // 5. Remove from deleted logs
            _context.DeletedFileLogs.Remove(deletedLog);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully restored document: {FileName} to original location", deletedLog.OriginalFileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore document: {FileName}", deletedLog.OriginalFileName);

            // Attempt rollback on failure (use same doc type resolution as restore)
            var docType = await GetDocumentTypeForRestoreAsync(deletedLog.DocType);
            if (!string.IsNullOrEmpty(docType))
            {
                await RollbackFileMovement(deletedLog, docType);
            }
            throw;
        }
    }

    private async Task<bool> MoveFilesToOriginalLocations(DeletedFileLog deletedLog, string docType)
    {
        try
        {
            var storageRoot = _config["StorageSettings:BasePath"] ?? "D:\\SOPMS_Documents";
            var archiveRoot = Path.Combine(storageRoot, "Archive", "Deleted");

            // Original target paths (where files should be restored to)
            string originalsBasePath = Path.Combine(storageRoot, "Originals", docType);
            string pdfBasePath = Path.Combine(storageRoot, "PDFs", docType);
            string videosBasePath = Path.Combine(storageRoot, "Videos", docType);

            bool anyFileMoved = false;

            // Restore PDF file
            if (!string.IsNullOrEmpty(deletedLog.FileName))
            {
                var archivedPdfPath = Path.Combine(archiveRoot, "PDFs", deletedLog.FileName);
                var originalPdfPath = Path.Combine(pdfBasePath, deletedLog.FileName);

                if (File.Exists(archivedPdfPath))
                {
                    Directory.CreateDirectory(pdfBasePath);

                    // Handle file name conflicts
                    var finalPdfPath = await HandleFileConflict(originalPdfPath);

                    File.Move(archivedPdfPath, finalPdfPath);
                    _logger.LogInformation("Restored PDF to original location: {FileName} -> {FinalPath}",
                        deletedLog.FileName, finalPdfPath);
                    anyFileMoved = true;
                }
                else
                {
                    _logger.LogWarning("PDF file not found in archive: {FileName}", deletedLog.FileName);
                }
            }

            // Restore original file
            if (!string.IsNullOrEmpty(deletedLog.OriginalFileName))
            {
                var archivedOriginalPath = Path.Combine(archiveRoot, "Originals", deletedLog.OriginalFileName);
                var originalFilePath = Path.Combine(originalsBasePath, deletedLog.OriginalFileName);

                if (File.Exists(archivedOriginalPath))
                {
                    Directory.CreateDirectory(originalsBasePath);

                    // Handle file name conflicts
                    var finalOriginalPath = await HandleFileConflict(originalFilePath);

                    File.Move(archivedOriginalPath, finalOriginalPath);
                    _logger.LogInformation("Restored original file to original location: {FileName} -> {FinalPath}",
                        deletedLog.OriginalFileName, finalOriginalPath);
                    anyFileMoved = true;
                }
                else
                {
                    _logger.LogWarning("Original file not found in archive: {FileName}", deletedLog.OriginalFileName);
                }

                // Restore video file if applicable
                var ext = Path.GetExtension(deletedLog.OriginalFileName)?.ToLower();
                if (IsVideoFile(ext))
                {
                    var archivedVideoPath = Path.Combine(archiveRoot, "Videos", deletedLog.OriginalFileName);
                    var originalVideoPath = Path.Combine(videosBasePath, deletedLog.OriginalFileName);

                    if (File.Exists(archivedVideoPath))
                    {
                        Directory.CreateDirectory(videosBasePath);

                        // Handle file name conflicts
                        var finalVideoPath = await HandleFileConflict(originalVideoPath);

                        File.Move(archivedVideoPath, finalVideoPath);
                        _logger.LogInformation("Restored video file to original location: {FileName} -> {FinalPath}",
                            deletedLog.OriginalFileName, finalVideoPath);
                        anyFileMoved = true;
                    }
                    else
                    {
                        _logger.LogWarning("Video file not found in archive: {FileName}", deletedLog.OriginalFileName);
                    }
                }
            }

            return anyFileMoved;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving files to original locations for: {FileName}", deletedLog.OriginalFileName);
            return false;
        }
    }

    private async Task<string> HandleFileConflict(string targetPath)
    {
        if (!File.Exists(targetPath))
            return targetPath;

        // Generate unique filename
        var directory = Path.GetDirectoryName(targetPath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        var counter = 1;

        string newPath;
        do
        {
            newPath = Path.Combine(directory!, $"{fileNameWithoutExt}_restored_{counter}{extension}");
            counter++;
        } while (File.Exists(newPath));

        _logger.LogInformation("File conflict resolved: {Original} -> {New}", targetPath, newPath);
        return newPath;
    }

    private async Task RollbackFileMovement(DeletedFileLog deletedLog, string docType)
    {
        try
        {
            _logger.LogWarning("Attempting rollback for failed restoration: {FileName}", deletedLog.OriginalFileName);

            var storageRoot = _config["StorageSettings:BasePath"] ?? "D:\\SOPMS_Documents";
            var archiveRoot = Path.Combine(storageRoot, "Archive", "Deleted");

            // Original target paths
            string originalsBasePath = Path.Combine(storageRoot, "Originals", docType);
            string pdfBasePath = Path.Combine(storageRoot, "PDFs", docType);
            string videosBasePath = Path.Combine(storageRoot, "Videos", docType);

            // Rollback PDF file
            if (!string.IsNullOrEmpty(deletedLog.FileName))
            {
                var originalPdfPath = Path.Combine(pdfBasePath, deletedLog.FileName);
                var archivedPdfPath = Path.Combine(archiveRoot, "PDFs", deletedLog.FileName);

                if (File.Exists(originalPdfPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(archivedPdfPath)!);
                    File.Move(originalPdfPath, archivedPdfPath, true);
                    _logger.LogInformation("Rolled back PDF to archive: {FileName}", deletedLog.FileName);
                }
            }

            // Rollback original file
            if (!string.IsNullOrEmpty(deletedLog.OriginalFileName))
            {
                var originalFilePath = Path.Combine(originalsBasePath, deletedLog.OriginalFileName);
                var archivedOriginalPath = Path.Combine(archiveRoot, "Originals", deletedLog.OriginalFileName);

                if (File.Exists(originalFilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(archivedOriginalPath)!);
                    File.Move(originalFilePath, archivedOriginalPath, true);
                    _logger.LogInformation("Rolled back original to archive: {FileName}", deletedLog.OriginalFileName);
                }

                // Rollback video file if applicable
                var ext = Path.GetExtension(deletedLog.OriginalFileName)?.ToLower();
                if (IsVideoFile(ext))
                {
                    var originalVideoPath = Path.Combine(videosBasePath, deletedLog.OriginalFileName);
                    var archivedVideoPath = Path.Combine(archiveRoot, "Videos", deletedLog.OriginalFileName);

                    if (File.Exists(originalVideoPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(archivedVideoPath)!);
                        File.Move(originalVideoPath, archivedVideoPath, true);
                        _logger.LogInformation("Rolled back video to archive: {FileName}", deletedLog.OriginalFileName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rollback for: {FileName}", deletedLog.OriginalFileName);
        }
    }

    /// <summary>Returns a document type safe for path use: Bulletin match, or sanitized DocType, or "General".</summary>
    private async Task<string> GetDocumentTypeForRestoreAsync(string docType)
    {
        try
        {
            var validDocTypes = await GetDistinctDocumentsAsync();
            if (validDocTypes != null && validDocTypes.Count > 0 && !string.IsNullOrEmpty(docType))
            {
                var matched = validDocTypes
                    .FirstOrDefault(d => d.Equals(docType, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matched))
                    return matched;
            }
            // Fallback: use sanitized DocType from log or "General" so restore can proceed
            var fallback = string.IsNullOrWhiteSpace(docType)
                ? "General"
                : string.Join("_", docType.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrEmpty(fallback))
                fallback = "General";
            _logger.LogInformation("Using document type fallback for restore: {Fallback} (original: {DocType})", fallback, docType);
            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document type for restore: {DocType}", docType);
            return string.IsNullOrWhiteSpace(docType) ? "General" : "General";
        }
    }

    private async Task<string> GetValidDocumentTypeAsync(string docType)
    {
        if (string.IsNullOrEmpty(docType))
            return null;
        try
        {
            var validDocTypes = await GetDistinctDocumentsAsync();
            return validDocTypes?
                .FirstOrDefault(d => d.Equals(docType, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting valid document type for: {DocType}", docType);
            return null;
        }
    }

    private async Task<List<string>> GetDistinctDocumentsAsync()
    {
        var documentTypes = new List<string>();
        string connStr = _config.GetConnectionString("DefaultConnection"); // Update with your connection string name

        try
        {
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                string sql = "SELECT BulletinName AS DocumentType FROM Bulletin"; // Update with your actual table/column
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    await conn.OpenAsync();
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string documentType = reader["DocumentType"]?.ToString();
                            if (!string.IsNullOrEmpty(documentType))
                            {
                                documentTypes.Add(documentType);
                            }
                        }
                    }
                }
            }
            return documentTypes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching distinct document types from database");
            return new List<string>();
        }
    }

    private bool IsVideoFile(string extension)
    {
        return extension?.ToLower() switch
        {
            ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv" or ".webm" or ".flv" or ".m4v" => true,
            _ => false
        };
    }

    private async Task<bool> UpdateDatabaseRecords(DeletedFileLog deletedLog)
    {
        try
        {
            if (deletedLog == null)
            {
                _logger.LogWarning("Skipped database update — DeletedFileLog is null.");
                return false;
            }

            // Check if document already exists (prevent duplicates)
            var existingDoc = await _context.DocRegisters
                .FirstOrDefaultAsync(d => d.SopNumber == deletedLog.SOPNumber 
                && d.Revision == deletedLog.Revision 
                && d.OriginalFile == deletedLog.OriginalFileName);

            if (existingDoc != null)
            {
                _logger.LogWarning("Document already exists in active records: {FileName}", deletedLog.OriginalFileName);
                return false;
            }

            // Determine content type based on file extension
            var fileExtension = Path.GetExtension(deletedLog.OriginalFileName);
            var contentType = GetMimeType(fileExtension);

            // Create new DocRegister record from DeletedFileLog
            var docRegister = new DocRegister
            {
                SopNumber = deletedLog.SOPNumber,
                uniqueNumber = Guid.NewGuid().ToString(),
                ContentType = contentType ?? "application/octet-stream",
                Revision = deletedLog.Revision,
                Author = deletedLog.Author ?? deletedLog.DeletedBy ?? "System",
                Department = deletedLog.Department ?? "Restored",
                FileName = deletedLog.FileName,
                OriginalFile = deletedLog.OriginalFileName,
                FileSize = deletedLog.FileSize,
                UserEmail = deletedLog.UserEmail,
                DepartmentSupervisor = deletedLog.DepartmentSupervisor,
                SupervisorEmail = deletedLog.SupervisorEmail,
                Area = deletedLog.Area ?? "General",
                DocType = deletedLog.DocType ?? "Unknown",
                EffectiveDate = deletedLog.EffectiveDate ?? DateTime.Now,
                UploadDate = DateTime.Now,
                Status = "Approved",
                IsArchived = false,
                ArchivedOn = null,
            };

            _context.DocRegisters.Add(docRegister);
            await _context.SaveChangesAsync();

            _logger.LogInformation("✅ Restored document '{FileName}' (SOP: {SOP}) from DeletedFileLog into DocRegisters.",
                deletedLog.OriginalFileName, deletedLog.SOPNumber);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to restore document from DeletedFileLog: {FileName}", deletedLog?.OriginalFileName);
            return false;
        }
    }

    private string GetMimeType(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return "application/octet-stream";

        extension = extension.ToLower();

        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlt" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xltx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.template",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".webm" => "video/webm",
            ".ogg" => "video/ogg",
            ".wmv" => "video/x-ms-wmv",
            ".mkv" => "video/x-matroska",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            _ => "application/octet-stream"
        };
    }

    public bool CanRestore(DeletedFileLog deletedLog)
    {
        try
        {
            var storageRoot = _config["StorageSettings:BasePath"] ?? "D:\\SOPMS_Documents";
            var archiveRoot = Path.Combine(storageRoot, "Archive", "Deleted");

            // Check if PDF exists
            if (!string.IsNullOrEmpty(deletedLog.FileName))
            {
                var pdfPath = Path.Combine(archiveRoot, "PDFs", deletedLog.FileName);
                if (File.Exists(pdfPath))
                    return true;
            }

            // Check if original file exists
            if (!string.IsNullOrEmpty(deletedLog.OriginalFileName))
            {
                var originalPath = Path.Combine(archiveRoot, "Originals", deletedLog.OriginalFileName);
                if (File.Exists(originalPath))
                    return true;

                // Check if video exists
                var ext = Path.GetExtension(deletedLog.OriginalFileName)?.ToLower();
                if (IsVideoFile(ext))
                {
                    var videoPath = Path.Combine(archiveRoot, "Videos", deletedLog.OriginalFileName);
                    if (File.Exists(videoPath))
                        return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if document can be restored: {FileName}", deletedLog.OriginalFileName);
            return false;
        }
    }
}