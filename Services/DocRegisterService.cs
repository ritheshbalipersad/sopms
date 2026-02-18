using DinkToPdf;
using DinkToPdf.Contracts;
using DocumentFormat.OpenXml.Drawing.Charts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SOPMSApp.Data;
using SOPMSApp.Models;
using System.IO;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using static NPOI.SS.Formula.PTG.ArrayPtg;

namespace SOPMSApp.Services
{
    public class DocRegisterService
    {
        private readonly IConverter _pdfConverter;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DocRegisterService> _logger;

        public const string UploadFolder = "uploads";
        public const string OriginalsFolder = "originals";
        public const string ArchiveFolder = "archives";
        public const string StatusPendingApproval = "Pending Approval";
        public const string StatusArchived = "Archived";

        public DocRegisterService(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<DocRegisterService> logger, IConverter pdfConverter)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _pdfConverter = pdfConverter ?? throw new ArgumentNullException(nameof(pdfConverter));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ✅ Check for existing active document
        public async Task<(bool Exists, string Message)> CheckForConflictAsync(string sopNumber, string originalFile)
        {
            if (string.IsNullOrWhiteSpace(sopNumber))
                throw new ArgumentException("SOP number cannot be empty", nameof(sopNumber));

            if (string.IsNullOrWhiteSpace(originalFile))
                throw new ArgumentException("Original filename cannot be empty", nameof(originalFile));

            var normalizedFile = originalFile.Trim().ToLower();

            var existingDoc = await _context.DocRegisters
                .FirstOrDefaultAsync(d => d.SopNumber == sopNumber &&
                                          d.OriginalFile.ToLower().Trim() == normalizedFile &&
                                          d.IsArchived == false);

            if (existingDoc == null)
                return (false, string.Empty);

            var msg = existingDoc.Status == StatusPendingApproval
                ? $"A pending approval document with SOP Number '{sopNumber}' and filename '{originalFile}' already exists."
                : $"A document with SOP Number '{sopNumber}' and filename '{originalFile}' already exists (Status: {existingDoc.Status}).";

            return (true, msg + " Do you want to override it?");
        }


