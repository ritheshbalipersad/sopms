using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;
using SOPMSApp.ViewModels;
using System.Data;
using System.Diagnostics;
using System.Text;

namespace SOPMSApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConverter _converter;
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly entTTSAPDbContext _entContext;
        private readonly IConfiguration _configuration;
        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, IConfiguration configuration, entTTSAPDbContext entContext, IConverter converter)
        {
            _logger = logger;
            _context = context;
            _configuration = configuration;
            _entContext = entContext;
            _converter = converter;
        }

        public async Task<IActionResult> Index()
        {
            DateTime today = DateTime.Today;
            DateTime thirtyDaysAgo = today.AddDays(-30);

            // 1. Group documents by department and calculate counts (including reviewed in last 30 days)
            var rawData = await _context.DocRegisters
             .Where(d => d.Status == "Approved" && d.IsArchived != true)
             .GroupBy(d => d.Department)
             .Select(g => new
             {
                 Department = g.Key,
                 ActiveCount = g.Count(d => d.ReviewStatus == "Active"),
                 ExpiredCount = g.Count(d => d.ReviewStatus == "Expired"),
                 RenewCount = g.Count(d => d.ReviewStatus == "Renew"),
                 ReviewedLast30DaysCount = g.Count(d => d.LastReviewDate > thirtyDaysAgo),
                 TotalCount = g.Count()
             })
             .ToListAsync();


            // 2. Map to ViewModel items
            var departmentData = rawData.Select(g => new DepartmentSOPStatusItem
            {
                Department = g.Department,
                ActiveCount = g.ActiveCount,
                ExpiredCount = g.ExpiredCount,
                RenewCount = g.RenewCount,
                ReviewedLast30Days = g.ReviewedLast30DaysCount,
                TotalCount = g.TotalCount,
                ActivePercentage = g.TotalCount > 0 ? Math.Round((double)g.ActiveCount * 100 / g.TotalCount, 2) : 0,
                RenewPercentage = g.TotalCount > 0 ? Math.Round((double)g.RenewCount * 100 / g.TotalCount, 2) : 0,
                ExpiredPercentage = g.TotalCount > 0 ? Math.Round((double)g.ExpiredCount * 100 / g.TotalCount, 2) : 0,
                ReviewedLast30DaysPercentage = g.TotalCount > 0 ? Math.Round((double)g.ReviewedLast30DaysCount * 100 / g.TotalCount, 2) : 0,
                Compliance_Rate = g.TotalCount > 0 ? Math.Round((double)g.ActiveCount * 100 / g.TotalCount, 2) : 0
            }).ToList();

            // 3. Get distinct areas
            var areas = await GetDistinctAreasAsync();

            // 4. Pending / Expires in 30 Days depending on role
            int pendingCount = 0;
            DateTime next30Days = today.AddDays(30);

            if (User.IsInRole("Admin"))
            {
                pendingCount = await _context.DocRegisters
                    .CountAsync(d => d.IsArchived != true &&
                        (d.Status == "Pending Admin Approval" || d.Status == "Pending Approval"));
            }
            else if (User.IsInRole("Manager"))
            {
                pendingCount = await _context.DocRegisters
                    .CountAsync(d => d.IsArchived != true && d.Status == "Pending Approval");
            }
            else
            {
                // Regular users: documents expiring in next 30 days
                pendingCount = await _context.DocRegisters
                    .CountAsync(d => d.IsArchived != true && d.EffectiveDate >= today && d.EffectiveDate <= next30Days);
            }

            // 5. Total approved docs for compliance rate
            var totalDocs = await _context.DocRegisters
                .CountAsync(d => d.IsArchived != true && d.Status == "Approved");

            var totalActive = departmentData.Sum(d => d.ActiveCount);
            var totalExpired = departmentData.Sum(d => d.ExpiredCount);
            var totalRenew = departmentData.Sum(d => d.RenewCount);
            var totalReviewedLast30Days = departmentData.Sum(d => d.ReviewedLast30Days);

            var complianceRate = totalDocs > 0 ? (double)totalActive * 100 / totalDocs : 0;
            var departmentCount = departmentData.Count;

            // 6. Average review days
            var reviewDaysList = await _context.DocRegisters
               .Where(d => d.IsArchived != true && d.LastReviewDate != null)
               .Select(d => EF.Functions.DateDiffDay(d.EffectiveDate, d.LastReviewDate.Value))
               .ToListAsync();

            var averageReviewDays = reviewDaysList.Any() ? reviewDaysList.Average() : 0;

            // 7. Build the ViewModel
            var viewModel = new DepartmentSOPStatusViewModel
            {
                DepartmentData = departmentData,
                Areas = areas,
                PendingCount = pendingCount,
                ActiveSopCount = totalActive,
                DepartmentCount = departmentCount,
                ComplianceRate = Math.Round(complianceRate, 1),
                AverageReviewDays = Math.Round(averageReviewDays.GetValueOrDefault(), 1),
                TotalDocs = totalDocs,
                TotalExpired = totalExpired,
                TotalRenew = totalRenew,
                TotalReviewedLast30Days = totalReviewedLast30Days
            };

            return View(viewModel);
        }


      

        [HttpGet]
        public IActionResult GetPendingCount()
        {
            int count = 0;

            if (User.IsInRole("Admin"))
            {
                // Admin sees all pending approvals (both types)
                count = _context.DocRegisters.Count(d =>
                    d.IsArchived == false &&
                    (d.Status == "Pending Admin Approval" ||
                     d.Status == "Pending Approval"));
            }
            else if (User.IsInRole("Manager"))
            {
                // Manager sees only "Pending Approval"
                count = _context.DocRegisters.Count(d =>
                    d.IsArchived == false &&
                    d.Status == "Pending Approval");
            }
            else
            {
                // Regular users see documents expiring in the next 30 days
                DateTime today = DateTime.Today;
                DateTime next30Days = today.AddDays(30);

                count = _context.DocRegisters.Count(d =>
                    d.IsArchived == false && d.EffectiveDate >= today && d.EffectiveDate <= next30Days);
            }

            return Json(new { pending = count });
        }



        private async Task<List<string>> GetDistinctAreasAsync()
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
                return new List<string>(); 
            }
        }

        private async Task<List<string>> GetDepartmentAsync()
        {
            var department = new List<string>();
            string connStr = _configuration.GetConnectionString("entTTSAPConnection");

            try
            {
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
                                string deptName = reader["Department"]?.ToString();
                                if (!string.IsNullOrEmpty(deptName))
                                {
                                    department.Add(deptName);
                                }
                            }
                        }
                    }
                }
                return department;
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private async Task<List<string>> GetDocumentsAsync()
        {
            var documents = new List<string>();
            string connStr = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    string sql = "SELECT DocumentType FROM Documents";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        await conn.OpenAsync();
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string doctype = reader["Documents"]?.ToString();
                                if (!string.IsNullOrEmpty(doctype))
                                {
                                    documents.Add(doctype);
                                }
                            }
                        }
                    }
                }
                return documents;
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        public IActionResult Infor()
        {
            var reviewDaysQuery = _context.DocRegisters
                .Where(d => d.EffectiveDate != null 
                && d.EffectiveDate > DateTime.Today
                && d.IsArchived == false);

            var model = new DashboardStatsViewModel
            {
                ActiveSopCount = _context.DocRegisters
                    .Count(d => d.Status == "Approved" 
                    && d.ReviewStatus == "Active"
                    && d.IsArchived == false),

                DepartmentCount = _entContext.Department.Count(),

                ComplianceRate = CalculateComplianceRate(),

                AverageReviewDays = reviewDaysQuery.Any()
                    ? (int)reviewDaysQuery.Average(d => EF.Functions.DateDiffDay(d.EffectiveDate.Value, DateTime.Today))
                    : 0 // fallback default, to avoid nullable
            };

            return View(model);
        }

        private double CalculateComplianceRate()
        {
            // 1. Get the total number of "Approved" and "Active" SOPs
            var totalActive = _context.DocRegisters
                .Count(d => d.Status == "Approved" && d.IsArchived == false);

            // 2. Return 0% if no active SOPs
            if (totalActive == 0)
                return 0;

            // 3. Today's date
            var today = DateTime.Today;

            // 4. Get number of compliant SOPs (EffectiveDate is today or earlier)
            var compliantCount = _context.DocRegisters
                .Count(d => d.Status == "Approved"
                            && d.ReviewStatus == "Active"
                            && d.EffectiveDate.HasValue
                            && d.IsArchived == false);
                

            // 5. Calculate and return compliance rate as a percentage
            return (compliantCount / (double)totalActive) * 100;
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpGet]
        public IActionResult GetSopStatusData()
        {
            try
            {
                DateTime currentDate = DateTime.Today;
                DateTime last30Days = currentDate.AddDays(-30);

                var departmentData = _context.DocRegisters
                    .Where(d => d.Status == "Approved"
                        && d.EffectiveDate != null
                        && d.LastReviewDate != null
                        && d.IsArchived == false)
                    .GroupBy(d => d.Department)
                    .Select(g => new
                    {
                        Department = g.Key,
                        // First calculate reviewed
                        ReviewedLast30Days = g.Count(d => d.LastReviewDate >= last30Days),

                        // Then use it in Active count (subtract reviewed)
                        Active = g.Count(d => EF.Functions.DateDiffDay(currentDate, d.EffectiveDate) > 30)
                                 - g.Count(d => d.LastReviewDate >= last30Days),

                        Renew = g.Count(d => EF.Functions.DateDiffDay(currentDate, d.EffectiveDate) > 0 &&
                                             EF.Functions.DateDiffDay(currentDate, d.EffectiveDate) <= 30),
                        Expired = g.Count(d => EF.Functions.DateDiffDay(currentDate, d.EffectiveDate) <= 0)
                    })
                    .ToList();

                var overallData = new
                {
                    TotalActive = departmentData.Sum(x => x.Active),
                    TotalRenew = departmentData.Sum(x => x.Renew),
                    TotalExpired = departmentData.Sum(x => x.Expired),
                    TotalReviewedLast30Days = departmentData.Sum(x => x.ReviewedLast30Days)
                };

                return Json(new
                {
                    departmentData,
                    overallData
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR in GetSopStatusData: " + ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }
 

        public async Task<IActionResult> Download(int id)
        {
            var document = await _context.DocRegisters.FindAsync(id);
            if (document == null)
            {
                return NotFound("Document not found in the database.");
            }

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "upload", document.FileName);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("File not found on the server.");
            }

            var memory = new MemoryStream();
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await stream.CopyToAsync(memory);
            }

            memory.Position = 0;
            return File(memory, document.ContentType ?? "application/octet-stream", document.FileName);
        }


        public IActionResult PdfDiagnostics()
        {
            var result = new StringBuilder();
            result.AppendLine("PDF Service Diagnostics");
            result.AppendLine("========================");

            // Check architecture
            var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            result.AppendLine($"Server Architecture: {architecture}");
            result.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

            // Check possible DLL locations
            string[] possiblePaths =
            {
                Path.Combine(Directory.GetCurrentDirectory(), "runtimes", "win-x64", "native", "libwkhtmltox.dll"),
                Path.Combine(Directory.GetCurrentDirectory(), "runtimes", "win-x86", "native", "libwkhtmltox.dll"),
                Path.Combine(Directory.GetCurrentDirectory(), "DinkToPdf", "64bit", "libwkhtmltox.dll"),
                Path.Combine(Directory.GetCurrentDirectory(), "DinkToPdf", "32bit", "libwkhtmltox.dll"),
                Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "libwkhtmltox.dll"),
                Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x86", "native", "libwkhtmltox.dll"),
                Path.Combine(AppContext.BaseDirectory, "DinkToPdf", "64bit", "libwkhtmltox.dll"),
                Path.Combine(AppContext.BaseDirectory, "DinkToPdf", "32bit", "libwkhtmltox.dll")
            };

            result.AppendLine("\nChecking DLL locations:");
            foreach (var path in possiblePaths)
            {
                bool exists = System.IO.File.Exists(path);
                result.AppendLine($"{path} - {(exists ? "✅ FOUND" : "❌ MISSING")}");

                if (exists)
                {
                    try
                    {
                        var fileInfo = new FileInfo(path);
                        result.AppendLine($"  Size: {fileInfo.Length} bytes, Created: {fileInfo.CreationTime}");
                    }
                    catch (Exception ex)
                    {
                        result.AppendLine($"  Error accessing: {ex.Message}");
                    }
                }
            }

            // Check if service is registered
            var converter = HttpContext.RequestServices.GetService<IConverter>();
            result.AppendLine($"\nIConverter registered: {(converter != null ? "✅ YES" : "❌ NO")}");

            ViewBag.Diagnostics = result.ToString();
            return View();
        }



    }
}
