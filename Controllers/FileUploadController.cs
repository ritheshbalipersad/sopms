using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;
using SOPMSApp.Models.ViewModels;
using SOPMSApp.Services;
using System.Text;
using static SOPMSApp.Models.ViewModels.VideoUploadViewModel;


namespace SOPMSApp.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly DocFileService _fileService;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly entTTSAPDbContext _entTTSAPDbContext;
        private readonly ILogger<FileUploadController> _logger;
        private readonly DocRegisterService _docRegisterService;
        private readonly IDocumentAuditLogService _auditLog;
        private readonly string _storageRoot;


        public FileUploadController( ApplicationDbContext context, IWebHostEnvironment env, IConfiguration configuration, DocRegisterService docRegisterService, ILogger<FileUploadController> logger, DocFileService fileService, entTTSAPDbContext entTTSAPDbContext, IDocumentAuditLogService auditLog )
        {
            _env = env;
            _logger = logger;
            _context = context;
            _fileService = fileService;
            _configuration = configuration;
            _entTTSAPDbContext = entTTSAPDbContext;
            _docRegisterService = docRegisterService;
            _auditLog = auditLog;
            _storageRoot = configuration["StorageSettings:BasePath"];
        }


        ///Returns a view

        public async Task<IActionResult> Index()
        {
            
            var areas = await GetDistinctAreasAsync();
            var departments = await GetDepartmentsAsync();
            var documents = await GetDistinctDocumentsAsync();

            ViewData["Areas"] = areas;
            ViewData["Documents"] = documents;
            ViewData["Department"] = departments;
          
            ViewBag.UserDepartment = User.FindFirst("DepartmentID")?.Value;
            ViewBag.DocumentTypes = new SelectList(documents);
  
            return View();
        }

       
        [HttpGet]
        public IActionResult FileBulkUpload()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetSopNumber(string docType)
        {
            try
            {
                Console.WriteLine($"GetSopNumber called with docType: {docType}");

                if (string.IsNullOrWhiteSpace(docType))
                {
                    Console.WriteLine("docType is empty");
                    return Json(new { sopNumber = "" });
                }

                string sopNumber = await GenerateSopNumberAsync(docType);
                Console.WriteLine($"Generated SOP Number: {sopNumber}");

                return Json(new { sopNumber });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetSopNumber: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Return error in JSON format
                return Json(new
                {
                    sopNumber = "",
                    error = ex.Message
                });
            }
        }

        [HttpGet]
        private bool IsFileSelected(IFormFile file)
        {
            return file != null && file.Length > 0;
        }



        // POST:FileUpload/UploadFile/ UPLOAD FILE FUNCTION
        [HttpPost]
        public async Task<IActionResult> UploadFile( IFormFile OriginalFile, IFormFile? PdfFile, IFormFile? VideoFile, string sopNumber, string author,  string department, DateTime? lastReviewDate, string docType, DateTime? effectiveDate, string documentType, string[] Area, string revision)
        {
            try
            {
                if (OriginalFile == null)
                {
                    TempData["Error"] = "Original file is required.";
                    return RedirectToAction("Index");
                }

                // --- Auto-generate SOP number if not provided ---
                if (string.IsNullOrWhiteSpace(sopNumber))
                {
                    string generatedSopNumber = await GenerateSopNumberAsync(docType);
                    if (string.IsNullOrWhiteSpace(generatedSopNumber))
                    {
                        TempData["Error"] = "Unable to generate SOP Number. Please check the Document Type acronym.";
                        return RedirectToAction("Index");
                    }
                    sopNumber = generatedSopNumber;
                }
                else
                {
                    // If SOP number was provided, ensure it follows the expected format
                    sopNumber = sopNumber.Trim().ToUpper();
                }

                // Validate DocType from your external source
                var validDocTypes = await GetDistinctDocumentsAsync();
                string matchedDocType = validDocTypes.FirstOrDefault(d => d.Equals(docType, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(matchedDocType))
                {
                    TempData["Error"] = $"Invalid Document Type: {docType}";
                    return RedirectToAction("Index");
                }

                // --- validate original file types ---
                var allowedOriginalExts = new[]
                {
                    ".doc",   // Word 97-2003 Document
                    ".docx",  // Word Document (modern)
                    ".dot",   // Word 97-2003 Template
                    ".dotx",  // Word Template (modern)
                    ".xls",   // Excel 97-2003 Workbook
                    ".xlsx",  // Excel Workbook (modern)
                    ".xlt",   // Excel 97-2003 Template
                    ".xltx",  // Excel Template (modern)
                    ".ppt",   // PowerPoint 97-2003 Presentation
                    ".pptx",  // PowerPoint Presentation (modern)
                    ".pot",   // PowerPoint 97-2003 Template
                    ".potx",  // PowerPoint Template (modern)
                    ".pdf"    // PDF
                };

                var originalExt = Path.GetExtension(OriginalFile.FileName).ToLower();
                var pdfExt = PdfFile != null ? Path.GetExtension(PdfFile.FileName).ToLower() : null;
                //string departmentSupervisor = await GetSupervisorName(department);
                //string supervisorEmail = "N/A";

                string departmentSupervisor = await GetSupervisorName(department);
                string supervisorEmail = await _entTTSAPDbContext.Labor
                    .Where(l => l.LaborName == departmentSupervisor)
                    .Select(l => l.Email)
                    .FirstOrDefaultAsync() ?? "N/A";

                // AUTO SOP NUMBER GENERATION
                if (string.IsNullOrWhiteSpace(sopNumber))
                {
                    sopNumber = await GenerateSopNumberAsync(docType);
                }
                else
                {
                    // Normalize
                    sopNumber = sopNumber.Replace(" ", "").Trim();
                }


                if (!allowedOriginalExts.Contains(originalExt) || (PdfFile != null && pdfExt != ".pdf"))
                {
                    TempData["Error"] = "Only Word/Excel/PDF as original are allowed.";
                    return RedirectToAction("Index");
                }

                // --- video validation if present ---
                if (VideoFile != null)
                {
                    var vExt = Path.GetExtension(VideoFile.FileName).ToLower();
                    var allowedVideoExts = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" };
                    if (!allowedVideoExts.Contains(vExt) || VideoFile.Length == 0 || VideoFile.Length > 2147483648)
                    {
                        TempData["Error"] = "Invalid video file.";
                        return RedirectToAction("Index");
                    }
                }


                // Ensure revision format
                if (!revision.StartsWith("Rev: ", StringComparison.OrdinalIgnoreCase))
                    revision = $"Rev: {revision}";


                string originalFileName = Path.GetFileName(OriginalFile.FileName);
                string? pdfFileName = PdfFile != null ? Path.GetFileName(PdfFile.FileName) : null;
                string? videoFileName = VideoFile != null ? Path.GetFileName(VideoFile.FileName) : null;

                //Duplicate check: If SOP number already exists, check if it's the same document type
                var existingSop = await _context.DocRegisters
                    .FirstOrDefaultAsync(d => d.SopNumber == sopNumber && d.IsArchived != true);

                if (existingSop != null)
                {
                    // Option 1: Show error and stop
                    TempData["Error"] = $"A document with SOP Number '{sopNumber}' already exists. Please use a different SOP number.";
                    return RedirectToAction("Index");

                }



                // --- Prepare storage paths ---
                // D:\SOPMS_Documents 
                string originalsBasePath = Path.Combine(_storageRoot, "Originals", matchedDocType);
                string pdfBasePath = Path.Combine(_storageRoot, "PDFs", matchedDocType);
                string videosBasePath = Path.Combine(_storageRoot, "Videos", matchedDocType);

                Directory.CreateDirectory(originalsBasePath);
                Directory.CreateDirectory(pdfBasePath);
                Directory.CreateDirectory(videosBasePath);

                // Compose safe filenames (timestamped to avoid collisions)
                string storedOriginalName = originalFileName;
                string originalPath = Path.Combine(originalsBasePath, storedOriginalName);
                string? storedPdfName = pdfFileName;
                string? pdfPath = PdfFile != null ? Path.Combine(pdfBasePath, storedPdfName!) : null;
                string? storedVideoName = videoFileName;
                string? videoPath = VideoFile != null ? Path.Combine(videosBasePath, storedVideoName!) : null;
               
                // --- Save files ---
                await SaveFileToDiskAsync(OriginalFile, originalPath);
                if (PdfFile != null && pdfPath != null) await SaveFileToDiskAsync(PdfFile, pdfPath);
                if (VideoFile != null && videoPath != null) await SaveFileToDiskAsync(VideoFile, videoPath);


                // Create or update DocRegister record

                DocRegister docRecord = existingSop ?? new DocRegister
                {
                    SopNumber = sopNumber.Replace(" ", ""),
                    FileName = pdfFileName ?? "N/A",
                    OriginalFile = originalFileName,
                    uniqueNumber = Guid.NewGuid().ToString(),
                    ContentType = PdfFile != null ? GetMimeType(".pdf") : GetContentType(Path.GetExtension(OriginalFile.FileName)),
                    LastReviewDate = lastReviewDate ?? DateTime.Now,
                    UploadDate = DateTime.Now,
                    FileSize = OriginalFile.Length + (PdfFile?.Length ?? 0) + (VideoFile?.Length ?? 0),
                    Author = User.FindFirst("LaborName")?.Value ?? "Unknown User",
                    UserEmail = User.FindFirst("Email")?.Value ?? "N/A",
                    DepartmentSupervisor = departmentSupervisor,
                    SupervisorEmail = supervisorEmail,
                    Department = department,
                    DocType = matchedDocType,
                    Area = Area != null ? string.Join(", ", Area) : null,
                    EffectiveDate = effectiveDate,
                    DocumentType = documentType,
                    Revision = revision,
                    Status = "Pending Approval",
                    ReviewedBy = "Pending",
                    IsArchived = false,
                    DocumentPath = null,
                    VideoPath = null,
                    Changed = null,
                    IsStructured = false,
                   

                };

                if (existingSop == null)
                    await _context.DocRegisters.AddAsync(docRecord);
                else
                {
                    docRecord.FileSize = OriginalFile.Length + (PdfFile?.Length ?? 0) + (VideoFile?.Length ?? 0);
                    docRecord.Status = "Pending Approval";
                    docRecord.ReviewedBy = "Pending";
                    docRecord.IsStructured = false;

                    _context.DocRegisters.Update(docRecord);
                }

                // Save DB first so we have an Id (if new)
                await _context.SaveChangesAsync();

                var performedBy = User.FindFirst("LaborName")?.Value ?? User.Identity?.Name ?? "System";
                await _auditLog.LogAsync(docRecord.Id, docRecord.SopNumber ?? "", "Uploaded", performedBy, null, docRecord.OriginalFile);

                // --- Call procedure to insert email log ---
                if (!string.IsNullOrEmpty(docRecord.SupervisorEmail) && docRecord.SupervisorEmail != "N/A")
                {
                    try
                    {
                        var parameters = new[]
                        {
                            new SqlParameter("@SopNumber", docRecord.SopNumber),
                            new SqlParameter("@Department", docRecord.Department ?? (object)DBNull.Value),
                            new SqlParameter("@FileName", docRecord.OriginalFile ?? (object)DBNull.Value),
                            new SqlParameter("@Author", docRecord.Author ?? (object)DBNull.Value),
                            new SqlParameter("@EffectiveDate", docRecord.EffectiveDate ?? (object)DBNull.Value),
                            new SqlParameter("@SupervisorEmail", docRecord.SupervisorEmail),
                            new SqlParameter("@Status", docRecord.Status ?? (object)DBNull.Value)
                        };

                        await _context.Database.ExecuteSqlRawAsync(
                            "EXEC dbo.sp_InsertPendingSOPEmail @SopNumber, @Department, @FileName, @Author, @EffectiveDate, @SupervisorEmail, @Status",
                            parameters
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to insert pending SOP email for SOP {SopNumber}", docRecord.SopNumber);
                    }
                }

                // Save files to disk (atomic-ish approach: if file save fails, attempt to rollback DB changes)
                /*try
                 {
                     await SaveFileToDiskAsync(OriginalFile, originalPath);

                     if (PdfFile != null && pdfPath != null)
                     {
                         await SaveFileToDiskAsync(PdfFile, pdfPath);
                         docRecord.DocumentPath = Path.Combine("PDFs", matchedDocType, storedPdfName!).Replace("\\", "/");
                     }
                     else
                     {
                         // set DocumentPath pointing to Originals folder so preview can use it
                         docRecord.DocumentPath = Path.Combine("Originals", matchedDocType, storedOriginalName).Replace("\\", "/");
                     }

                     if (VideoFile != null && videoPath != null)
                     {
                         await SaveFileToDiskAsync(VideoFile, videoPath);
                         docRecord.VideoPath = Path.Combine("Videos", matchedDocType, storedVideoName!).Replace("\\", "/");
                     }
                 }
                 catch (Exception fileEx)
                 {
                     // Rollback: remove created DB entry if it's newly created and files failed
                     if (existingSop == null)
                     {
                         _context.DocRegisters.Remove(docRecord);
                         await _context.SaveChangesAsync();
                     }
                     TempData["Error"] = $"File storage failed: {fileEx.Message}";
                     _logger.LogError(fileEx, "File storage failed for SOP: {SopNumber}", sopNumber);
                     return RedirectToAction("Index");
                 }*/

                // Update doc record with paths and file size (and save)
                /*docRecord.FileSize = (OriginalFile.Length + (PdfFile?.Length ?? 0) + (VideoFile?.Length ?? 0));
                await _context.SaveChangesAsync();*/

               
                TempData["Success"] = "Document uploaded successfully!";
                TempData["Warning"] = "Awaiting for Approval!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Upload failed: {ex.Message}";
                _logger.LogError(ex, $"Document upload failed for SOP: {sopNumber}.");
            }

            return RedirectToAction("Index");
        }

        private string GetContentType(string extension)
        {
            return extension.ToLower() switch
            {
                // PDF
                ".pdf" => "application/pdf",

                // Word
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".dot" => "application/msword",
                ".dotx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.template",

                
                ".xls" => "application/vnd.ms-excel",
                ".xlt" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xltx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.template",

                // PowerPoint
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".pot" => "application/vnd.ms-powerpoint",
                ".potx" => "application/vnd.openxmlformats-officedocument.presentationml.template",

                // Default fallback
                _ => "application/octet-stream"
            };
        }

        private async Task<string> GenerateSopNumberAsync(string docType)
        {
            if (string.IsNullOrEmpty(docType))
                return "N/A";

            // Get acronym from your database 
            string acronym = await GetSopAcronymAsync(docType);
            if (string.IsNullOrWhiteSpace(acronym))
                return null;

            // Fetch only SOP Numbers starting with the acronym from entTTSAP
            var existingNumbers = await _context.DocRegisters
                .Where(d => d.SopNumber.StartsWith(acronym))
                .Select(d => d.SopNumber)
                .ToListAsync();

            int maxSuffix = 0;

            foreach (var sop in existingNumbers)
            {
                // Extract the numeric suffix after the acronym
                string suffix = sop.Substring(acronym.Length);
                if (int.TryParse(suffix, out int number) && number > maxSuffix)
                    maxSuffix = number;
            }

            // Generate new number with a 3-digit suffix (e.g., WI001, WI002, ...)
            return $"{acronym}{(maxSuffix + 1):D3}";
        }

        
        private async Task<string> GetSopAcronymAsync(string docType)
        {
            string connStr = _configuration.GetConnectionString("entTTSAPConnection");
            try
            {
                using var conn = new SqlConnection(connStr);
                const string sql = "SELECT UDFChar1 FROM Bulletin WHERE BulletinName = @docType";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@docType", docType);

                await conn.OpenAsync();
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SOP acronym for DocType: {DocType}", docType);
                return string.Empty;
            }
        }

        [HttpPost]
        public async Task<JsonResult> CheckDuplicate(string sopNumber, string originalFile, string revision, string department)
        {
            try
            {
                // Input validation
                if (string.IsNullOrWhiteSpace(sopNumber) || string.IsNullOrWhiteSpace(originalFile))
                {
                    return Json(new
                    {
                        isDuplicate = false,
                        message = "SOP number and file name are required for duplicate check"
                    });
                }

                // Normalize inputs
                sopNumber = sopNumber?.Trim().ToLower() ?? string.Empty;
                originalFile = originalFile?.Trim().ToLower() ?? string.Empty;
                revision = revision?.Trim() ?? string.Empty;
                department = department?.Trim() ?? string.Empty;

                // Build query based on available parameters
                var query = _context.DocRegisters
                    .Where(d => d.IsArchived == false &&
                               d.SopNumber.ToLower() == sopNumber &&
                               d.OriginalFile.ToLower() == originalFile);

                // Add revision check if provided
                if (!string.IsNullOrEmpty(revision))
                {
                    query = query.Where(d => d.Revision == revision);
                }

                // Add department check if provided
                if (!string.IsNullOrEmpty(department))
                {
                    query = query.Where(d => d.Department == department);
                }

                var existingDoc = await query.FirstOrDefaultAsync();

                if (existingDoc != null)
                {
                    string message;

                    if (!string.IsNullOrEmpty(revision) && !string.IsNullOrEmpty(department))
                    {
                        message = $"A document with SOP Number '{sopNumber.ToUpper()}', " +
                                 $"File '{originalFile}', " +
                                 $"Revision '{revision}', " +
                                 $"Department '{department}' already exists.";
                    }
                    else if (!string.IsNullOrEmpty(revision))
                    {
                        message = $"A document with SOP Number '{sopNumber.ToUpper()}', " +
                                 $"File '{originalFile}', " +
                                 $"Revision '{revision}' already exists.";
                    }
                    else if (!string.IsNullOrEmpty(department))
                    {
                        message = $"A document with SOP Number '{sopNumber.ToUpper()}', " +
                                 $"File '{originalFile}', " +
                                 $"Department '{department}' already exists.";
                    }
                    else
                    {
                        message = $"A document with SOP Number '{sopNumber.ToUpper()}', " +
                                 $"File '{originalFile}' already exists.";
                    }

                    return Json(new
                    {
                        isDuplicate = true,
                        message = message
                    });
                }

                return Json(new { isDuplicate = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for duplicate document");
                return Json(new
                {
                    isDuplicate = false,
                    message = "An error occurred while checking for duplicates. Please try again."
                });
            }
        }

        private async Task<List<string>> GetDistinctAreasAsync()
        {
            // Prefer Areas from DocRet (maintained in Admin) so upload page shows all areas in the db
            try
            {
                var fromDb = await _context.Areas
                    .OrderBy(a => a.AreaName)
                    .Select(a => a.AreaName)
                    .ToListAsync();
                if (fromDb.Count > 0)
                    return fromDb;
            }
            catch { /* Areas table may not exist in some setups */ }

            // Fallback: entTTSAP.asset (legacy)
            var areas = new List<string>();
            string connStr = _configuration.GetConnectionString("entTTSAPConnection");
            if (string.IsNullOrEmpty(connStr)) return areas;
            try
            {
                using (var conn = new SqlConnection(connStr))
                {
                    string sql = "SELECT assetname AS AreaName FROM asset WHERE udfbit5 = 1 AND isup = 1 ORDER BY assetname";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        await conn.OpenAsync();
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var areaName = reader["AreaName"]?.ToString();
                                if (!string.IsNullOrEmpty(areaName))
                                    areas.Add(areaName);
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
            return areas;
        }

        private async Task<List<string>> GetDistinctDocumentsAsync()
        {
            var areas = new List<string>();
            string connStr = _configuration.GetConnectionString("entTTSAPConnection");

            try
            {
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    string sql = "SELECT BulletinName AS DocumentType FROM Bulletin ";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        await conn.OpenAsync();
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string documentDtype = reader["DocumentType"]?.ToString();
                                if (!string.IsNullOrEmpty(documentDtype))
                                {
                                    areas.Add(documentDtype);
                                }
                            }
                        }
                    }
                }
                return areas;
            }
            catch (Exception)
            {
                return new List<string>(); // Return empty list on error
            }
        }

        private async Task<List<DepartmentModel>> GetDepartmentsAsync()
        {
            var departments = new List<DepartmentModel>();
            string connStr = _configuration.GetConnectionString("entTTSAPConnection");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                string sql = "SELECT DepartmentID, DepartmentName, SupervisorName FROM Department where active = 1";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    await conn.OpenAsync();
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            departments.Add(new DepartmentModel
                            {
                                DepartmentID = reader["DepartmentID"].ToString(),
                                DepartmentName = reader["DepartmentName"]?.ToString(),
                                SupervisorName = reader["SupervisorName"]?.ToString()

                            });
                        }
                    }
                }
            }

            return departments;
        }

        private async Task<string> GetSupervisorName(string department)
        {
            try
            {
                if (string.IsNullOrEmpty(department))
                    return "N/A";

                var departments = await GetDepartmentsAsync();

                // Find department by name (case insensitive)
                var dept = departments.FirstOrDefault(d =>
                    d.DepartmentName?.Equals(department, StringComparison.OrdinalIgnoreCase) == true);

                if (dept != null && !string.IsNullOrEmpty(dept.SupervisorName))
                {
                    return dept.SupervisorName;
                }

                _logger.LogWarning("No supervisor Name found for department: {Department}", department);
                return "N/A";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supervisor Name for department: {Department}", department);
                return "N/A";
            }
        }

        private bool IsAllowedFileType(IFormFile file)
        {
            var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };
            var allowedMimeTypes = new[]
            {
                "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var mimeType = file.ContentType;

            return allowedExtensions.Contains(extension) && allowedMimeTypes.Contains(mimeType);
        }

        private bool SopNumberExists(string sopNumber)
        {
            return _context.DocRegisters.Any(d => d.SopNumber == sopNumber);
        }

        private bool FileNameExists(string originalFileName, string? fileName)
        {
            return _context.DocRegisters.Any(d => d.FileName == fileName && d.OriginalFile == originalFileName);
        }
        private (string originalsPath, string pdfsPath) EnsureUploadDirectoriesExist()
        {
            string originalsPath = Path.Combine(_env.WebRootPath, "Originals");
            string pdfsPath = Path.Combine(_env.WebRootPath, "upload");

            if (!Directory.Exists(originalsPath))
                Directory.CreateDirectory(originalsPath);

            if (!Directory.Exists(pdfsPath))
                Directory.CreateDirectory(pdfsPath);

            return (originalsPath, pdfsPath);
        }

        private async Task SaveFileToDiskAsync(IFormFile file, string path)
        {
            using var stream = new FileStream(path, FileMode.Create);
            await file.CopyToAsync(stream);
        }



        [HttpGet]
        public IActionResult StreamVideo(int id)
        {
            var doc = _context.DocRegisters.Find(id);
            if (doc == null || string.IsNullOrEmpty(doc.VideoPath))
                return NotFound();

            var filePath = Path.Combine(_storageRoot, doc.VideoPath);
            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var contentType = GetVideoContentType(doc.OriginalFile ?? doc.VideoPath);
            return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
        }

        [HttpGet]
        public async Task<IActionResult> UploadVideo()
        {
            var sops = await _context.DocRegisters
                .Where(d => d.IsArchived != true && d.Status == "Approved")
                .ToListAsync();

            var model = new VideoUploadViewModel
            {
                ExistingSops = sops.Select(d => new SelectListItem
                {
                    Value = d.SopNumber,
                    Text = $"{d.SopNumber} - {d.DocType} ({d.Department})"
                }).ToList(),

                Areas = await GetDistinctAreasAsync(),

                Departments = (await GetDepartmentsAsync())
                    .Select(d => new SelectListItem
                    {
                        Value = d.DepartmentName,
                        Text = d.DepartmentName
                    }).ToList(),

                Documents = (await GetDistinctDocumentsAsync())
                    .Select(doc => new SelectListItem
                    {
                        Value = doc,
                        Text = doc
                    }).ToList()
            };


            // Populate ExistingSopDetails AFTER initializer
            model.ExistingSopDetails = sops
                .GroupBy(d => d.SopNumber)                 
                .Select(g => g.OrderByDescending(x => x.Revision).First()) // pick latest revision
                .ToDictionary(
                    d => d.SopNumber,
                    d => new ExistingSopDetail
                    {
                        SopNumber = d.SopNumber,
                        Department = d.Department,
                        DocumentType = d.DocType,
                        Description = d.DocumentType,
                        Revision = d.Revision,
                        LastReviewDate = d.LastReviewDate,
                        EffectiveDate = d.EffectiveDate,
                        Areas = !string.IsNullOrEmpty(d.Area) ? d.Area.Split(", ").ToList() : new List<string>()
                    });


            ViewBag.UserDepartment = User.FindFirst("DepartmentID")?.Value;

            return View(model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(2147483648)] // 2 GB
        public async Task<IActionResult> UploadVideo( IFormFile videoFile, string SopNumber,string DocumentType, string Department,  string DocType, string Revision,  DateTime? EffectiveDate,DateTime? LastReviewDate,string[]? Area)
        {
            try
            {
                // 1️⃣ VALIDATION
                if (videoFile == null || videoFile.Length == 0)
                {
                    TempData["Error"] = "Please upload a video file.";
                    return RedirectToAction(nameof(UploadVideo));
                }

                if (videoFile.Length > 2147483648)
                {
                    TempData["Error"] = "File size exceeds the maximum limit (2 GB).";
                    return RedirectToAction(nameof(UploadVideo));
                }

                var ext = Path.GetExtension(videoFile.FileName).ToLowerInvariant();
                var allowedExts = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" };
                if (!allowedExts.Contains(ext))
                {
                    TempData["Error"] = "Unsupported video format. Please upload MP4, MOV, AVI, MKV, or WEBM files.";
                    return RedirectToAction(nameof(UploadVideo));
                }

                var mimeType = GetVideoContentType(videoFile.FileName);
                if (!mimeType.StartsWith("video/"))
                {
                    TempData["Error"] = "Invalid file type. Please upload a video file.";
                    return RedirectToAction(nameof(UploadVideo));
                }

                if (string.IsNullOrWhiteSpace(SopNumber))
                {
                    TempData["Error"] = "You must select an existing SOP to link the video.";
                    return RedirectToAction(nameof(UploadVideo));
                }

                SopNumber = SopNumber.Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(Revision) && !Revision.StartsWith("Rev:", StringComparison.OrdinalIgnoreCase))
                    Revision = $"Rev: {Revision}";

                // 2️⃣ LINK VIDEO TO EXISTING SOP
                var existingDoc = await _context.DocRegisters
                    .FirstOrDefaultAsync(d => d.SopNumber.ToUpper() == SopNumber && d.IsArchived != true);

                if (existingDoc == null)
                {
                    TempData["Error"] = "Selected SOP not found. Please choose a valid SOP.";
                    return RedirectToAction(nameof(UploadVideo));
                }

                // 3️⃣ FILE STORAGE — SAME STRUCTURE AS MAIN UPLOAD
                string videosBasePath = Path.Combine(_storageRoot, "Videos", DocType ?? "General");
                Directory.CreateDirectory(videosBasePath);

                string storedVideoName = Path.GetFileName(videoFile.FileName);
                string physicalPath = Path.Combine(videosBasePath, storedVideoName);

                if (System.IO.File.Exists(physicalPath))
                {
                    TempData["Error"] = $"A video file '{storedVideoName}' already exists. Please rename or remove it before uploading.";
                    return RedirectToAction(nameof(UploadVideo));
                }

                // Save file to disk
                await using (var stream = new FileStream(physicalPath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }

                // ✅ Relative path for DB and preview use
                string relativeVideoPath = Path.Combine("Videos", DocType ?? "General", storedVideoName).Replace("\\", "/");

                // 4️⃣ USER + DEPARTMENT INFO
                string loggedInUser = User.FindFirst("LaborName")?.Value ?? "Unknown User";
                string userEmail = User.FindFirst("Email")?.Value ?? "N/A";

                if (string.IsNullOrEmpty(Department))
                {
                    var departments = await GetDepartmentsAsync();
                    var userDeptId = User.FindFirst("DepartmentID")?.Value;
                    Department = departments.FirstOrDefault(d => d.DepartmentID == userDeptId)?.DepartmentName ?? userDeptId;
                }

                var supervisorInfo = await (
                    from d in _entTTSAPDbContext.Department
                    join l in _entTTSAPDbContext.Labor on d.SupervisorName equals l.LaborName into labors
                    from l in labors.DefaultIfEmpty()
                    where d.DepartmentName == Department
                    select new { l.LaborName, l.Email }
                ).FirstOrDefaultAsync();

                string departmentSupervisor = supervisorInfo?.LaborName ?? "N/A";
                string supervisorEmail = supervisorInfo?.Email ?? "N/A";

                // 5️⃣ UPDATE EXISTING SOP RECORD
                existingDoc.VideoPath = relativeVideoPath;
                existingDoc.ContentType = mimeType;
                existingDoc.FileSize =  videoFile.Length;
                existingDoc.Revision = Revision ?? existingDoc.Revision;
                existingDoc.LastReviewDate = LastReviewDate ?? existingDoc.LastReviewDate;
                existingDoc.EffectiveDate = EffectiveDate ?? existingDoc.EffectiveDate;
                existingDoc.Area = Area != null ? string.Join(", ", Area) : existingDoc.Area;
                existingDoc.Status = "Pending Approval";
                existingDoc.Author = loggedInUser;
                existingDoc.UserEmail = userEmail;
                existingDoc.DepartmentSupervisor = departmentSupervisor;
                existingDoc.SupervisorEmail = supervisorEmail;

                _context.DocRegisters.Update(existingDoc);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"✅ Video linked successfully to SOP {SopNumber}.";
                TempData["Warning"] = "Awaiting Approval.";
                return RedirectToAction(nameof(UploadVideo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading video for SOP: {SopNumber}", SopNumber);
                TempData["Error"] = "⚠️ An error occurred during upload. Please try again.";
                return RedirectToAction(nameof(UploadVideo));
            }
        }

        private string GetVideoContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".ogg" => "video/ogg",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".mkv" => "video/x-matroska",
                _ => "application/octet-stream"
            };
        }




        public async Task<IActionResult> BulkUpload()
        {
            var areas = await GetDistinctAreasAsync();
            var departments = await GetDepartmentsAsync();
            var documents = await GetDistinctDocumentsAsync();

            ViewData["Areas"] = areas;
            ViewData["Documents"] = documents;
            ViewData["Department"] = departments;

            ViewBag.UserDepartment = User.FindFirst("DepartmentID")?.Value;
            ViewBag.DocumentTypes = new SelectList(documents);

            return View();
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = 2147483648)]
        public async Task<IActionResult> UploadAll([FromForm] List<IFormFile> files, IFormFile excelFile)
        {
            const int batchSize = 50;
            var errors = new List<string>();
            var problematicLines = new List<int>();
            var successCount = 0;
            var failedCount = 0;
            var validDocuments = new List<DocRegister>();
            var skippedDocuments = new List<string>();
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Get selected DocType from form dropdown
            string selectedDocType = Request.Form["docType"].FirstOrDefault() ?? "General";

            // Validate input
            if ((files == null || !files.Any()) || excelFile == null || excelFile.Length == 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Please upload both Excel metadata and corresponding files.",
                    errors = errors
                });
            }

            // Validate file types
            var allowedExtensions = new[]
            {
                // Documents
                ".pdf",
                ".doc", ".docx",
                ".dot", ".dotx",

                // Excel
                ".xls", ".xlsx",
                ".xlt", ".xltx",

                // PowerPoint
                ".ppt", ".pptx",
                ".pot", ".potx",

                // Text / Data
                ".txt", ".csv",

                // Archives
                ".zip", ".rar", ".7z",

                // Videos
                ".mp4", ".avi", ".mov", ".webm", ".ogg",

                // Images
                ".jpg", ".jpeg", ".png", ".gif", ".bmp"
            };

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                {
                    errors.Add($"Invalid file type: {file.FileName}. Allowed types: {string.Join(", ", allowedExtensions)}");
                }
            }

            if (errors.Any())
            {
                return Json(new
                {
                    success = false,
                    message = "File type validation failed.",
                    errors = errors
                });
            }

            // Ensure folders exist and are writable
            string originalsFolder = Path.Combine(_storageRoot, "Originals");
            string pdfFolder = Path.Combine(_storageRoot, "Uploads");
            string videosFolder = Path.Combine(_storageRoot, "Videos");

            try
            {
                Directory.CreateDirectory(originalsFolder);
                Directory.CreateDirectory(pdfFolder);
                Directory.CreateDirectory(videosFolder);

                // Test write permissions
                await TestFolderPermissionsAsync(originalsFolder, "Originals");
                await TestFolderPermissionsAsync(pdfFolder, "PDF");
                await TestFolderPermissionsAsync(videosFolder, "Videos");
            }
            catch (Exception ex)
            {
                errors.Add($"Folder permission error: {ex.Message}");
                return Json(new
                {
                    success = false,
                    message = "Folder permission error.",
                    errors = errors
                });
            }

            var documentsToCreate = new List<DocRegister>();
            var requiredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uploadedFileNames = files.Select(f => Path.GetFileName(f.FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string loggedInUser = User.Identity?.Name ?? "System";

            // --- Process Excel Metadata using temporary file for large files ---
            string tempExcelPath = null;
            try
            {
                var ext = Path.GetExtension(excelFile.FileName);
                if (ext != ".xlsx" && ext != ".xlsm" && ext != ".xltx" && ext != ".xltm")
                {
                    errors.Add("Excel file must be .xlsx, .xlsm, .xltx, or .xltm");
                    return Json(new { success = false, message = "Invalid Excel file type", errors });
                }

                tempExcelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ext);

                await using (var tempStream = new FileStream(tempExcelPath, FileMode.Create))
                {
                    await excelFile.CopyToAsync(tempStream);
                }

                using var workbook = new XLWorkbook(tempExcelPath);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    try
                    {
                        string sopNumber = row.Cell(1)?.GetString()?.Trim() ?? string.Empty;
                        string originalFile = row.Cell(2)?.GetString()?.Trim() ?? string.Empty;
                        string fileName = row.Cell(3)?.GetString()?.Trim() ?? string.Empty;
                        string extension = row.Cell(4)?.GetString()?.Trim() ?? string.Empty;
                        string author = row.Cell(5)?.GetString()?.Trim() ?? string.Empty;
                        string department = row.Cell(6)?.GetString()?.Trim() ?? string.Empty;
                        DateTime? lastReviewDate = SafeParseDate(row.Cell(7)?.GetString());
                        string docType = row.Cell(8)?.GetString()?.Trim() ?? string.Empty;
                        DateTime? effectiveDate = SafeParseDate(row.Cell(9)?.GetString());
                        string documentType = row.Cell(10)?.GetString()?.Trim() ?? string.Empty;
                        string area = row.Cell(11)?.GetString()?.Trim() ?? string.Empty;
                        string revision = row.Cell(12)?.GetString()?.Trim() ?? string.Empty;

                        if (!revision.StartsWith("Rev: ", StringComparison.OrdinalIgnoreCase))
                            revision = "Rev: " + revision;

                        // Override DocType with dropdown selection if Excel is empty
                        if (string.IsNullOrWhiteSpace(docType))
                            docType = selectedDocType;
                        else if (!docType.Equals(selectedDocType, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"Row {row.RowNumber()}: DocType '{docType}' does not match selected DocType '{selectedDocType}'. Using selected DocType.");
                            docType = selectedDocType;
                        }

                        string pdfFileName = string.IsNullOrWhiteSpace(fileName) ? "N/A" : $"{fileName}.pdf";
                        string originalFileName = $"{originalFile}{extension}";

                        if (string.IsNullOrWhiteSpace(sopNumber))
                        {
                            errors.Add($"Row {row.RowNumber()}: SOP Number is required.");
                            problematicLines.Add(row.RowNumber());
                            failedCount++;
                            continue;
                        }

                        if (documentsToCreate.Any(d => d.SopNumber.Equals(sopNumber, StringComparison.OrdinalIgnoreCase)))
                        {
                            errors.Add($"Row {row.RowNumber()}: Duplicate SOP Number '{sopNumber}' in Excel.");
                            problematicLines.Add(row.RowNumber());
                            failedCount++;
                            continue;
                        }

                        // Check if document already exists (more efficient query)
                        var existingDoc = await _context.DocRegisters
                            .AsNoTracking()
                            .FirstOrDefaultAsync(d =>
                                (d.SopNumber.Equals(sopNumber) &&
                                 d.FileName.Equals(pdfFileName)) ||
                                d.OriginalFile.Equals(originalFileName));

                        if (existingDoc != null)
                        {
                            skippedDocuments.Add($"SOP: {sopNumber}, File: {originalFileName} or {pdfFileName}");
                            continue;
                        }

                        // Archive old SOPs if any (batch this later)
                        var existingSop = await _context.DocRegisters
                            .FirstOrDefaultAsync(d => d.SopNumber.Equals(sopNumber) &&
                                                      d.IsArchived != true);
                        if (existingSop != null)
                        {
                            existingSop.IsArchived = true;
                            existingSop.Status = "Archived";
                            existingSop.Revision += " (Archived)";
                            existingSop.UploadDate = DateTime.Now;
                            _context.DocRegisters.Update(existingSop);
                        }

                        // Track required files
                        requiredFiles.Add(originalFileName);
                        if (pdfFileName != "N/A") requiredFiles.Add(pdfFileName);

                        var newDoc = new DocRegister
                        {
                            SopNumber = sopNumber,
                            FileName = pdfFileName,
                            OriginalFile = originalFileName,
                            uniqueNumber = Guid.NewGuid().ToString(),
                            ContentType = GetMimeType(extension),
                            LastReviewDate = lastReviewDate,
                            UploadDate = DateTime.Now,
                            FileSize = 0,
                            Author = author,
                            Department = department,
                            DocType = docType,
                            Area = area,
                            EffectiveDate = effectiveDate,
                            DocumentType = documentType,
                            Revision = revision,
                            Status = "Pending Approval",
                            ReviewedBy = loggedInUser
                        };

                        documentsToCreate.Add(newDoc);
                        validDocuments.Add(newDoc);
                    }
                    catch (Exception exRow)
                    {
                        errors.Add($"Row {row.RowNumber()}: Failed to process - {exRow.Message}");
                        problematicLines.Add(row.RowNumber());
                        failedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to process Excel file: {ex.Message}");
                return Json(new
                {
                    success = false,
                    message = "Failed to process Excel file.",
                    errors = errors
                });
            }
            finally
            {
                // Clean up temp file
                if (tempExcelPath != null && System.IO.File.Exists(tempExcelPath))
                {
                    try { System.IO.File.Delete(tempExcelPath); } catch { /* Ignore cleanup errors */ }
                }
            }

            // --- Validate file existence ---
            var missingFiles = requiredFiles.Where(f => !uploadedFileNames.Contains(f)).ToList();
            if (missingFiles.Any())
            {
                foreach (var missing in missingFiles)
                {
                    errors.Add($"Missing required file: {missing}");
                    failedCount++;
                }
            }

            // --- Process in smaller, manageable batches ---
            if (documentsToCreate.Any())
            {
                // Create lookup dictionary for faster file matching
                var fileLookup = documentsToCreate
                    .SelectMany(d => new[]
                    {
                new { FileName = d.FileName, Document = d },
                new { FileName = d.OriginalFile, Document = d }
                    })
                    .Where(x => !string.IsNullOrEmpty(x.FileName) && x.FileName != "N/A")
                    .GroupBy(x => x.FileName)
                    .ToDictionary(x => x.Key, x => x.First().Document, StringComparer.OrdinalIgnoreCase);

                // Process database operations first
                try
                {
                    // Save documents in batches to avoid timeouts
                    var performedBy = User.FindFirst("LaborName")?.Value ?? User.Identity?.Name ?? "System";
                    for (int i = 0; i < documentsToCreate.Count; i += batchSize)
                    {
                        var batch = documentsToCreate.Skip(i).Take(batchSize).ToList();
                        await _context.DocRegisters.AddRangeAsync(batch);
                        await _context.SaveChangesAsync();

                        foreach (var doc in batch)
                            await _auditLog.LogAsync(doc.Id, doc.SopNumber ?? "", "Uploaded", performedBy, "Bulk upload", doc.OriginalFile);

                        // Process files for this batch
                        var batchResult = await ProcessFilesBatchAsync(files, batch, fileLookup, originalsFolder, pdfFolder, videosFolder, processedFiles, errors);
                        successCount += batchResult;
                    }
                }
                catch (Exception exDb)
                {
                    errors.Add($"Database error: {exDb.Message}");
                    failedCount += documentsToCreate.Count - successCount;
                }
            }

            // --- Prepare status summary ---
            var status = failedCount == 0 ? "Success" : (successCount > 0 ? "Warning" : "Error");
            var messageSummary = new StringBuilder();
            if (successCount > 0) messageSummary.AppendLine($"Successfully processed {successCount} document(s).");
            if (skippedDocuments.Any())
            {
                messageSummary.AppendLine($"Skipped {skippedDocuments.Count} existing document(s).");
            }
            if (failedCount > 0)
            {
                messageSummary.AppendLine($"Failed to process {failedCount} document(s).");
                if (problematicLines.Any())
                    messageSummary.AppendLine($"Problematic rows: {string.Join(", ", problematicLines.Distinct())}");
            }

            return Json(new
            {
                success = failedCount == 0,
                message = messageSummary.ToString(),
                details = $"Processed {successCount} document(s), skipped {skippedDocuments.Count}.",
                errors = errors
            });
        }

        // Helper method to process files in batches
        private async Task<int> ProcessFilesBatchAsync( List<IFormFile> files, List<DocRegister> batch, Dictionary<string, DocRegister> fileLookup, string originalsFolder, string pdfFolder, string videosFolder, HashSet<string> processedFiles, List<string> errors)
        {
            int batchSuccessCount = 0;

            foreach (var file in files)
            {
                try
                {
                    string fileName = Path.GetFileName(file.FileName);

                    // Skip if already processed or not in lookup
                    if (processedFiles.Contains(fileName) || !fileLookup.TryGetValue(fileName, out var matchingDoc))
                        continue;

                    // Check if this document belongs to the current batch
                    if (!batch.Contains(matchingDoc))
                        continue;

                    string targetFolder;
                    string fileExtension = Path.GetExtension(fileName).ToLower();

                    if (fileExtension == ".pdf")
                        targetFolder = pdfFolder;
                    else if (fileExtension == ".mp4" || fileExtension == ".avi" || fileExtension == ".mov")
                        targetFolder = videosFolder;
                    else
                    {
                        var docTypeFolder = Path.Combine(originalsFolder, matchingDoc.DocType ?? "General");
                        Directory.CreateDirectory(docTypeFolder);
                        targetFolder = docTypeFolder;
                    }

                    string filePath = Path.Combine(targetFolder, fileName);

                    // Ensure directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                    // Delete existing file if it exists
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);

                    // Use buffered async copy for large files
                    await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

                    await file.CopyToAsync(fileStream);

                    // Update file size
                    var fileInfo = new FileInfo(filePath);
                    matchingDoc.FileSize = fileInfo.Length;

                    processedFiles.Add(fileName);
                    batchSuccessCount++;
                }
                catch (Exception exFile)
                {
                    errors.Add($"Failed to process file {file.FileName}: {exFile.Message}");
                }
            }

            // Save file size updates
            if (batchSuccessCount > 0)
            {
                await _context.SaveChangesAsync();
            }

            return batchSuccessCount;
        }

        // Helper method to test folder permissions
        private async Task TestFolderPermissionsAsync(string folderPath, string folderName)
        {
            var testFile = Path.Combine(folderPath, $"permission_test_{Guid.NewGuid()}.tmp");
            try
            {
                await System.IO.File.WriteAllTextAsync(testFile, "test");
                if (!System.IO.File.Exists(testFile))
                    throw new IOException($"Cannot write to {folderName} folder");

                System.IO.File.Delete(testFile);
            }
            catch
            {
                throw new UnauthorizedAccessException($"No write permission to {folderName} folder: {folderPath}");
            }
        }

        // Safe date parsing method
        private DateTime? SafeParseDate(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return null;

            if (DateTime.TryParse(dateString, out DateTime result))
                return result;

            return null;
        }

        // Get MIME type method (add this if missing)
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
                ".xlt" => "application/vnd.ms-excel", // legacy Excel template
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xltx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.template", // modern Excel template
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".webm" => "video/webm",
                ".ogg" => "video/ogg",
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


        public async Task<IActionResult> VerifyUploadedFiles()
        {
            var missingFiles = new List<string>();
            var records = await _context.DocRegisters
                .Where(r => r.IsArchived == false)
                .ToListAsync();

            string pdfFolder = Path.Combine(_storageRoot, "uploads");
            string originalsFolder = Path.Combine(_storageRoot, "Originals");
            string structuredPdfsFolder = Path.Combine(_storageRoot, "structuredpdfs");

            // Cache all files' base names for quick lookup
            var pdfFilesBaseNames = Directory.GetFiles(pdfFolder)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var originalsFilesBaseNames = Directory.GetFiles(originalsFolder)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var structuredPdfFilesBaseNames = Directory.Exists(structuredPdfsFolder)
                ? Directory.GetFiles(structuredPdfsFolder).Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                // Check for PDF file
                if (!string.IsNullOrWhiteSpace(record.FileName) && record.FileName != "N/A")
                {
                    var baseName = Path.GetFileNameWithoutExtension(record.FileName);

                    // Check if file is missing in all relevant PDF folders
                    if (!pdfFilesBaseNames.Contains(baseName) && !structuredPdfFilesBaseNames.Contains(baseName))
                    {
                        missingFiles.Add($"Missing in PDF Folders: {record.FileName} (SOP: {record.SopNumber})");
                    }
                }

                // Check for Original file
                if (!string.IsNullOrWhiteSpace(record.OriginalFile))
                {
                    var baseName = Path.GetFileNameWithoutExtension(record.OriginalFile);

                    if (!originalsFilesBaseNames.Contains(baseName))
                    {
                        missingFiles.Add($"Missing in Originals Folder: {record.OriginalFile} (SOP: {record.SopNumber})");
                    }
                }
            }

            ViewBag.MissingFiles = missingFiles;
            return View();
        }

        private async Task<string> GetDepartmentNameFromId(string deptId)
        {
            var department = await _entTTSAPDbContext.Department
                .FirstOrDefaultAsync(d => d.DepartmentID == deptId);

            return department?.DepartmentName ?? deptId;
        }


        [HttpGet]
        [Authorize(Roles = "Admin")]
        // DOWNLOAD FILE FUNCTION
        public async Task<IActionResult> Download(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest("Invalid document ID.");

                // Get document metadata
                var document = await _context.DocRegisters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (document == null)
                    return NotFound("Document metadata not found.");

                if (string.IsNullOrWhiteSpace(document.OriginalFile) && string.IsNullOrWhiteSpace(document.FileName))
                    return BadRequest("Document filename is missing.");

                var basePath = _configuration["StorageSettings:BasePath"];
                if (string.IsNullOrEmpty(basePath))
                    return StatusCode(500, "Storage configuration is missing.");

                // "Download Document" = PDF only. Search ONLY in PDFs folder for a .pdf file.
                string pdfFileName = !string.IsNullOrWhiteSpace(document.FileName) ? Path.GetFileName(document.FileName) : null;
                string originalFileName = Path.GetFileName(document.OriginalFile ?? "");
                string originalBaseNoExt = Path.GetFileNameWithoutExtension(originalFileName);

                string actualFilePath = null;
                string downloadName = null;
                var pdfFolder = Path.Combine(basePath, "PDFs");

                if (Directory.Exists(pdfFolder))
                {
                    var allPdfs = Directory.GetFiles(pdfFolder, "*.pdf", SearchOption.AllDirectories);
                    // Match by stored PDF filename, or by original base name + .pdf
                    var pdfMatch = allPdfs.FirstOrDefault(f =>
                    {
                        var name = Path.GetFileName(f);
                        var baseName = Path.GetFileNameWithoutExtension(f);
                        if (!string.IsNullOrEmpty(pdfFileName) && name.Equals(pdfFileName, StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (!string.IsNullOrEmpty(originalBaseNoExt) && baseName.Equals(originalBaseNoExt, StringComparison.OrdinalIgnoreCase))
                            return true;
                        return false;
                    });
                    if (pdfMatch != null)
                    {
                        actualFilePath = pdfMatch;
                        downloadName = Path.GetFileName(pdfMatch);
                    }
                }

                // "Download Original" is a separate link; this action should only serve PDF for main download.
                // If no PDF exists, fall back to original file so the button still works.
                if (actualFilePath == null && !string.IsNullOrEmpty(originalFileName))
                {
                    var searchPathsOriginal = new List<string>
                    {
                        Path.Combine(basePath, "Originals", originalFileName),
                        Path.Combine(basePath, "Videos", originalFileName)
                    };
                    if (!string.IsNullOrWhiteSpace(document.DocType))
                        searchPathsOriginal.Insert(0, Path.Combine(basePath, "Originals", document.DocType, originalFileName));

                    foreach (var path in searchPathsOriginal)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            actualFilePath = path;
                            downloadName = document.OriginalFile;
                            break;
                        }
                    }
                }

                if (actualFilePath == null)
                {
                    var origFolder = Path.Combine(basePath, "Originals");
                    var vidFolder = Path.Combine(basePath, "Videos");
                    foreach (var folder in new[] { origFolder, vidFolder })
                    {
                        if (!Directory.Exists(folder)) continue;
                        var match = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                            .FirstOrDefault(f => Path.GetFileName(f).Equals(originalFileName, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            actualFilePath = match;
                            downloadName = document.OriginalFile;
                            break;
                        }
                    }
                }

                if (actualFilePath == null)
                    return NotFound("The document file could not be found in the storage location.");

                string contentType = GetMimeType(Path.GetExtension(actualFilePath)) ?? "application/octet-stream";
                if (string.IsNullOrEmpty(downloadName))
                    downloadName = Path.GetFileName(actualFilePath);

                return PhysicalFile(actualFilePath, contentType, downloadName, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading document with ID {DocumentId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An error occurred while processing your request.");
            }
        }

        // Enhanced MIME type detection method
      

        // GET: VIEW FILE FUNCTION
        public async Task<IActionResult> GetDocument(string fileName, string docType = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("Filename is required.");

            // Prepare search folders
            var searchFolders = new List<string>();

            if (!string.IsNullOrWhiteSpace(docType))
                searchFolders.Add(Path.Combine(_storageRoot, "Originals", docType));

            searchFolders.Add(Path.Combine(_storageRoot, "Originals")); // fallback
            searchFolders.Add(Path.Combine(_storageRoot, "PDFs"));
            searchFolders.Add(Path.Combine(_storageRoot, "videos"));

            string filePath = null;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

            // Search recursively in all folders
            foreach (var folder in searchFolders)
            {
                if (!Directory.Exists(folder)) continue;

                filePath = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                                    .FirstOrDefault(f => Path.GetFileName(f)
                                    .Equals(fileName, StringComparison.OrdinalIgnoreCase));

                if (filePath != null) break;

                // fallback: match without extension
                filePath = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                                    .Equals(fileNameWithoutExt, StringComparison.OrdinalIgnoreCase));

                if (filePath != null) break;
            }

            if (filePath == null)
                return NotFound("File not found.");

            var mimeType = GetMimeType(Path.GetExtension(filePath)) ?? "application/octet-stream";
            return PhysicalFile(filePath, mimeType, Path.GetFileName(filePath), enableRangeProcessing: true);
        }


        private string EnsureUploadDirectoryExists()
        {
            string uploadFolder = Path.Combine(_env.WebRootPath, "Originals");


            if (!Directory.Exists(uploadFolder))
                Directory.CreateDirectory(uploadFolder);

            return uploadFolder;
        }

        public async Task<IActionResult> ViewDetails(int id)
        {
            var document = await _context.DocRegisters.FindAsync(id);
            if (document == null)
            {
                return NotFound();
            }
            return View(document);
        }

       
    }
}