        public async Task SyncStructuredSopAsync(StructuredSop sop, string loggedInUser, string email, string pdfFileName, string basePath)
        {
            if (sop == null)
                throw new ArgumentNullException(nameof(sop));

            try
            {
                // ============================================================
                // 1️⃣ CHECK EXISTING DOCREGISTER FOR THIS STRUCTURED SOP
                // ============================================================
                var existing = await _context.DocRegisters
                    .FirstOrDefaultAsync(d =>
                        d.SopNumber == sop.SopNumber &&
                        d.IsStructured == true &&
                        (d.IsArchived == null || d.IsArchived == false));

                // ============================================================
                // 2️⃣ ARCHIVE IF REVISION CHANGED
                // ============================================================
                if (existing != null && existing.Revision != sop.Revision)
                {
                    await ArchiveExistingDocRegisterAsync(existing, loggedInUser);
                    existing = null;
                }

                // ============================================================
                // 3️⃣ CREATE OR UPDATE DOCREGISTER RECORD
                // ============================================================
                var docRegister = existing ?? new DocRegister
                {
                    SopNumber = sop.SopNumber,
                    uniqueNumber = Guid.NewGuid().ToString(),
                    IsStructured = true,
                    StructuredSopId = sop.Id,
                    ContentType = "application/pdf"
                };

                // ============================================================
                // 4️⃣ UPDATE SHARED METADATA (ALWAYS UPDATE)
                // ============================================================
                docRegister.OriginalFile = sop.Title;
                docRegister.Revision = sop.Revision;
                docRegister.Department = sop.ControlledBy;
                docRegister.DocType = sop.DocType;
                docRegister.Area = sop.Area;
                docRegister.Author = loggedInUser;
                docRegister.UserEmail = email;
                docRegister.DepartmentSupervisor = sop.DepartmentSupervisor;
                docRegister.SupervisorEmail = sop.SupervisorEmail;
                docRegister.Status = "Pending Approval";
                docRegister.ReviewedBy = "Pending";
                docRegister.LastReviewDate = DateTime.Now;
                docRegister.EffectiveDate = sop.EffectiveDate;
                docRegister.IsStructured = true;
                docRegister.StructuredSopId = sop.Id;

                // ============================================================
                // 5️⃣ STORE FILE INFORMATION
                // ============================================================
                docRegister.FileName = $"{sop.SopNumber}_{sop.Title}.pdf";
                docRegister.OriginalFile = $"{sop.SopNumber}_{sop.Title}.pdf";

                var pdfPath = Path.Combine(basePath, "Originals", sop.DocType ?? "General", pdfFileName);

                if (File.Exists(pdfPath))
                    docRegister.FileSize = new FileInfo(pdfPath).Length;

                // ============================================================
                // 6️⃣ INSERT OR UPDATE DOCREGISTER
                // ============================================================
                if (existing == null)
                    await _context.DocRegisters.AddAsync(docRegister);
                else
                    _context.DocRegisters.Update(docRegister);

                await _context.SaveChangesAsync();

                // ============================================================
                // 7️⃣ CALL EMAIL PROCEDURE (NON-BLOCKING)
                // ============================================================
                try
                {
                    var parameters = new[]
                    {
                        new SqlParameter("@SopNumber", docRegister.SopNumber ?? (object)DBNull.Value),
                        new SqlParameter("@Department", docRegister.Department ?? (object)DBNull.Value),
                        new SqlParameter("@FileName", docRegister.OriginalFile ?? (object)DBNull.Value),
                        new SqlParameter("@Author", docRegister.Author ?? (object)DBNull.Value),
                        new SqlParameter("@EffectiveDate", docRegister.EffectiveDate ?? (object)DBNull.Value),
                        new SqlParameter("@SupervisorEmail", docRegister.SupervisorEmail ?? (object)DBNull.Value),
                        new SqlParameter("@Status", docRegister.Status ?? (object)DBNull.Value)
                    };

                    await _context.Database.ExecuteSqlRawAsync(
                        "EXEC dbo.sp_InsertPendingSOPEmail @SopNumber, @Department, @FileName, @Author, @EffectiveDate, @SupervisorEmail, @Status",
                        parameters
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to insert pending SOP email for SOP {SopNumber}", docRegister.SopNumber);
                }

                // ============================================================
                // 8️⃣ UPDATE STRUCTURED SOP LINK
                // ============================================================
                sop.DocRegisterId = docRegister.Id;
                sop.IsSyncedToDocRegister = true;
                sop.SyncedDate = DateTime.Now;

                _context.StructuredSops.Update(sop);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Structured SOP synced to DocRegister successfully: {SopNumber}", sop.SopNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Structured SOP {SopNumber} with DocRegister", sop.SopNumber);
                throw;
            }
        }

        private async Task ArchiveExistingDocRegisterAsync(DocRegister existing, string archivedBy)
        {
            existing.IsArchived = true;
            existing.ReviewedBy = $"Archived by {archivedBy}";
            existing.ArchivedOn = DateTime.Now;
            _context.DocRegisters.Update(existing);
            await _context.SaveChangesAsync();
        }

      


        // ✅ Main archive + save method
        public async Task ArchiveAndSaveAsync(DocRegister newDoc, bool overrideConfirmed, string archivedBy = "System", IDbContextTransaction? externalTransaction = null)
        {
            if (newDoc == null)
                throw new ArgumentNullException(nameof(newDoc));

            // Use the external transaction if provided
            var transaction = externalTransaction;

            bool createdInternalTransaction = false;
            if (transaction == null)
            {
                transaction = await _context.Database.BeginTransactionAsync();
                createdInternalTransaction = true;
            }

            try
            {
                var activeDocs = await _context.DocRegisters
                    .Where(d => d.SopNumber == newDoc.SopNumber && (d.IsArchived == false || d.IsArchived == null))
                    .ToListAsync();

                if (activeDocs.Any())
                {
                    if (!overrideConfirmed)
                        throw new InvalidOperationException($"Active document(s) exist for SOP {newDoc.SopNumber}, override not confirmed.");

                    foreach (var existing in activeDocs)
                        await ArchiveExistingDocumentAsync(existing, archivedBy);
                }

                // 🔁 Auto-increment revision number
                newDoc.Revision = await GetNextRevisionAsync(newDoc.SopNumber);

                // 🆕 Save new document
                newDoc.IsArchived = false;
                newDoc.Status = StatusPendingApproval;
                newDoc.UploadDate = DateTime.UtcNow;

                await _context.DocRegisters.AddAsync(newDoc);
                await _context.SaveChangesAsync();

                if (createdInternalTransaction)
                    await transaction.CommitAsync();

                _logger.LogInformation("Archived {Count} old docs and saved new one for SOP {SopNumber}",
                    activeDocs.Count, newDoc.SopNumber);
            }
            catch (Exception ex)
            {
                if (createdInternalTransaction)
                    await transaction.RollbackAsync();

                _logger.LogError(ex, "ArchiveAndSaveAsync failed for SOP {SopNumber}", newDoc?.SopNumber);
                throw new InvalidOperationException($"Archiving and saving failed: {ex.Message}", ex);
            }
        }

