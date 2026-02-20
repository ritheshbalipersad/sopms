using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace SOPMSApp.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class DocumentTypesAdminController : Controller
    {
        private readonly IConfiguration _configuration;

        public DocumentTypesAdminController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnStr => _configuration.GetConnectionString("entTTSAPConnection") ?? "";

        public async Task<IActionResult> Index()
        {
            var list = new List<DocumentTypeItem>();
            if (string.IsNullOrEmpty(ConnStr)) return View(list);
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("SELECT BulletinID, BulletinName, UDFChar1 FROM Bulletin ORDER BY BulletinName", conn);
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new DocumentTypeItem
                    {
                        BulletinID = r.GetInt32(0),
                        BulletinName = r["BulletinName"]?.ToString() ?? "",
                        UDFChar1 = r["UDFChar1"]?.ToString()
                    });
                }
            }
            catch { /* table may not exist */ }
            return View(list);
        }

        public IActionResult Create()
        {
            return View(new DocumentTypeItem { BulletinName = "", UDFChar1 = "" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DocumentTypeItem model)
        {
            if (string.IsNullOrWhiteSpace(model.BulletinName))
            {
                ModelState.AddModelError("BulletinName", "Document type name is required.");
                return View(model);
            }
            model.BulletinName = model.BulletinName.Trim();
            model.UDFChar1 = (model.UDFChar1 ?? "").Trim();
            if (string.IsNullOrEmpty(ConnStr)) { ModelState.AddModelError("", "Database not configured."); return View(model); }
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();
                using var check = new SqlCommand("SELECT 1 FROM Bulletin WHERE BulletinName = @n", conn);
                check.Parameters.AddWithValue("@n", model.BulletinName);
                if (await check.ExecuteScalarAsync() != null)
                {
                    ModelState.AddModelError("BulletinName", "A document type with this name already exists.");
                    return View(model);
                }
                using var cmd = new SqlCommand("INSERT INTO Bulletin (BulletinName, UDFChar1) VALUES (@name, @acronym)", conn);
                cmd.Parameters.AddWithValue("@name", model.BulletinName);
                cmd.Parameters.AddWithValue("@acronym", (object?)model.UDFChar1 ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                TempData["Success"] = "Document type created successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error saving: " + ex.Message);
                return View(model);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            if (string.IsNullOrEmpty(ConnStr)) return NotFound();
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT BulletinID, BulletinName, UDFChar1 FROM Bulletin WHERE BulletinID = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            return View(new DocumentTypeItem
            {
                BulletinID = r.GetInt32(0),
                BulletinName = r["BulletinName"]?.ToString() ?? "",
                UDFChar1 = r["UDFChar1"]?.ToString()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DocumentTypeItem model)
        {
            if (id != model.BulletinID) return NotFound();
            if (string.IsNullOrWhiteSpace(model.BulletinName))
            {
                ModelState.AddModelError("BulletinName", "Document type name is required.");
                return View(model);
            }
            model.BulletinName = model.BulletinName.Trim();
            model.UDFChar1 = (model.UDFChar1 ?? "").Trim();
            if (string.IsNullOrEmpty(ConnStr)) { ModelState.AddModelError("", "Database not configured."); return View(model); }
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("UPDATE Bulletin SET BulletinName = @name, UDFChar1 = @acronym WHERE BulletinID = @id", conn);
                cmd.Parameters.AddWithValue("@name", model.BulletinName);
                cmd.Parameters.AddWithValue("@acronym", (object?)model.UDFChar1 ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                if (await cmd.ExecuteNonQueryAsync() == 0) return NotFound();
                TempData["Success"] = "Document type updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error saving: " + ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (string.IsNullOrEmpty(ConnStr)) return NotFound();
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("DELETE FROM Bulletin WHERE BulletinID = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                if (await cmd.ExecuteNonQueryAsync() == 0) return NotFound();
                TempData["Success"] = "Document type deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error deleting: " + ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }
    }

    public class DocumentTypeItem
    {
        public int BulletinID { get; set; }
        public string BulletinName { get; set; } = "";
        public string? UDFChar1 { get; set; }
    }
}
