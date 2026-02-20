using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;

namespace SOPMSApp.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AuditLogController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditLogController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string sopNumber = null, string actionType = null, string performedBy = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50)
        {
            IQueryable<DocumentAuditLog> query = _context.DocumentAuditLogs.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(sopNumber))
                query = query.Where(a => a.SopNumber.Contains(sopNumber.Trim()));
            if (!string.IsNullOrWhiteSpace(actionType))
                query = query.Where(a => a.Action == actionType.Trim());
            if (!string.IsNullOrWhiteSpace(performedBy))
                query = query.Where(a => a.PerformedBy.Contains(performedBy.Trim()));
            if (from.HasValue)
                query = query.Where(a => a.PerformedAtUtc >= from.Value.ToUniversalTime());
            if (to.HasValue)
            {
                var toEnd = to.Value.Date.AddDays(1).ToUniversalTime();
                query = query.Where(a => a.PerformedAtUtc < toEnd);
            }

            query = query.OrderByDescending(a => a.PerformedAtUtc);

            var total = await query.CountAsync();
            var list = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SopNumber = sopNumber;
            ViewBag.ActionType = actionType;
            ViewBag.PerformedBy = performedBy;
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Actions = await _context.DocumentAuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(x => x).ToListAsync();

            return View(list);
        }
    }
}