        // ✅ Archive individual document + copy to DocArchive
        private async Task ArchiveExistingDocumentAsync(DocRegister existing, string archivedBy)
        {
            existing.IsArchived = true;
            existing.Status = StatusArchived;
            existing.ArchivedOn = DateTime.UtcNow;
            _context.DocRegisters.Update(existing);

            var archiveEntry = new DocArchive
            {
                SopNumber = existing.SopNumber,
                Title = existing.OriginalFile,
                Revision = existing.Revision,
                FileName = existing.FileName,
                ContentType = existing.ContentType,
                Department = existing.Department,
                Author = existing.Author,
                UserEmail = existing.UserEmail,
                DocType = existing.DocType,
                Area = existing.Area,
                EffectiveDate = existing.EffectiveDate,
                ArchivedBy = archivedBy,
                ArchivedOn = DateTime.UtcNow,
                SourceTable = "DocRegisters",
                SourceId = existing.Id,
                Notes = "Automatically archived on override"
            };

            await _context.DocArchives.AddAsync(archiveEntry);
            await _context.SaveChangesAsync();

            // Move files safely to archive folder
            await MoveFilesToArchiveAsync(existing);
        }

        public async Task TrackRevisionHistoryAsync(DocRegister oldDoc, string revisedBy, string changes)
        {
            var history = new DocRegisterHistory
            {
                DocRegisterId = oldDoc.Id,
                SopNumber = oldDoc.SopNumber,
                OriginalFile = oldDoc.OriginalFile,
                FileName = oldDoc.FileName,
                Department = oldDoc.Department,
                Revision = oldDoc.Revision,
                EffectiveDate = oldDoc.EffectiveDate,
                LastReviewDate = oldDoc.LastReviewDate,
                UploadDate = oldDoc.UploadDate,
                Status = oldDoc.Status,
                DocumentType = oldDoc.DocType,
                RevisedBy = revisedBy,
                ChangeDescription = changes,
                RevisedOn = DateTime.Now
            };

            _context.DocRegisterHistories.Add(history);
            await _context.SaveChangesAsync();
        }


        // ✅ Move physical files into unique archive folder per SOP/revision
        private async Task MoveFilesToArchiveAsync(DocRegister document)
        {
            try
            {
                var subFolder = $"{document.SopNumber}_Rev{document.Revision?.Replace("Rev:", "") ?? "01"}";
                var archiveDir = Path.Combine(_environment.WebRootPath, ArchiveFolder, subFolder);
                Directory.CreateDirectory(archiveDir);

                var tasks = new List<Task>
                {
                    MoveFileToArchiveAsync(UploadFolder, document.FileName, archiveDir),
                    MoveFileToArchiveAsync(OriginalsFolder, document.OriginalFile, archiveDir)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move files to archive for SOP {SopNumber}", document.SopNumber);
                throw new InvalidOperationException("Failed to archive files", ex);
            }
        }

        private async Task MoveFileToArchiveAsync(string sourceFolder, string fileName, string archiveDir)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            var sourcePath = Path.Combine(_environment.WebRootPath, sourceFolder, fileName);
            var targetPath = Path.Combine(archiveDir, fileName);

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning("File not found for archiving: {FilePath}", sourcePath);
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                // If same filename exists in target, append timestamp
                if (File.Exists(targetPath))
                {
                    var ext = Path.GetExtension(fileName);
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var uniqueName = $"{name}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
                    targetPath = Path.Combine(archiveDir, uniqueName);
                }

                await Task.Run(() => File.Move(sourcePath, targetPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move file {FileName} to archive", fileName);
                throw;
            }
        }

        // ✅ Revision generator
        private async Task<string> GetNextRevisionAsync(string sopNumber)
        {
            var lastRevision = await _context.DocRegisters
                .Where(d => d.SopNumber == sopNumber)
                .OrderByDescending(d => d.ArchivedOn)
                .Select(d => d.Revision)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(lastRevision))
                return "Rev:01";

            var digits = new string(lastRevision.Where(char.IsDigit).ToArray());
            int.TryParse(digits, out int revNum);
            return $"Rev:{(revNum + 1):00}";
        }


