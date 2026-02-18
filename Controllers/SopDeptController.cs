using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using SOPMSApp.Data;
using SOPMSApp.Models;

namespace SOPMSApp.Controllers
{
    public class SopDeptController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SopDeptController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public SopDeptController(ApplicationDbContext context, IConfiguration configuration, IWebHostEnvironment env, ILogger<SopDeptController> logger)
        {
            _context = context;
            _configuration = configuration;
            _env = env;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string department = "")
        {
            try
            {
                var departments = await GetDistinctDeptAsync();
                ViewData["Departments"] = departments;
                ViewBag.DepartmentList = departments;
                ViewBag.SelectedDepartment = department;

                if (string.IsNullOrWhiteSpace(department))
                {
                    return View(new List<DocRegister>());
                }

                var sopList = await _context.DocRegisters
                    .Where(d => d.Status == "Approved"
                        && d.IsArchived == false
                        && ((d.Department ?? "").ToLower().Contains(department.ToLower()))
                        && (
                            (d.FileName ?? "").ToLower().Contains(".pdf")
                            || (d.OriginalFile ?? "").ToLower().Contains(".pdf")
                            || (d.OriginalFile ?? "").ToLower().Contains(".mp4")
                        ))
                    .ToListAsync();

                return View(sopList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        private async Task<List<string>> GetDistinctDeptAsync()
        {
            var departments = new List<string>();
            string connStr = _configuration.GetConnectionString("entTTSAPConnection");

            try
            {
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    string sql = "SELECT DepartmentName FROM Department WHERE Active = 1";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        await conn.OpenAsync();
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var departmentName = reader["DepartmentName"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(departmentName))
                                {
                                    departments.Add(departmentName);
                                }
                            }
                        }
                    }
                }

                return departments;
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        [HttpGet]
        public IActionResult View(int id)
        {
            try
            {
                var doc = _context.DocRegisters
                .FirstOrDefault(d => d.Id == id && d.IsArchived == false && d.Status == "Approved");

                if (doc == null)
                {
                    return NotFound();
                }

                return View(doc);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error loading document {Id} for viewing", id);
                TempData["Error"] = "An error occurred while loading the document.";
                return RedirectToAction(nameof(Index));
            }

        }
    }
}
