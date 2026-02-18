using Microsoft.AspNetCore.Hosting;
using SOPMSApp.Data;
using SOPMSApp.Models;
using System.IO;

namespace SOPMSApp.Services
{
    public class DocRevisionService : IDocRevisionService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context;

        public DocRevisionService(IWebHostEnvironment env, ApplicationDbContext context)
        {
            _env = env;
            _context = context;
        }

        public async Task<(bool success, string message)> ReviseDocumentAsync(DocRegister sop, IFormFile revisedOriginal, IFormFile? revisedPdf, DateTime effectiveDate, string documentType)
        {
            if (revisedOriginal == null || revisedOriginal.Length == 0)
                return (false, "Revised original file is required.");

            if (!string.Equals(revisedOriginal.FileName, sop.OriginalFile, StringComparison.OrdinalIgnoreCase))
                return (false, $"Uploaded file name must match original file: {sop.OriginalFile}");

            if (revisedOriginal.Length > 10 * 1024 * 1024)
                return (false, "File size must not exceed 10MB.");

            string revisionPrefix = "Rev: ";
            if (!sop.Revision.StartsWith(revisionPrefix) || !int.TryParse(sop.Revision.Substring(revisionPrefix.Length), out int currentRev))
                return (false, "Invalid revision number format.");

            try
            {
                string archiveFolder = Path.Combine(_env.WebRootPath, "RevisionArchives");
                Directory.CreateDirectory(archiveFolder);

                string originalsPath = Path.Combine(_env.WebRootPath, "Originals", sop.OriginalFile);
                ArchiveFile(originalsPath, archiveFolder);

                string revisedOriginalPath = Path.Combine(_env.WebRootPath, "Originals", revisedOriginal.FileName);
                using (var stream = new FileStream(revisedOriginalPath, FileMode.Create))
                {
                    await revisedOriginal.CopyToAsync(stream);
                }
                sop.OriginalFile = revisedOriginal.FileName;

                if (revisedPdf != null && revisedPdf.Length > 0)
                {
                    if (Path.GetExtension(revisedPdf.FileName).ToLower() != ".pdf")
                        return (false, "PDF must have a .pdf extension.");

                    string originalName = Path.GetFileNameWithoutExtension(sop.OriginalFile);
                    string pdfName = Path.GetFileNameWithoutExtension(revisedPdf.FileName);

                    if (!string.Equals(pdfName, originalName, StringComparison.OrdinalIgnoreCase))
                        return (false, "PDF file name must match the original (without extension).");

                    string uploadPath = Path.Combine(_env.WebRootPath, "upload", sop.FileName);
                    ArchiveFile(uploadPath, archiveFolder);

                    string revisedPdfPath = Path.Combine(_env.WebRootPath, "upload", revisedPdf.FileName);
                    using (var stream = new FileStream(revisedPdfPath, FileMode.Create))
                    {
                        await revisedPdf.CopyToAsync(stream);
                    }

                    sop.FileName = revisedPdf.FileName;
                }

                // Update metadata
                //sop.Revision = $"{revisionPrefix}{currentRev + 1}";
                sop.EffectiveDate = effectiveDate;
                sop.LastReviewDate = DateTime.Now;
                sop.UploadDate = DateTime.Now;
                sop.Status = "Approved";
                sop.DocumentType = documentType;

                _context.Update(sop);
                await _context.SaveChangesAsync();

                return (true, "Revision completed successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Revision failed: {ex.Message}");
            }
        }

        private void ArchiveFile(string path, string archiveFolder)
        {
            if (File.Exists(path))
            {
                string fileName = Path.GetFileName(path);
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string archivedFile = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";
                string archivePath = Path.Combine(archiveFolder, archivedFile);

                File.Copy(path, archivePath, overwrite: true);
            }
        }
    }
}
