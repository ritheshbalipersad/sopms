using DinkToPdf;
using DinkToPdf.Contracts;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using SOPMSApp.Data;
using SOPMSApp.Extensions;
using SOPMSApp.Models;
using SOPMSApp.Services;
using SOPMSApp.ViewModels;
using System.Security.AccessControl;
using System.Text.RegularExpressions;

public class StructuredSopController : Controller
{

    private readonly string _storageRoot;
    private readonly IConverter _pdfConverter;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _context;
    private readonly entTTSAPDbContext _entTTSAPDbContext;
    private readonly DocRegisterService _docRegisterService;
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly ILogger<StructuredSopController> _logger;

    

    public StructuredSopController(ApplicationDbContext context, IWebHostEnvironment hostingEnvironment, IConfiguration configuration, IConverter pdfConverter, DocRegisterService docRegisterService, entTTSAPDbContext entTTSAPDbContext, ILogger<StructuredSopController> logger)
    {
       
        _logger = logger;
        _context = context;
        _configuration = configuration;
        _entTTSAPDbContext = entTTSAPDbContext;
        _hostingEnvironment = hostingEnvironment;
        _storageRoot = configuration["StorageSettings:BasePath"];
        _pdfConverter = pdfConverter ?? throw new ArgumentNullException(nameof(pdfConverter));
        _docRegisterService = docRegisterService ?? throw new ArgumentNullException(nameof(docRegisterService));
        
    }

    
    public async Task<IActionResult> Index(string? searchTerm)
    {
        try
        {
            var today = DateTime.Today;

            // Base query: all approved, non-active, non-archived SOPs
            IQueryable<StructuredSop> sopsQuery = _context.StructuredSops
            .Where(d => d.ArchivedOn == null);

            // Apply search filter if searchTerm is provided
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                sopsQuery = sopsQuery.Where(s =>
                    s.SopNumber.Contains(searchTerm) ||
                    s.Title.Contains(searchTerm) ||
                    s.ControlledBy.Contains(searchTerm));
                ViewBag.ShowReset = true; // ✅ tell the view to show reset
            }
            else
            {
                ViewBag.ShowReset = false;
            }


            // Apply ordering last
            sopsQuery = sopsQuery.OrderBy(d => d.EffectiveDate);

            var sops = await sopsQuery.ToListAsync();


            // Flag to show Reset button only when searchTerm is used
            ViewBag.ShowReset = !string.IsNullOrWhiteSpace(searchTerm);

            if (!sops.Any())
            {
                ViewBag.Message = "No documents found.";
            }

            return View(sops);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Structured SOPs");
            TempData["Error"] = "An error occurred while loading documents.";
            return View(new List<StructuredSop>());
        }
    }
    

    [HttpGet]
    public async Task<IActionResult> GetSopNumber(string docType)
    {
        if (string.IsNullOrWhiteSpace(docType))
            return Json(new { sopNumber = "" });

        string sopNumber = await GenerateSopNumberAsync(docType);
        return Json(new { sopNumber });
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        try
        {
            ViewData["Areas"] = await GetAreasAsync() ?? new List<string>();
            ViewData["Department"] = await GetDepartmentsAsync() ?? new List<DepartmentModel>();
            ViewData["Documents"] = await GetDocunentsAsync() ?? new List<string>();
            ViewBag.UserDepartment = User?.Identity?.Name ?? "";

            var userDepartmentId = User.FindFirst("DepartmentID")?.Value;
            var userDepartment = !string.IsNullOrEmpty(userDepartmentId)
                ? await _entTTSAPDbContext.Department.FirstOrDefaultAsync(d => d.DepartmentID == userDepartmentId)
                : null;

            var defaultDepartment = userDepartment?.DepartmentName ?? "Admin";

            return View(new SopCreateViewModel
            {
                Steps = new List<SopStepViewModel> { new SopStepViewModel() { StepNumber = 1 } },
                ControlledBy = defaultDepartment,
                Signatures = User.FindFirst("LaborName")?.Value ?? "Unknown User",
                EffectiveDate = DateTime.Today.AddDays(1)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading SOP create page");
            TempData["Error"] = "Error loading form. Please try again.";
            return RedirectToAction("Index");
        }
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SopCreateViewModel vm)
    {
        _logger.LogInformation("Automatic Create SOP called - SOP: {SopNumber}", vm.SopNumber);

        // 1️⃣ Basic Validations
        if (vm.SelectedAreas == null || !vm.SelectedAreas.Any())
            ModelState.AddModelError("SelectedAreas", "Please select at least one applicable area.");

        if (vm.Steps == null || !vm.Steps.Any() || vm.Steps.Any(s => string.IsNullOrWhiteSpace(s.Instructions)))
            ModelState.AddModelError("", "At least one step with instructions is required.");

        if (!ModelState.IsValid)
        {
            await RepopulateViewDataAsync();
            return View(vm);
        }

        // 2️⃣ Generate SOP Number Automatically
        if (string.IsNullOrWhiteSpace(vm.SopNumber))
        {
            vm.SopNumber = await GenerateSopNumberAsync(vm.DocType);
            if (string.IsNullOrWhiteSpace(vm.SopNumber))
            {
                ModelState.AddModelError("", "Unable to generate SOP Number. Please check the Document Type acronym.");
                await RepopulateViewDataAsync();
                return View(vm);
            }
        }

        string loggedInUser = User.FindFirst("LaborName")?.Value ?? "Unknown User";
        string email = User.FindFirst("Email")?.Value ?? "N/A";

        string departmentSupervisor = "N/A";
        string supervisorEmail = "N/A";

        if (!string.IsNullOrEmpty(vm.ControlledBy))
        {
            var supervisorInfo = await (from d in _entTTSAPDbContext.Department
                                        join l in _entTTSAPDbContext.Labor on d.SupervisorName equals l.LaborName
                                        where d.DepartmentName == vm.ControlledBy
                                        select new { l.LaborName, l.Email }).FirstOrDefaultAsync();

            departmentSupervisor = supervisorInfo?.LaborName ?? "N/A";
            supervisorEmail = supervisorInfo?.Email ?? "N/A";
        }

        // 3️⃣ Check duplicates
        var duplicateInStructuredSops = await _context.StructuredSops
            .Where(s => s.ArchivedOn == null)
            .Where(s => s.SopNumber == vm.SopNumber || s.Title.ToLower().Trim() == vm.Title.ToLower().Trim())
            .FirstOrDefaultAsync();

        var duplicateInDocRegister = await _context.DocRegisters
            .Where(d => (d.IsArchived == null || d.IsArchived == false))
            .Where(d => d.SopNumber == vm.SopNumber || d.OriginalFile.ToLower().Trim() == (vm.SopNumber + "_" + vm.Title).ToLower().Trim())
            .FirstOrDefaultAsync();

        // ❌ Error if duplicate exists in both tables
        if (duplicateInStructuredSops != null && duplicateInDocRegister != null)
        {
            ModelState.AddModelError("", $"Duplicate Work Instruction detected for '{vm.SopNumber}' or title '{vm.Title}'. Please check under Review Work Instruction, and revise if necessary");
            await RepopulateViewDataAsync();
            return View(vm);
        }


        // 4️⃣ Automatically archive DocRegister duplicate
        if (duplicateInDocRegister != null)
        {
            await ArchiveExistingDocRegisterAsync(duplicateInDocRegister, loggedInUser);
        }



        // 5️⃣ Proceed to create new SOP
        try
        {
            var basePath = _configuration["StorageSettings:BasePath"];
            if (string.IsNullOrEmpty(basePath))
            {
                ModelState.AddModelError("", "Storage configuration is missing.");
                await RepopulateViewDataAsync();
                return View(vm);
            }

            var sop = new StructuredSop
            {
                SopNumber = vm.SopNumber?.Trim(),
                Title = vm.Title?.Trim(),
                Revision = vm.Revision?.Trim(),
                EffectiveDate = vm.EffectiveDate,
                ControlledBy = vm.ControlledBy?.Trim(),
                DocType = vm.DocType?.Trim(),
                Area = vm.SelectedAreas != null ? string.Join(", ", vm.SelectedAreas) : "",
                Status = "Pending Approval",
                ReviewedBy = null,
                ApprovedBy = "Pending Approval",
                CreatedDate = DateTime.Now,
                CreatedAt = DateTime.Now,
                Signatures = loggedInUser,
                UserEmail = email,
                DepartmentSupervisor = departmentSupervisor,
                SupervisorEmail = supervisorEmail,
                Steps = new List<SopStep>()
            };

            // Ensure directories exist
            var instructionsPath = Path.Combine(basePath, "StructuredSop", "instructions");
            var keypointsPath = Path.Combine(basePath, "StructuredSop", "keypoints");
            Directory.CreateDirectory(instructionsPath);
            Directory.CreateDirectory(keypointsPath);

            // 6️ Process Steps
            for (int i = 0; i < vm.Steps.Count; i++)
            {
                var stepVm = vm.Steps[i];
                var instructionImagePaths = new List<string>();
                var keyPointImagePaths = new List<string>();

                try
                {
                    // Process Instructions
                    if (!string.IsNullOrEmpty(stepVm.Instructions))
                    {
                        var processedInstructions = await ProcessHtmlWithImagesAsync(
                            stepVm.Instructions,
                            "StructuredSop/instructions",
                            $"step_{i + 1}_instructions",
                            basePath
                        );
                        instructionImagePaths.AddRange(processedInstructions.SavedImagePaths);
                        stepVm.Instructions = processedInstructions.ProcessedHtml;
                    }

                    // Process Step Images (Upload mode)
                    if (stepVm.StepImages != null && stepVm.StepImages.Any())
                    {
                        foreach (var img in stepVm.StepImages)
                        {
                            if (img != null && img.Length > 0)
                            {
                                var path = await SaveFileToNewStorageAsync(img, "StructuredSop/instructions", basePath);
                                if (!string.IsNullOrEmpty(path))
                                    instructionImagePaths.Add(path);
                            }
                        }
                    }

                    // Process Step Images (Paste mode)
                    if (!string.IsNullOrEmpty(stepVm.StepImagesPaste))
                    {
                        var processedStepImages = await ProcessHtmlWithImagesAsync(
                            stepVm.StepImagesPaste,
                            "StructuredSop/instructions",
                            $"step_{i + 1}_step_images",
                            basePath
                        );
                        instructionImagePaths.AddRange(processedStepImages.SavedImagePaths);
                        stepVm.StepImagesPaste = processedStepImages.ProcessedHtml;
                    }

                    // Process Key Points
                    if (!string.IsNullOrEmpty(stepVm.KeyPoints))
                    {
                        var processedKeyPoints = await ProcessHtmlWithImagesAsync(
                            stepVm.KeyPoints,
                            "StructuredSop/keypoints",
                            $"step_{i + 1}_keypoints",
                            basePath
                        );
                        keyPointImagePaths.AddRange(processedKeyPoints.SavedImagePaths);
                        stepVm.KeyPoints = processedKeyPoints.ProcessedHtml;
                    }

                    // Process Key Point Image (Upload mode)
                    if (stepVm.KeyPointImage != null && stepVm.KeyPointImage.Length > 0)
                    {
                        var path = await SaveFileToNewStorageAsync(stepVm.KeyPointImage, "StructuredSop/keypoints", basePath);
                        if (!string.IsNullOrEmpty(path))
                            keyPointImagePaths.Add(path);
                    }

                    // Process Key Point Image (Paste mode)
                    if (!string.IsNullOrEmpty(stepVm.KeyPointImagePaste))
                    {
                        var processedKeyPointImages = await ProcessHtmlWithImagesAsync(
                            stepVm.KeyPointImagePaste,
                            "StructuredSop/keypoints",
                            $"step_{i + 1}_keypoint_images",
                            basePath
                        );
                        keyPointImagePaths.AddRange(processedKeyPointImages.SavedImagePaths);
                        stepVm.KeyPointImagePaste = processedKeyPointImages.ProcessedHtml;
                    }

                    // Add step to SOP
                    sop.Steps.Add(new SopStep
                    {
                        StepNumber = stepVm.StepNumber,
                        Instructions = stepVm.Instructions,
                        KeyPoints = stepVm.KeyPoints,
                        ImagePath = instructionImagePaths.Any() ? string.Join(",", instructionImagePaths.Distinct()) : null,
                        KeyPointImagePath = keyPointImagePaths.Any() ? string.Join(",", keyPointImagePaths.Distinct()) : null
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing step {StepNumber}", i + 1);
                    sop.Steps.Add(new SopStep
                    {
                        StepNumber = stepVm.StepNumber,
                        Instructions = stepVm.Instructions ?? "Error processing step content",
                        KeyPoints = stepVm.KeyPoints,
                        ImagePath = null,
                        KeyPointImagePath = null
                    });
                }
            }


            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Add Structured SOP
                _context.StructuredSops.Add(sop);
                await _context.SaveChangesAsync();

                // Generate PDF
                string pdfFileName = await GenerateAndSavePdfToOriginalsAsync(sop, basePath);
                // 🔁 Sync with DocRegister (creates or updates automatically)
                await _docRegisterService.SyncStructuredSopAsync(sop, loggedInUser, email, pdfFileName, basePath);

                await transaction.CommitAsync();

                _logger.LogInformation("SOP created successfully: {SopNumber}", sop.SopNumber);

                TempData["Success"] = "SOP created successfully!";
                return RedirectToAction(nameof(Details), new { id = sop.Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction failed for SOP: {SopNumber}", vm.SopNumber);
                ModelState.AddModelError("", "An error occurred while saving. Please try again.");
                await RepopulateViewDataAsync();
                return View(vm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating SOP: {SopNumber}", vm.SopNumber);
            ModelState.AddModelError("", $"Error saving SOP: {ex.Message}");
            await RepopulateViewDataAsync();
            return View(vm);
        }
    }

    private async Task ArchiveExistingStructuredSopAsync(StructuredSop sop, string archivedBy)
    {
        sop.ArchivedOn = DateTime.UtcNow;
        //sop.ArchivedBy = archivedBy;
        sop.Status = "Archived";

        _context.StructuredSops.Update(sop);
        await _context.SaveChangesAsync();
    }

    // Get acronym for the selected DocType from Bulletin table
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

    // Generate SOP Number automatically from acronym
    private async Task<string> GenerateSopNumberAsync(string docType)
    {
        if (string.IsNullOrWhiteSpace(docType))
            return null;

        string acronym = await GetSopAcronymAsync(docType);
        if (string.IsNullOrWhiteSpace(acronym))
            return null;

        // Fetch only SOP Numbers starting with the acronym
        var existingNumbers = await _context.DocRegisters
            .Where(d => d.SopNumber.StartsWith(acronym))
            .Select(d => d.SopNumber)
            .ToListAsync();

        int maxSuffix = 0;

        foreach (var sop in existingNumbers)
        {
            string suffix = sop.Substring(acronym.Length);
            if (int.TryParse(suffix, out int number) && number > maxSuffix)
                maxSuffix = number;
        }

        // Generate new number with a 3-digit suffix (e.g., WI001, WI002, ...)
        return $"{acronym}{(maxSuffix + 1):D3}";
    }

    private async Task ArchiveExistingDocRegisterAsync(DocRegister existingDoc, string archivedBy)
    {
        if (existingDoc == null) return;

        existingDoc.IsArchived = true;
        //existingDoc.ArchivedBy = archivedBy;
        existingDoc.ArchivedOn = DateTime.Now;

        _context.DocRegisters.Update(existingDoc);
        await _context.SaveChangesAsync();
    }

    private async Task RepopulateViewDataAsync()
    {
        ViewData["Areas"] = await GetAreasAsync() ?? new List<string>();
        ViewData["Department"] = await GetDepartmentsAsync() ?? new List<DepartmentModel>();
        ViewData["Documents"] = await GetDocunentsAsync() ?? new List<string>();
        ViewBag.UserDepartment = User?.Identity?.Name ?? "";
    }

    private async Task ArchiveExistingSopAsync(DocRegister existingDoc)
    {
        try
        {
            var existingSop = await _context.StructuredSops
                .FirstOrDefaultAsync(s => s.SopNumber == existingDoc.SopNumber && s.Title == existingDoc.OriginalFile);

            if (existingSop != null)
            {
                // Normalize the Revision string
                string revision = existingSop.Revision?.Trim() ?? string.Empty;
                revision = revision.Replace("Rev:", "", StringComparison.OrdinalIgnoreCase).Trim();
                revision = $"Rev: {revision}";

                // Archive old structured SOP record
                var oldDoc = new DocRegister
                {
                    SopNumber = existingSop.SopNumber,
                    OriginalFile = existingSop.Title,
                    uniqueNumber = Guid.NewGuid().ToString(),
                    FileName = "",
                    ContentType = "structured/sop",
                    Author = existingSop.Signatures,
                    Department = existingSop.ControlledBy,
                    DocType = existingSop.DocType,
                    Area = existingSop.Area,
                    LastReviewDate = DateTime.Now,
                    EffectiveDate = existingSop.EffectiveDate,
                    Revision = revision,
                    Status = "Archived",
                    ReviewedBy = "Archived",
                    FileSize = 0,
                    IsArchived = true,
                    ArchivedOn = DateTime.Now
                };

                // Mark the structured SOP as archived
                existingSop.Status = "Archived";
                existingSop.Revision = revision;
                _context.StructuredSops.Update(existingSop);

                // Save the archived record in DocRegisters
                await _docRegisterService.ArchiveAndSaveAsync(oldDoc, true);

                _logger.LogInformation("Archived existing SOP: {SopNumber}", existingSop.SopNumber);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving existing SOP: {SopNumber}", existingDoc.SopNumber);
            // Don't throw - continue with new SOP creation
        }
    }

    private async Task<List<string>> CheckForDuplicatesAsync(SopCreateViewModel vm)
    {
        var errors = new List<string>();

        // Check StructuredSops for same SOP number
        var existingActiveSop = await _context.StructuredSops
            .FirstOrDefaultAsync(s => s.SopNumber == vm.SopNumber && s.ArchivedOn == null);
        if (existingActiveSop != null)
        {
            errors.Add($"An SOP with Number '{vm.SopNumber}' already exists.");
        }

        // Check DocRegister for same SOP number
        var existingActiveDoc = await _context.DocRegisters
            .FirstOrDefaultAsync(d => d.SopNumber == vm.SopNumber && (d.IsArchived == null || d.IsArchived == false));
        if (existingActiveDoc != null)
        {
            errors.Add($"A document with SOP Number '{vm.SopNumber}' already exists in DocRegister.");
        }

        // Check for same title under same DocType
        var existingSameTitle = await _context.StructuredSops
            .Where(s => s.ArchivedOn == null)
            .Where(s => s.DocType == vm.DocType && s.Title.ToLower().Trim() == vm.Title.ToLower().Trim())
            .FirstOrDefaultAsync();
        if (existingSameTitle != null)
        {
            errors.Add($"A document with title '{vm.Title}' already exists under document type '{vm.DocType}'.");
        }

        return errors;
    }

    private async Task<(string ProcessedHtml, List<string> SavedImagePaths)> ProcessHtmlWithImagesAsync(  string htmlContent,  string subDirectory, string prefix, string basePath)
    {
        if (string.IsNullOrEmpty(htmlContent))
            return (htmlContent, new List<string>());

        var savedImagePaths = new List<string>();
        var processedHtml = htmlContent;

        // Regex to find base64 images in HTML
        var base64Pattern = @"<img[^>]*?src=""data:image/(?<type>[^;]+);base64,(?<data>[^""]*)""[^>]*>";
        var matches = Regex.Matches(htmlContent, base64Pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            try
            {
                var imageType = match.Groups["type"].Value.ToLower();
                var base64Data = match.Groups["data"].Value;

                // Validate image type
                if (!IsValidImageType(imageType))
                {
                    _logger.LogWarning("Invalid image type: {ImageType}", imageType);
                    continue;
                }

                // Validate base64 data
                if (string.IsNullOrEmpty(base64Data) || base64Data.Length < 100)
                {
                    _logger.LogWarning("Invalid base64 data for image");
                    continue;
                }

                var bytes = Convert.FromBase64String(base64Data);

                // Validate image size (max 10MB)
                if (bytes.Length > 10 * 1024 * 1024)
                {
                    _logger.LogWarning("Image too large: {Size} bytes", bytes.Length);
                    continue;
                }

                // Generate unique filename
                var fileName = $"{prefix}_{Guid.NewGuid():N}.{imageType}";
                var filePath = Path.Combine(basePath, subDirectory, fileName);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Save the image file
                await System.IO.File.WriteAllBytesAsync(filePath, bytes);

                // Create the new image URL (relative path for web access)
                var newSrc = $"/{subDirectory.Replace('\\', '/')}/{fileName}";

                // Replace base64 with file path in HTML
                var newImgTag = match.Value.Replace(
                    $"data:image/{imageType};base64,{base64Data}",
                    newSrc
                );

                processedHtml = processedHtml.Replace(match.Value, newImgTag);
                savedImagePaths.Add(filePath);

                _logger.LogInformation("Saved pasted image: {FilePath}", filePath);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Invalid base64 format in image data");
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing base64 image in HTML content");
                continue;
            }
        }

        return (processedHtml, savedImagePaths);
    }

    private bool IsValidImageType(string imageType)
    {
        var validTypes = new[] { "jpeg", "jpg", "png", "gif", "bmp", "webp" };
        return validTypes.Contains(imageType.ToLower());
    }

    private async Task<string> SaveFileToNewStorageAsync(IFormFile file, string subFolder, string basePath)
    {
        try
        {
            if (file == null || file.Length == 0)
                return null;

            // Validate file size
            if (file.Length > 50 * 1024 * 1024) // 50MB
            {
                _logger.LogWarning("File too large: {FileName} - {Size} bytes", file.FileName, file.Length);
                return null;
            }

            // Validate file type
            var validTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/bmp", "image/webp" };
            if (!validTypes.Contains(file.ContentType.ToLower()))
            {
                _logger.LogWarning("Invalid file type: {FileName} - {ContentType}", file.FileName, file.ContentType);
                return null;
            }

            var folderPath = Path.Combine(basePath, subFolder);
            Directory.CreateDirectory(folderPath);

            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(folderPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Return relative path for database storage
            return $"/StructuredSop/{Path.GetFileName(subFolder)}/{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving file to new storage: {FileName}", file?.FileName);
            return null;
        }
    }

    private async Task<List<string>> ExtractAndSavePastedImagesToNewStorageAsync(string htmlContent, string subDirectory, string prefix, string basePath)
    {
        var result = await ProcessHtmlWithImagesAsync(htmlContent, subDirectory, prefix, basePath);
        return result.SavedImagePaths;
    }

    private async Task<string> SaveBase64ImageToNewStorageAsync(string base64Data, string subDirectory, string fileName, string basePath)
    {
        try
        {
            // Check if it's a full data URL or just base64
            if (base64Data.StartsWith("data:image"))
            {
                // Extract base64 from data URL
                var match = Regex.Match(base64Data, @"data:image/(?<type>[^;]+);base64,(?<data>.+)");
                if (match.Success)
                {
                    base64Data = match.Groups["data"].Value;

                    // Ensure file extension matches image type
                    var imageType = match.Groups["type"].Value.ToLower();
                    if (!fileName.EndsWith($".{imageType}"))
                    {
                        fileName = Path.ChangeExtension(fileName, $".{imageType}");
                    }
                }
            }

            var bytes = Convert.FromBase64String(base64Data);
            var filePath = Path.Combine(basePath, subDirectory, fileName);

            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await System.IO.File.WriteAllBytesAsync(filePath, bytes);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving base64 image: {FileName}", fileName);
            return null;
        }
    }


    [HttpPost]
    public async Task<IActionResult> SavePastedImageBase64([FromBody] Base64ImageRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.Base64Image))
                return Json(new { success = false, error = "No image data received." });

            // Extract base64 content
            var base64Data = request.Base64Image;
            var base64PrefixIndex = base64Data.IndexOf(",");
            if (base64PrefixIndex > -1)
                base64Data = base64Data.Substring(base64PrefixIndex + 1);

            if (string.IsNullOrEmpty(base64Data) || base64Data.Length < 100)
                return Json(new { success = false, error = "Invalid image data." });

            byte[] imageBytes = Convert.FromBase64String(base64Data);

            // Validate image size
            if (imageBytes.Length > 10 * 1024 * 1024) // 10MB
                return Json(new { success = false, error = "Image too large. Maximum size is 10MB." });

            // Create upload path
            string uploadFolder = Path.Combine(_hostingEnvironment.WebRootPath, "uploads", "pasted");
            if (!Directory.Exists(uploadFolder))
                Directory.CreateDirectory(uploadFolder);

            // Generate filename
            string fileName = $"{Guid.NewGuid():N}.png";
            string filePath = Path.Combine(uploadFolder, fileName);

            // Save image to disk
            await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

            string fileUrl = $"/uploads/pasted/{fileName}";
            return Json(new { success = true, url = fileUrl });
        }
        catch (FormatException)
        {
            return Json(new { success = false, error = "Invalid base64 format." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving pasted image.");
            return Json(new { success = false, error = "An error occurred while saving the image." });
        }
    }

    public class Base64ImageRequest
    {
        public string Base64Image { get; set; }
        public string DocType { get; set; }
    }

    
    // UPDATED SaveFileAsync method for new storage location
    private async Task<string> SaveFileAsync(IFormFile file, string folder)
    {
        if (file == null || file.Length == 0) return null;

        try
        {
            // Get storage base path from configuration
            var basePath = _configuration["StorageSettings:BasePath"];
            if (string.IsNullOrEmpty(basePath))
            {
                throw new InvalidOperationException("Storage configuration is missing.");
            }

            var uploadsFolder = Path.Combine(basePath, folder);
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            // ✅ Keep original extension, support ANY picture type
            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // ✅ Return consistent path format for FileAccess controller
            return $"/{folder}/{uniqueFileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving uploaded file");
            return null;
        }
    }

    private async Task<string?> ProcessStepImage(IFormFile newImage, string? existingImagePath)
    {
        if (newImage != null && newImage.Length > 0)
        {
            if (!string.IsNullOrEmpty(existingImagePath))
            {
                // Delete from both old and new storage locations during migration
                var basePath = _configuration["StorageSettings:BasePath"];

                // Delete from new storage location
                if (!string.IsNullOrEmpty(basePath))
                {
                    var newPath = Path.Combine(basePath, existingImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(newPath))
                    {
                        System.IO.File.Delete(newPath);
                    }
                }

                // Delete from legacy location
                var oldPath = Path.Combine(_hostingEnvironment.WebRootPath, existingImagePath.TrimStart('/'));
                if (System.IO.File.Exists(oldPath))
                {
                    System.IO.File.Delete(oldPath);
                }
            }
            return await SaveFileAsync(newImage, "StructuredSop/instructions");
        }

        return existingImagePath;
    }

    private async Task<string> GenerateAndSavePdfToOriginalsAsync(StructuredSop sop, string basePath)
    {
        string tempFooterPath = null;
        try
        {
            if (_pdfConverter == null)
                throw new InvalidOperationException("PDF converter service is not available");

            // 1️⃣ Render main SOP HTML
            var htmlContent = await this.RenderViewToStringAsync("StructuredSopTemplate", sop);

            // 2️⃣ Render footer HTMLstring footerHtml;
            string footerHtml;
            try
            {
                footerHtml = await this.RenderViewToStringAsync("_SopPdfFooter", sop);
            }
            catch
            {
                footerHtml = $@"
                <div style='text-align: center; font-size: 10px; color: #666; padding-top: 10px;'>
                    Page <span class='page'></span> of <span class='topage'></span> | 
                    SOP: {sop.SopNumber} | 
                    Effective: {sop.EffectiveDate:yyyy-MM-dd}
                </div>";
            }


            // 3️⃣ Save footer to temp file (required by wkhtmltopdf)
            tempFooterPath = Path.Combine(Path.GetTempPath(), $"footer_{Guid.NewGuid()}.html");
            await System.IO.File.WriteAllTextAsync(tempFooterPath, footerHtml);

            // 4️⃣ Build PDF document
            var pdfDoc = new HtmlToPdfDocument
            {
                GlobalSettings = new GlobalSettings
                {
                    ColorMode = ColorMode.Color,
                    Orientation = Orientation.Portrait,
                    PaperSize = PaperKind.A4,
                    Margins = new MarginSettings { Top = 20, Bottom = 40, Left = 15, Right = 15 },
                    DocumentTitle = $"{sop.SopNumber}_{sop.Title}"
                },
                Objects = {
                    new ObjectSettings
                    {
                        HtmlContent = htmlContent,
                        WebSettings = {
                            DefaultEncoding = "utf-8",
                            LoadImages = true,
                            EnableIntelligentShrinking = true
                        },
                        FooterSettings = new FooterSettings
                        {
                            HtmUrl = tempFooterPath,
                            Line = true,
                            FontSize = 9,
                            Spacing = 5,
                            Right = "Page [page] of [toPage]"
                        }
                    }
                }
            };

            // 5️⃣ Convert to PDF bytes
            byte[] pdfBytes = _pdfConverter.Convert(pdfDoc);

            // 6️⃣ Save PDF to Originals/{DocType}/ folder
            string sanitizedDocType = string.Join("_", sop.DocType?.Split(Path.GetInvalidFileNameChars()) ?? new[] { "General" });
            string folderPath = Path.Combine(basePath, "Originals", sanitizedDocType);
            Directory.CreateDirectory(folderPath);

            string fileName = $"{sop.SopNumber}_{sop.Title}.pdf";
            string filePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(filePath, pdfBytes);

            // 7️⃣ Return just filename for DB storage (FileAccess controller will handle the path)
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for SOP {SopNumber}", sop.SopNumber);
            throw;
        }
        finally
        {
            // 8️⃣ Clean up temp footer file
            if (tempFooterPath != null && System.IO.File.Exists(tempFooterPath))
            {
                try { System.IO.File.Delete(tempFooterPath); } catch { /* Ignore cleanup errors */ }
            }
        }
    }


    //Export to PDF also saves to Originals/{DocType}/
    public async Task<IActionResult> ExportSopToPdf(int id)
    {
        if (_pdfConverter == null)
        {
            TempData["Error"] = "PDF generation service is not available";
            return RedirectToAction(nameof(Details), new { id });
        }

        var sop = await _context.StructuredSops
                                .Include(s => s.Steps)
                                .FirstOrDefaultAsync(s => s.Id == id);

        if (sop == null) return NotFound();

        string tempFooterPath = null;

        try
        {
            // Get storage base path
            var basePath = _configuration["StorageSettings:BasePath"];
            if (string.IsNullOrEmpty(basePath))
            {
                TempData["Error"] = "Storage configuration is missing";
                return RedirectToAction(nameof(Details), new { id });
            }

            // 1. Render main HTML
            var htmlContent = await this.RenderViewToStringAsync("StructuredSopTemplate", sop);

            // 2. Render footer HTML
            var footerHtml = await this.RenderViewToStringAsync("_SopPdfFooter", sop);

            // 3. Save footer to temp file (needed by wkhtmltopdf)
            tempFooterPath = Path.Combine(Path.GetTempPath(), $"footer_{Guid.NewGuid()}.html");
            System.IO.File.WriteAllText(tempFooterPath, footerHtml);

            // 4. Build PDF doc
            var pdfDoc = new HtmlToPdfDocument
            {
                GlobalSettings = new GlobalSettings
                {
                    ColorMode = ColorMode.Color,
                    Orientation = Orientation.Portrait,
                    PaperSize = PaperKind.A4,
                    Margins = new MarginSettings { Top = 20, Bottom = 40, Left = 10, Right = 10 },
                    DocumentTitle = $"{sop.SopNumber}_{sop.Title}"
                },
                Objects = {
                    new ObjectSettings
                    {
                        HtmlContent = htmlContent,
                        WebSettings = {
                            DefaultEncoding = "utf-8",
                            LoadImages = true,
                            EnableIntelligentShrinking = true
                        },
                        FooterSettings = new FooterSettings
                        {
                            HtmUrl = tempFooterPath,
                            Line = true,
                            FontSize = 10,
                            Spacing = 8,
                            Right = "Page [page] of [toPage]"
                        }
                    }
                }
            };

            var pdfBytes = _pdfConverter.Convert(pdfDoc);

            // 5. Save to Originals/{DocType}/ folder
            string sanitizedDocType = string.Join("_", sop.DocType?.Split(Path.GetInvalidFileNameChars()) ?? new[] { "General" });
            string folderPath = Path.Combine(basePath, "Originals", sanitizedDocType);
            Directory.CreateDirectory(folderPath);

            string fileName = $"{sop.SopNumber}_{sop.Title}.pdf";
            string fullPath = Path.Combine(folderPath, fileName);
            await System.IO.File.WriteAllBytesAsync(fullPath, pdfBytes);

            // 6. Clean up temp file
            if (System.IO.File.Exists(tempFooterPath))
                System.IO.File.Delete(tempFooterPath);

            // 7. Stream inline to browser (open in new tab)
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
            return File(pdfBytes, "application/pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF");
            return StatusCode(500, $"Error generating PDF: {ex.Message}");
        }
    }

    // method to handle file operations during migration
    private void DeleteFileFromBothLocations(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var basePath = _configuration["StorageSettings:BasePath"];

            // Delete from new storage location
            if (!string.IsNullOrEmpty(basePath))
            {
                var newPath = Path.Combine(basePath, filePath.TrimStart('/'));
                if (System.IO.File.Exists(newPath))
                {
                    System.IO.File.Delete(newPath);
                    _logger.LogInformation("Deleted file from new storage: {FilePath}", newPath);
                }
            }

            // Delete from legacy location
            var oldPath = Path.Combine(_hostingEnvironment.WebRootPath, filePath.TrimStart('/'));
            if (System.IO.File.Exists(oldPath))
            {
                System.IO.File.Delete(oldPath);
                _logger.LogInformation("Deleted file from legacy storage: {FilePath}", oldPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file: {FilePath}", filePath);
        }
    }

    private async Task<List<string>> GetAreasAsync()
    {
        var areas = new List<string>();
        string connStr = _configuration.GetConnectionString("entTTSAPConnection");

        try
        {
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                string sql = "SELECT assetname AS AreaName FROM asset WHERE udfbit5 = 1 AND isup = 1";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    await conn.OpenAsync();
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string areaName = reader["AreaName"]?.ToString();
                            if (!string.IsNullOrEmpty(areaName))
                            {
                                areas.Add(areaName);
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

    private async Task<List<string>> GetDocunentsAsync()
    {
        var documents = new List<string>();
        string connStr = _configuration.GetConnectionString("entTTSAPConnection");

        if (string.IsNullOrEmpty(connStr))
        {
            // Log warning or handle missing connection string
            return documents;
        }

        try
        {
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                string sql = @" SELECT BulletinName AS DocumentType 
                                  FROM Bulletin 
                                 WHERE BulletinName LIKE '%Work Instruction%' 
                                    OR BulletinName LIKE '%Procedures%' 
                                 ORDER BY BulletinName";

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
                                documents.Add(documentType);
                            }
                        }
                    }
                }
            }

            return documents;
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
            string sql = "SELECT DepartmentID, DepartmentName FROM Department where active = 1";
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
                            DepartmentName = reader["DepartmentName"]?.ToString()
                        });
                    }
                }
            }
        }

        return departments;
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var sop = await _context.StructuredSops.Include(s => s.Steps).FirstOrDefaultAsync(s => s.Id == id);
        if (sop == null) return NotFound();

        sop.Steps = sop.Steps.OrderBy(st => st.StepNumber).ToList();
        return View(sop);
    }



    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        // Remove the Status filter to allow editing of non-approved SOPs
        var sop = await _context.StructuredSops
            .Include(s => s.Steps)
            .FirstOrDefaultAsync(s => s.Id == id); // ← Remove the Status filter

        if (sop == null)
        {
            return NotFound();
        }

        
        var vm = new SopEditViewModel
        {
            Id = sop.Id,
            SopNumber = sop.SopNumber,
            Title = sop.Title,
            Revision = sop.Revision,
            EffectiveDate = sop.EffectiveDate,
            ControlledBy = sop.ControlledBy,
            DocType = sop.DocType,
            SelectedAreas = sop.Area?
                .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                .ToList() ?? new List<string>(),
            Steps = sop.Steps
                .OrderBy(s => s.StepNumber)
                .Select(s => new StepViewModel
                {
                    Id = s.Id,
                    StepNumber = s.StepNumber,
                    Instructions = s.Instructions,
                    KeyPoints = s.KeyPoints,
                    ExistingImagePath = s.ImagePath,
                    ExistingKeyPointImagePath = s.KeyPointImagePath
                }).ToList()
        };

        // Fetch dropdown data
        ViewData["Areas"] = await GetDistinctAreasAsync();
        ViewData["Department"] = await GetDepartmentsAsync();
        ViewData["Documents"] = await GetDistinctDocumentsAsync();
        ViewBag.UserDepartment = User?.Identity?.Name ?? "";

        return View(vm);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [FromForm] SopEditViewModel vm)
    {
        if (id != vm.Id)
            return NotFound();

        // ✅ Validate that at least one step has instructions
        if (vm.Steps == null || !vm.Steps.Any(s => !string.IsNullOrWhiteSpace(s.Instructions)))
        {
            ModelState.AddModelError("", "At least one step with instructions is required.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdownDataAsync();
            return View(vm);
        }

        try
        {
            var loggedInUser = User.FindFirst("LaborName")?.Value ?? "System";
            var email = User.FindFirst("Email")?.Value ?? "system@example.com";

            // ✅ Validate storage configuration
            var basePath = _configuration["StorageSettings:BasePath"];
            if (string.IsNullOrEmpty(basePath))
            {
                ModelState.AddModelError("", "Storage configuration is missing.");
                await PopulateDropdownDataAsync();
                return View(vm);
            }

            // ✅ Load SOP with its steps
            var sop = await _context.StructuredSops
                .Include(s => s.Steps)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sop == null)
                return NotFound();

            var historyLogs = new List<StructuredSopHistories>();

            void TrackChange(string propertyName, string oldValue, string newValue)
            {
                if (oldValue != newValue)
                {
                    historyLogs.Add(new StructuredSopHistories
                    {
                        SopId = sop.Id,
                        PropertyName = propertyName,
                        OldValue = oldValue,
                        NewValue = newValue,
                        ChangedBy = loggedInUser,
                        ChangedByEmail = email,
                        ChangedAt = DateTime.Now
                    });
                }
            }

            // ✅ Compare metadata and track changes
            TrackChange("Title", sop.Title, vm.Title);
            TrackChange("Revision", sop.Revision?.ToString(), vm.Revision?.ToString());
            TrackChange("EffectiveDate", sop.EffectiveDate.ToString("yyyy-MM-dd"), vm.EffectiveDate.ToString("yyyy-MM-dd"));
            TrackChange("ControlledBy", sop.ControlledBy, vm.ControlledBy);
            TrackChange("DocType", sop.DocType, vm.DocType);
            TrackChange("Area", sop.Area, vm.SelectedAreas != null ? string.Join(", ", vm.SelectedAreas) : "");

            // ✅ Update SOP metadata
            sop.Title = vm.Title;
            sop.Revision = vm.Revision;
            sop.EffectiveDate = vm.EffectiveDate;
            sop.ControlledBy = vm.ControlledBy;
            sop.DocType = vm.DocType;
            sop.Area = vm.SelectedAreas != null ? string.Join(", ", vm.SelectedAreas) : string.Empty;
            sop.Status = "Pending Approval";
            sop.ReviewStatus = "Pending";
            sop.ReviewedBy = null;
            sop.ApprovedBy = "Pending Approval";
            sop.CreatedDate = DateTime.Now;

            // ✅ Remove steps that no longer exist in the view model
            var stepIdsToKeep = vm.Steps.Where(s => s.Id > 0).Select(s => s.Id).ToList();
            var stepsToRemove = sop.Steps.Where(s => !stepIdsToKeep.Contains(s.Id)).ToList();

            foreach (var step in stepsToRemove)
            {
                if (!string.IsNullOrEmpty(step.ImagePath))
                {
                    foreach (var image in step.ImagePath.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        DeleteFileFromBothLocations(image);
                }

                if (!string.IsNullOrEmpty(step.KeyPointImagePath))
                    DeleteFileFromBothLocations(step.KeyPointImagePath);

                // ✅ Log removed steps
                _context.SopStepHistory.Add(new SopStepHistories
                {
                    SopId = sop.Id,
                    StepId = step.Id,
                    PropertyName = "StepRemoved",
                    OldValue = step.Instructions,
                    NewValue = null,
                    ChangedBy = loggedInUser,
                    ChangedByEmail = email,
                    ChangedAt = DateTime.Now
                });

                _context.SopSteps.Remove(step);
            }

            // ✅ Update existing and add new steps
            foreach (var stepVm in vm.Steps)
            {
                SopStep existingStep;

                if (stepVm.Id > 0)
                {
                    existingStep = sop.Steps.FirstOrDefault(s => s.Id == stepVm.Id);
                    if (existingStep == null)
                    {
                        ModelState.AddModelError("", $"Step with ID {stepVm.Id} not found.");
                        await PopulateDropdownDataAsync();
                        return View(vm);
                    }

                    void TrackStepChange(string propName, string oldVal, string newVal)
                    {
                        if (oldVal != newVal)
                        {
                            _context.SopStepHistory.Add(new SopStepHistories
                            {
                                StepId = existingStep.Id,
                                SopId = sop.Id,
                                PropertyName = propName,
                                OldValue = oldVal,
                                NewValue = newVal,
                                ChangedBy = loggedInUser,
                                ChangedByEmail = email,
                                ChangedAt = DateTime.Now
                            });
                        }
                    }

                    // Track old vs new values
                    TrackStepChange("Instructions", existingStep.Instructions, stepVm.Instructions);
                    TrackStepChange("KeyPoints", existingStep.KeyPoints, stepVm.KeyPoints);

                    // ✅ Update step content and images
                    existingStep.StepNumber = stepVm.StepNumber;
                    existingStep.Instructions = await ProcessHtmlContentWithImagesAsync(stepVm.Instructions, "StructuredSop/instructions", basePath);
                    existingStep.KeyPoints = await ProcessHtmlContentWithImagesAsync(stepVm.KeyPoints, "StructuredSop/keypoints", basePath);

                    await HandleStepImageChangesAsync(existingStep, stepVm, basePath, loggedInUser, email);

                }
                else
                {
                    // ✅ Add new step
                    var newStep = new SopStep
                    {
                        StepNumber = stepVm.StepNumber,
                        Instructions = await ProcessHtmlContentWithImagesAsync(stepVm.Instructions, "StructuredSop/instructions", basePath),
                        KeyPoints = await ProcessHtmlContentWithImagesAsync(stepVm.KeyPoints, "StructuredSop/keypoints", basePath),
                        ImagePath = await SaveImagesAsync(stepVm.StepImages, stepVm.StepImagesPaste, "StructuredSop/instructions", basePath),
                        KeyPointImagePath = await SaveSingleImageAsync(stepVm.KeyPointImage, stepVm.KeyPointImagePaste, "StructuredSop/keypoints", basePath)
                    };

                    sop.Steps.Add(newStep);

                    // ✅ Log new step creation
                    _context.SopStepHistory.Add(new SopStepHistories
                    {
                        SopId = sop.Id,
                        PropertyName = "StepAdded",
                        OldValue = null,
                        NewValue = newStep.Instructions,
                        ChangedBy = loggedInUser,
                        ChangedByEmail = email,
                        ChangedAt = DateTime.Now
                    });
                }
            }

            // ✅ Reorder steps properly
            var orderedSteps = sop.Steps.OrderBy(s => s.StepNumber).ToList();
            for (int i = 0; i < orderedSteps.Count; i++)
            {
                orderedSteps[i].StepNumber = i + 1;
            }

            // ✅ Generate updated PDF
            string pdfFileName = await GenerateAndSavePdfToOriginalsAsync(sop, basePath);

            // ✅ Save metadata change logs
            if (historyLogs.Any())
                await _context.StructuredSopHistory.AddRangeAsync(historyLogs);

            // ✅ Sync DocRegister and save
            await _docRegisterService.SyncStructuredSopAsync(sop, loggedInUser, email, pdfFileName, basePath);

            _context.StructuredSops.Update(sop);
            await _context.SaveChangesAsync();

            TempData["Success"] = "SOP updated successfully!";
            return RedirectToAction(nameof(Details), new { id = sop.Id });
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!StructuredSopExists(vm.Id))
                return NotFound();

            ModelState.AddModelError("", "The record you attempted to edit was modified by another user. Please refresh and try again.");
            await PopulateDropdownDataAsync();
            return View(vm);
        }
        catch (Exception ex)
        {
            // Log the full error with inner exception
            var fullError = ex.ToString();
            _logger.LogError(fullError, "Error updating SOP");

            ModelState.AddModelError("", $"Error updating SOP: {ex.Message}");
            if (ex.InnerException != null)
            {
                ModelState.AddModelError("", $"Details: {ex.InnerException.Message}");
            }

            await PopulateDropdownDataAsync();
            return View(vm);
        }
    }

    private async Task PopulateDropdownDataAsync()
    {
        ViewData["Areas"] = await GetDistinctAreasAsync();
        ViewData["Department"] = await GetDepartmentsAsync();
        ViewData["Documents"] = await GetDistinctDocumentsAsync();
        ViewBag.UserDepartment = User?.Identity?.Name ?? "";
    }

    private async Task HandleStepImageChangesAsync(SopStep existingStep, StepViewModel stepVm, string basePath, string changedBy, string changedByEmail)
    {
        // Handle Instruction Images
        await HandleInstructionImagesAsync(existingStep, stepVm, basePath, changedBy, changedByEmail);

        // Handle Key Point Images
        await HandleKeyPointImagesAsync(existingStep, stepVm, basePath, changedBy, changedByEmail);
    }

    private async Task HandleInstructionImagesAsync(SopStep existingStep, StepViewModel stepVm, string basePath, string changedBy, string changedByEmail)
    {
        var oldImagePath = existingStep.ImagePath;
        var imagePaths = existingStep.ImagePath?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();

        // 1️⃣ Handle deleted instruction images
        if (!string.IsNullOrEmpty(stepVm.DeletedImages))
        {
            var deletedImages = stepVm.DeletedImages.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var deletedImage in deletedImages)
            {
                DeleteFileFromBothLocations(deletedImage);
                imagePaths.Remove(deletedImage);

                _context.SopStepHistory.Add(new SopStepHistories
                {
                    StepId = existingStep.Id,
                    PropertyName = "Instruction Image Deleted",
                    OldValue = deletedImage,
                    NewValue = null,
                    ChangedBy = changedBy,
                    ChangedByEmail = changedByEmail,
                    ChangedAt = DateTime.Now
                });
            }
        }

        // 2️⃣ Handle new uploaded instruction images
        if (stepVm.StepImages != null && stepVm.StepImages.Any(f => f.Length > 0))
        {
            foreach (var img in stepVm.StepImages.Where(f => f.Length > 0))
            {
                var path = await SaveFileToNewStorageAsync(img, "StructuredSop/instructions", basePath);
                if (!string.IsNullOrEmpty(path))
                {
                    imagePaths.Add(path);

                    _context.SopStepHistory.Add(new SopStepHistories
                    {
                        StepId = existingStep.Id,
                        PropertyName = "Instruction Image Added (Upload)",
                        OldValue = null,
                        NewValue = path,
                        ChangedBy = changedBy,
                        ChangedByEmail = changedByEmail,
                        ChangedAt = DateTime.Now
                    });
                }
            }
        }

        // 3️⃣ Handle pasted base64 instruction images
        if (!string.IsNullOrEmpty(stepVm.StepImagesPaste))
        {
            var pastedImagePaths = await ExtractAndSavePastedImagesAsync(stepVm.StepImagesPaste, "StructuredSop/instructions", basePath);
            foreach (var pastedPath in pastedImagePaths)
            {
                imagePaths.Add(pastedPath);

                _context.SopStepHistory.Add(new SopStepHistories
                {
                    StepId = existingStep.Id,
                    PropertyName = "Instruction Image Added (Paste)",
                    OldValue = null,
                    NewValue = pastedPath,
                    ChangedBy = changedBy,
                    ChangedByEmail = changedByEmail,
                    ChangedAt = DateTime.Now
                });
            }
        }

        // 🔄 Update final list of instruction image paths
        existingStep.ImagePath = imagePaths.Any() ? string.Join(",", imagePaths) : null;

        // 🔍 Audit for instruction image path changes
        if (existingStep.ImagePath != oldImagePath)
        {
            _context.SopStepHistory.Add(new SopStepHistories
            {
                StepId = existingStep.Id,
                PropertyName = "Instruction ImagePath Updated",
                OldValue = oldImagePath,
                NewValue = existingStep.ImagePath,
                ChangedBy = changedBy,
                ChangedByEmail = changedByEmail,
                ChangedAt = DateTime.Now
            });
        }
    }

    private async Task HandleKeyPointImagesAsync(SopStep existingStep, StepViewModel stepVm, string basePath, string changedBy, string changedByEmail)
    {
        var oldKeyPointImagePath = existingStep.KeyPointImagePath;
        var keyPointImagePaths = existingStep.KeyPointImagePath?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();

        // 1️⃣ Handle deleted key point images
        if (!string.IsNullOrEmpty(stepVm.DeletedKeyPointImage))
        {
            var deletedImages = stepVm.DeletedKeyPointImage.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var deletedImage in deletedImages)
            {
                DeleteFileFromBothLocations(deletedImage);
                keyPointImagePaths.Remove(deletedImage);

                _context.SopStepHistory.Add(new SopStepHistories
                {
                    StepId = existingStep.Id,
                    PropertyName = "Key Point Image Deleted",
                    OldValue = deletedImage,
                    NewValue = null,
                    ChangedBy = changedBy,
                    ChangedByEmail = changedByEmail,
                    ChangedAt = DateTime.Now
                });
            }
        }

        // 2️⃣ Handle new uploaded key point images
        if (stepVm.KeyPointImage != null && stepVm.KeyPointImage.Length > 0)
        {
            var path = await SaveFileToNewStorageAsync(stepVm.KeyPointImage, "StructuredSop/keypoints", basePath);
            if (!string.IsNullOrEmpty(path))
            {
                keyPointImagePaths.Add(path);

                _context.SopStepHistory.Add(new SopStepHistories
                {
                    StepId = existingStep.Id,
                    PropertyName = "Key Point Image Added (Upload)",
                    OldValue = null,
                    NewValue = path,
                    ChangedBy = changedBy,
                    ChangedByEmail = changedByEmail,
                    ChangedAt = DateTime.Now
                });
            }
        }

        // 3️⃣ Handle pasted base64 key point images
        if (!string.IsNullOrEmpty(stepVm.KeyPointImagePaste))
        {
            var pastedImagePaths = await ExtractAndSavePastedImagesAsync(stepVm.KeyPointImagePaste, "StructuredSop/keypoints", basePath);
            foreach (var pastedPath in pastedImagePaths)
            {
                keyPointImagePaths.Add(pastedPath);

                _context.SopStepHistory.Add(new SopStepHistories
                {
                    StepId = existingStep.Id,
                    PropertyName = "Key Point Image Added (Paste)",
                    OldValue = null,
                    NewValue = pastedPath,
                    ChangedBy = changedBy,
                    ChangedByEmail = changedByEmail,
                    ChangedAt = DateTime.Now
                });
            }
        }

        // 🔄 Update final list of key point image paths
        existingStep.KeyPointImagePath = keyPointImagePaths.Any() ? string.Join(",", keyPointImagePaths) : null;

        // 🔍 Audit for key point image path changes
        if (existingStep.KeyPointImagePath != oldKeyPointImagePath)
        {
            _context.SopStepHistory.Add(new SopStepHistories
            {
                StepId = existingStep.Id,
                PropertyName = "Key Point ImagePath Updated",
                OldValue = oldKeyPointImagePath,
                NewValue = existingStep.KeyPointImagePath,
                ChangedBy = changedBy,
                ChangedByEmail = changedByEmail,
                ChangedAt = DateTime.Now
            });
        }
    }

    private async Task<string> SaveImagesAsync(IEnumerable<IFormFile> uploads, string pastedImages, string folder, string basePath)
    {
        var paths = new List<string>();

        if (uploads != null && uploads.Any(f => f.Length > 0))
        {
            foreach (var img in uploads)
            {
                var path = await SaveFileToNewStorageAsync(img, folder, basePath);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
        }

        if (!string.IsNullOrEmpty(pastedImages))
            paths.AddRange(await ExtractAndSavePastedImagesAsync(pastedImages, folder, basePath));

        return paths.Any() ? string.Join(",", paths) : null;
    }

    private async Task<string> SaveSingleImageAsync(IFormFile upload, string pasted, string folder, string basePath)
    {
        if (upload != null && upload.Length > 0)
            return await SaveFileToNewStorageAsync(upload, folder, basePath);

        if (!string.IsNullOrEmpty(pasted))
        {
            var paths = await ExtractAndSavePastedImagesAsync(pasted, folder, basePath);
            return paths.FirstOrDefault();
        }

        return null;
    }

    private async Task<string> ProcessHtmlContentWithImagesAsync(string htmlContent, string subfolder, string basePath)
    {
        if (string.IsNullOrEmpty(htmlContent))
            return htmlContent;

        // Extract base64 images and save them
        var processedHtml = await ExtractAndSaveBase64ImagesAsync(htmlContent, subfolder, basePath);
        return processedHtml;
    }

    private async Task<string> ExtractAndSaveBase64ImagesAsync(string htmlContent, string subfolder, string basePath)
    {
        if (string.IsNullOrEmpty(htmlContent))
            return htmlContent;

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes == null)
            return htmlContent;

        foreach (var imgNode in imgNodes)
        {
            var src = imgNode.GetAttributeValue("src", "");
            if (src.StartsWith("data:image"))
            {
                // Extract and save base64 image
                var savedPath = await SaveBase64ImageAsync(src, subfolder, basePath);
                if (!string.IsNullOrEmpty(savedPath))
                {
                    imgNode.SetAttributeValue("src", savedPath);
                }
            }
        }

        return doc.DocumentNode.OuterHtml;
    }

    private async Task<List<string>> ExtractAndSavePastedImagesAsync(string htmlContent, string subfolder, string basePath)
    {
        var savedPaths = new List<string>();

        if (string.IsNullOrEmpty(htmlContent))
            return savedPaths;

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes == null)
            return savedPaths;

        foreach (var imgNode in imgNodes)
        {
            var src = imgNode.GetAttributeValue("src", "");
            if (src.StartsWith("data:image"))
            {
                var savedPath = await SaveBase64ImageAsync(src, subfolder, basePath);
                if (!string.IsNullOrEmpty(savedPath))
                {
                    savedPaths.Add(savedPath);
                }
            }
        }

        return savedPaths;
    }

    private async Task<string> SaveBase64ImageAsync(string base64Data, string subfolder, string basePath)
    {
        try
        {
            // Extract the base64 string from the data URL
            var base64String = base64Data;
            if (base64String.Contains("base64,"))
            {
                base64String = base64String.Substring(base64String.IndexOf("base64,") + 7);
            }

            // Decode base64 string to byte array
            var imageBytes = Convert.FromBase64String(base64String);

            // Generate unique filename
            var extension = GetImageExtensionFromBase64(base64Data);
            var fileName = $"{Guid.NewGuid()}{extension}";
            var relativePath = Path.Combine(subfolder, fileName);
            var fullPath = Path.Combine(basePath, relativePath);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save the file
            await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes);

            // Return relative path for database storage
            return relativePath.Replace("\\", "/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving base64 image");
            return null;
        }
    }

    private string GetImageExtensionFromBase64(string base64Data)
    {
        if (base64Data.StartsWith("data:image/jpeg") || base64Data.StartsWith("data:image/jpg"))
            return ".jpg";
        if (base64Data.StartsWith("data:image/png"))
            return ".png";
        if (base64Data.StartsWith("data:image/gif"))
            return ".gif";
        if (base64Data.StartsWith("data:image/bmp"))
            return ".bmp";
        if (base64Data.StartsWith("data:image/webp"))
            return ".webp";

        return ".jpg"; // default extension
    }
    
    // Archive PDF from Originals/{DocType}/ folder
    private async Task ArchivePdfFileAsync(string fileName, string basePath, string docType)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName)) return;

            var archivePath = Path.Combine(basePath, "ArchivedPDFs");
            Directory.CreateDirectory(archivePath);

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var archivedFileName = $"{timestamp}_{fileName}";

            // Try to move from Originals/{DocType}/ location first
            string sanitizedDocType = string.Join("_", docType?.Split(Path.GetInvalidFileNameChars()) ?? new[] { "General" });
            var sourcePath = Path.Combine(basePath, "Originals", sanitizedDocType, fileName);
            var destPath = Path.Combine(archivePath, archivedFileName);

            if (System.IO.File.Exists(sourcePath))
            {
                System.IO.File.Move(sourcePath, destPath);
                _logger.LogInformation("Archived PDF from Originals: {FileName} -> {ArchivedName}", fileName, archivedFileName);
            }
            else
            {
                // Try legacy PDFs location
                var legacySourcePath = Path.Combine(basePath, "PDFs", fileName);
                if (System.IO.File.Exists(legacySourcePath))
                {
                    System.IO.File.Move(legacySourcePath, destPath);
                    _logger.LogInformation("Archived PDF from PDFs folder: {FileName} -> {ArchivedName}", fileName, archivedFileName);
                }
                else
                {
                    // Try old legacy location
                    var oldLegacyPath = Path.Combine(_hostingEnvironment.WebRootPath, "Documents", "Uploads", fileName);
                    if (System.IO.File.Exists(oldLegacyPath))
                    {
                        System.IO.File.Move(oldLegacyPath, destPath);
                        _logger.LogInformation("Archived PDF from legacy storage: {FileName} -> {ArchivedName}", fileName, archivedFileName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to archive PDF file: {FileName}", fileName);
        }
    }

    private bool StructuredSopExists(int id)
    {
        return _context.StructuredSops.Any(e => e.Id == id);
    }
    private async Task<IEnumerable<SelectListItem>> GetDistinctAreasAsync()
    {
        var areas = new List<SelectListItem>();
        string connStr = _configuration.GetConnectionString("entTTSAPConnection");

        try
        {
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                string sql = "SELECT assetname AS AreaName FROM asset WHERE udfbit5 = 1 AND isup = 1";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    await conn.OpenAsync();
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string areaName = reader["AreaName"]?.ToString();
                            if (!string.IsNullOrEmpty(areaName))
                            {
                                areas.Add(new SelectListItem
                                {
                                    Text = areaName,
                                    Value = areaName
                                });
                            }
                        }
                    }
                }
            }
            return areas;
        }
        catch (Exception)
        {
            return new List<SelectListItem>(); // Return empty list on error
        }
    }

    private async Task<IEnumerable<SelectListItem>> GetDistinctDocumentsAsync()
    {
        var documents = new List<SelectListItem>();
        var connStr = _configuration.GetConnectionString("entTTSAPConnection");

        try
        {
            using var conn = new SqlConnection(connStr);
            const string sql = "SELECT DISTINCT BulletinName AS DocumentType FROM Bulletin";
            using var cmd = new SqlCommand(sql, conn);
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var documentType = reader["DocumentType"]?.ToString();
                if (!string.IsNullOrEmpty(documentType))
                {
                    documents.Add(new SelectListItem
                    {
                        Text = documentType,
                        Value = documentType
                    });
                }
            }
        }
        catch
        {
            return new List<SelectListItem>();
        }

        return documents;
    }

    private async Task<IEnumerable<SelectListItem>> GetDistinctDepartmentsAsync()
    {
        var departments = new List<SelectListItem>();
        var connStr = _configuration.GetConnectionString("entTTSAPConnection");

        using var conn = new SqlConnection(connStr);
        const string sql = "SELECT DepartmentID, DepartmentName FROM Department WHERE active = 1";

        using var cmd = new SqlCommand(sql, conn);
        await conn.OpenAsync();

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            departments.Add(new SelectListItem
            {
                Value = reader["DepartmentID"].ToString(),
                Text = reader["DepartmentName"]?.ToString()
            });
        }

        return departments;
    }

    private async Task<string> GetNextRevisionNumber(string sopNumber, string title)
    {
        // Find the highest existing revision for this SOP number/title combination
        var highestRevision = await _context.StructuredSops
            .Where(s => s.SopNumber == sopNumber && s.Title == title)
            .OrderByDescending(s => s.Revision)
            .Select(s => s.Revision)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(highestRevision))
        {
            return "1"; // First revision
        }

        // If revision is numeric, increment it
        if (int.TryParse(highestRevision, out int numericRevision))
        {
            return (numericRevision + 1).ToString();
        }

        // If revision has letters (like "A", "B"), get next letter
        if (highestRevision.Length == 1 && char.IsLetter(highestRevision[0]))
        {
            char nextChar = (char)(highestRevision[0] + 1);
            return nextChar.ToString();
        }

        // Default fallback - append "-1" to existing revision
        return $"{highestRevision}-1";
    }


    [HttpPost]
    public async Task<IActionResult> DeleteConfirmed(string sopNumber, string reason)
    {
        // Server-side validation
        if (string.IsNullOrWhiteSpace(sopNumber))
        {
            TempData["Error"] = "SOP number is required.";
            return RedirectToAction("Details", new { id = sopNumber });
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "Reason for deletion is required.";
            return RedirectToAction("Details", new { id = sopNumber });
        }

        string deletedBy = User.FindFirst("LaborName")?.Value ?? "Unknown User";

        var success = await _docRegisterService.DeleteStructuredSopAsync(sopNumber, deletedBy, reason);

        if (!success)
        {
            TempData["Error"] = "SOP not found.";
            return RedirectToAction("Index");
        }

        TempData["Success"] = "Structured SOP deleted successfully.";
        return RedirectToAction("Index");
    }


}
