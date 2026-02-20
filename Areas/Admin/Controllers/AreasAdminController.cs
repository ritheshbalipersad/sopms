using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;

namespace SOPMSApp.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AreasAdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AreasAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await _context.Areas.OrderBy(a => a.AreaName).ToListAsync();
            return View(list);
        }

        public IActionResult Create()
        {
            return View(new Area { AreaName = "" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Area model)
        {
            if (string.IsNullOrWhiteSpace(model.AreaName))
            {
                ModelState.AddModelError("AreaName", "Area name is required.");
                return View(model);
            }
            model.AreaName = model.AreaName.Trim();
            if (await _context.Areas.AnyAsync(a => a.AreaName == model.AreaName))
            {
                ModelState.AddModelError("AreaName", "An area with this name already exists.");
                return View(model);
            }
            _context.Areas.Add(model);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Area created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var area = await _context.Areas.FindAsync(id);
            if (area == null) return NotFound();
            return View(area);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Area model)
        {
            if (id != model.Id) return NotFound();
            if (string.IsNullOrWhiteSpace(model.AreaName))
            {
                ModelState.AddModelError("AreaName", "Area name is required.");
                return View(model);
            }
            model.AreaName = model.AreaName.Trim();
            if (await _context.Areas.AnyAsync(a => a.AreaName == model.AreaName && a.Id != id))
            {
                ModelState.AddModelError("AreaName", "An area with this name already exists.");
                return View(model);
            }
            var area = await _context.Areas.FindAsync(id);
            if (area == null) return NotFound();
            area.AreaName = model.AreaName;
            _context.Areas.Update(area);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Area updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var area = await _context.Areas.FindAsync(id);
            if (area == null) return NotFound();
            _context.Areas.Remove(area);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Area deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
