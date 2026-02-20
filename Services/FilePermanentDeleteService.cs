using Microsoft.AspNetCore.Hosting;
using SOPMSApp.Data;
using SOPMSApp.Models;

namespace SOPMSApp.Services
{
    public class FilePermanentDeleteService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<FilePermanentDeleteService> _logger;

        public FilePermanentDeleteService(
            ApplicationDbContext context,
            IConfiguration config,
            ILogger<FilePermanentDeleteService> logger,
            IWebHostEnvironment env)
        {
            _context = context;
            _config = config;
            _logger = logger;
            _env = env;
        }

        private string GetArchiveRootPath()
        {
            var basePath = _config["StorageSettings:BasePath"];
            if (!string.IsNullOrEmpty(basePath))
                return Path.Combine(basePath, "Archive", "Deleted");
            return Path.Combine(_env.WebRootPath, "Archive", "Deleted");
        }

        public async Task PermanentlyDeleteAsync(DeletedFileLog deletedLog)
        {
            try
            {
                _logger.LogInformation("Starting permanent deletion of document: {FileName}", deletedLog.OriginalFileName);

                // 1. Permanently delete files from archive
                DeleteArchivedFiles(deletedLog);

                // 2. Remove from database
                _context.DeletedFileLogs.Remove(deletedLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully permanently deleted document: {FileName}", deletedLog.OriginalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to permanently delete document: {FileName}", deletedLog.OriginalFileName);
                throw;
            }
        }

        private void DeleteArchivedFiles(DeletedFileLog deletedLog)
        {
            var archiveRoot = GetArchiveRootPath();

            // Delete PDF file
            if (!string.IsNullOrEmpty(deletedLog.FileName))
            {
                var pdfPath = Path.Combine(archiveRoot, "PDFs", deletedLog.FileName);
                SafeDeleteFile(pdfPath);
            }

            // Delete original file
            if (!string.IsNullOrEmpty(deletedLog.OriginalFileName))
            {
                var originalPath = Path.Combine(archiveRoot, "Originals", deletedLog.OriginalFileName);
                SafeDeleteFile(originalPath);

                // Delete video file if applicable
                var ext = Path.GetExtension(deletedLog.OriginalFileName)?.ToLower();
                if (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".wmv" || ext == ".mkv")
                {
                    var videoPath = Path.Combine(archiveRoot, "Videos", deletedLog.OriginalFileName);
                    SafeDeleteFile(videoPath);
                }
            }
        }

        private void SafeDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Deleted file: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete file: {FilePath}", filePath);
                // Continue with other files even if one fails
            }
        }
    }
}