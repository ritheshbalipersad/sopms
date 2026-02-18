using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using SOPMSApp.Data;
using SOPMSApp.Models;

namespace SOPMSApp.Controllers
{
    public class SopAreaController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SopAreaController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;

        public SopAreaController(ApplicationDbContext context, IConfiguration configuration, IWebHostEnvironment env, ILogger<SopAreaController> logger)
        {
            _logger = logger;
            _context = context;
            _env = env;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string area = "")
        {
            try
            {
                var areas = await GetDistinctAreasAsync();
                ViewData["Areas"] = areas;
                ViewBag.AreaList = areas;
                ViewBag.SelectedArea = area;

                if (string.IsNullOrWhiteSpace(area))
                {
                    return View(new List<DocRegister>());
                }

                var sopList = await _context.DocRegisters
                    .Where(d => d.Status == "Approved"
                        && d.IsArchived == false
                        
                        && ((d.Area ?? "").ToLower().Contains(area.ToLower()))
                        && (
                            (d.FileName ?? "").ToLower().Contains(".pdf")
                            || (d.OriginalFile ?? "").ToLower().Contains(".pdf")
                            || (d.OriginalFile ?? "").ToLower().Contains(".mp4")
                            || (d.VideoPath != null)

                        ))
                    .ToListAsync();

                return View(sopList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
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
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var areaName = reader["AreaName"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(areaName))
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
