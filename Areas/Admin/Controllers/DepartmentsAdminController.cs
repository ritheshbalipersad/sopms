using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace SOPMSApp.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class DepartmentsAdminController : Controller
    {
        private readonly IConfiguration _configuration;

        public DepartmentsAdminController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnStr => _configuration.GetConnectionString("entTTSAPConnection") ?? "";

        public async Task<IActionResult> Index(bool? activeOnly = true)
        {
            var list = new List<DepartmentAdminItem>();
            if (string.IsNullOrEmpty(ConnStr)) return View(list);
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();
                var sql = "SELECT DepartmentID, DepartmentName, SupervisorName, ISNULL(active, 1) FROM department";
                if (activeOnly == true) sql += " WHERE ISNULL(active, 1) = 1";
                sql += " ORDER BY DepartmentName";
                using var cmd = new SqlCommand(sql, conn);
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new DepartmentAdminItem
                    {
                        DepartmentID = r["DepartmentID"]?.ToString() ?? "",
                        DepartmentName = r["DepartmentName"]?.ToString() ?? "",
                        SupervisorName = r["SupervisorName"]?.ToString() ?? "",
                        Active = r.GetInt32(3) == 1
                    });
                }
            }
            catch { /* table may not exist */ }
            return View(list);
        }

        public IActionResult Create()
        {
            return View(new DepartmentAdminItem { DepartmentID = "", DepartmentName = "", SupervisorName = "", Active = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DepartmentAdminItem model)
        {
            if (string.IsNullOrWhiteSpace(model.DepartmentID))
            {
                ModelState.AddModelError("DepartmentID", "Department ID is required.");
                return View(model);
            }
            if (string.IsNullOrWhiteSpace(model.DepartmentName))
            {
                ModelState.AddModelError("DepartmentName", "Department name is required.");
                return View(model);
            }
            model.DepartmentID = model.DepartmentID.Trim();
            model.DepartmentName = model.DepartmentName.Trim();
            model.SupervisorName = (model.SupervisorName ?? "").Trim();
            if (string.IsNullOrEmpty(ConnStr)) { ModelState.AddModelError("", "Database not configured."); return View(model); }
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();
                using var check = new SqlCommand("SELECT 1 FROM department WHERE DepartmentID = @id", conn);
                check.Parameters.AddWithValue("@id", model.DepartmentID);
                if (await check.ExecuteScalarAsync() != null)
                {
                    ModelState.AddModelError("DepartmentID", "A department with this ID already exists.");
                    return View(model);
                }
                using var cmd = new SqlCommand(
                    "INSERT INTO department (DepartmentID, DepartmentName, SupervisorName, active) VALUES (@id, @name, @sup, @active)", conn);
                cmd.Parameters.AddWithValue("@id", model.DepartmentID);
                cmd.Parameters.AddWithValue("@name", model.DepartmentName);
                cmd.Parameters.AddWithValue("@sup", (object?)model.SupervisorName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@active", model.Active ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
                TempData["Success"] = "Department created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error saving: " + ex.Message);
                return View(model);
            }
        }

        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(ConnStr) || string.IsNullOrEmpty(id)) return NotFound();
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "SELECT DepartmentID, DepartmentName, SupervisorName, ISNULL(active, 1) FROM department WHERE DepartmentID = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            return View(new DepartmentAdminItem
            {
                DepartmentID = r["DepartmentID"]?.ToString() ?? "",
                DepartmentName = r["DepartmentName"]?.ToString() ?? "",
                SupervisorName = r["SupervisorName"]?.ToString() ?? "",
                Active = r.GetInt32(3) == 1
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, DepartmentAdminItem model)
        {
            if (id != model.DepartmentID) return NotFound();
            if (string.IsNullOrWhiteSpace(model.DepartmentName))
            {
                ModelState.AddModelError("DepartmentName", "Department name is required.");
                return View(model);
            }
            model.DepartmentName = model.DepartmentName.Trim();
            model.SupervisorName = (model.SupervisorName ?? "").Trim();
            if (string.IsNullOrEmpty(ConnStr)) { ModelState.AddModelError("", "Database not configured."); return View(model); }
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "UPDATE department SET DepartmentName = @name, SupervisorName = @sup, active = @active WHERE DepartmentID = @id", conn);
                cmd.Parameters.AddWithValue("@name", model.DepartmentName);
                cmd.Parameters.AddWithValue("@sup", (object?)model.SupervisorName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@active", model.Active ? 1 : 0);
                cmd.Parameters.AddWithValue("@id", id);
                if (await cmd.ExecuteNonQueryAsync() == 0) return NotFound();
                TempData["Success"] = "Department updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error saving: " + ex.Message);
                return View(model);
            }
        }
    }

    public class DepartmentAdminItem
    {
        public string DepartmentID { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public string? SupervisorName { get; set; }
        public bool Active { get; set; } = true;
    }
}