        public async Task<bool> DeleteStructuredSopAsync(string sopNumber, string deletedBy, string reason)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(sopNumber))
                    throw new ArgumentNullException(nameof(sopNumber));

                sopNumber = sopNumber.Trim();

                // 1. Load Structured SOP with its steps
                var structured = await _context.StructuredSops
                    .Include(s => s.Steps)
                    .FirstOrDefaultAsync(s => s.SopNumber == sopNumber);

                if (structured == null)
                    throw new InvalidOperationException($"SOP {sopNumber} not found.");

                // 2. Try load DocRegister entry
                var doc = await _context.DocRegisters
                    .FirstOrDefaultAsync(x => x.SopNumber == sopNumber);

                // 3. Archive files (optional)
                void ArchiveFile(string path)
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        return;

                    try
                    {
                        string archiveDir = Path.Combine("D:\\SOPMS_Documents\\Archive", DateTime.Now.ToString("yyyyMMdd"));
                        Directory.CreateDirectory(archiveDir);
                        string dest = Path.Combine(archiveDir, Path.GetFileName(path));
                        File.Copy(path, dest, true);
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, $"Failed to archive file: {path}");
                    }
                }

                // Archive files
                if (!string.IsNullOrWhiteSpace(doc?.VideoPath))
                    ArchiveFile(doc.VideoPath);

                foreach (var step in structured.Steps)
                {
                    if (!string.IsNullOrWhiteSpace(step.ImagePath))
                    {
                        foreach (var img in step.ImagePath.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            ArchiveFile(img);
                    }

                    if (!string.IsNullOrWhiteSpace(step.KeyPointImagePath))
                        ArchiveFile(step.KeyPointImagePath);
                }

                // 4. Create deletion log WITHOUT OriginalDocRegisterId
                var log = new DeletedFileLog
                {
                    SOPNumber = structured.SopNumber,
                    FileName = $"{structured.SopNumber}_{structured.Title}.pdf",
                    OriginalFileName = $"{structured.SopNumber}_{structured.Title}.pdf",
                    DeletedBy = deletedBy,
                    DeletedOn = DateTime.Now,
                    Reason = reason,
                    UserEmail = structured.UserEmail,
                    DocType = structured.DocType,
                    Department = structured.DepartmentSupervisor,
                    Area = structured.Area,
                    Revision = structured.Revision,
                    ContentType = doc?.ContentType,
                    FileSize = doc?.FileSize ?? 0,
                    Author = !string.IsNullOrWhiteSpace(doc?.Author) ? doc.Author :
                            (!string.IsNullOrWhiteSpace(structured?.Signatures) ? structured.Signatures : "Structured SOP"),
                    DepartmentSupervisor = structured.DepartmentSupervisor,
                    SupervisorEmail = structured.SupervisorEmail,
                    Status = structured.Status,
                    EffectiveDate = structured.EffectiveDate,
                    UploadDate = doc?.UploadDate,
                    WasApproved = structured.AdminApproved,
                    ArchivedOn = DateTime.Now
                    // DO NOT SET OriginalDocRegisterId here!
                };

                _context.DeletedFileLogs.Add(log);

                // Save the log FIRST
                await _context.SaveChangesAsync();

                // 5. FIRST: Remove foreign key reference from DocRegister to StructuredSop
                if (doc != null)
                {
                    // Set StructuredSopId to null to break the FK constraint
                    doc.StructuredSopId = null;
                    _context.DocRegisters.Update(doc);
                }

                // 6. Save changes after breaking the FK
                await _context.SaveChangesAsync();

                // 7. SECOND: Delete Structured SOP steps
                if (structured.Steps != null && structured.Steps.Any())
                {
                    _context.SopSteps.RemoveRange(structured.Steps);
                }

                // 8. THIRD: Delete Structured SOP 
                _context.StructuredSops.Remove(structured);

                // 9. FOURTH: Delete DocRegister 
                if (doc != null)
                {
                    _context.DocRegisters.Remove(doc);
                }

                // 10. Save all remaining changes
                await _context.SaveChangesAsync();

                // 11. Commit transaction
                await transaction.CommitAsync();

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger?.LogError(ex, $"Error deleting Structured SOP {sopNumber}");
                throw;
            }
        }
    }
}
