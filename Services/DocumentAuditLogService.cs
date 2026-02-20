using Microsoft.EntityFrameworkCore;
using SOPMSApp.Data;
using SOPMSApp.Models;

namespace SOPMSApp.Services
{
    public interface IDocumentAuditLogService
    {
        Task LogAsync(int? docRegisterId, string sopNumber, string action, string performedBy, string? details = null, string? documentTitle = null);
    }

    public class DocumentAuditLogService : IDocumentAuditLogService
    {
        private readonly ApplicationDbContext _context;

        public DocumentAuditLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(int? docRegisterId, string sopNumber, string action, string performedBy, string? details = null, string? documentTitle = null)
        {
            if (string.IsNullOrWhiteSpace(sopNumber)) sopNumber = "?";
            if (string.IsNullOrWhiteSpace(performedBy)) performedBy = "System";

            var log = new DocumentAuditLog
            {
                DocRegisterId = docRegisterId,
                SopNumber = sopNumber.Trim(),
                Action = action.Trim(),
                PerformedBy = performedBy.Trim(),
                PerformedAtUtc = DateTime.UtcNow,
                Details = details != null && details.Length > 2000 ? details.Substring(0, 2000) : details,
                DocumentTitle = documentTitle != null && documentTitle.Length > 500 ? documentTitle.Substring(0, 500) : documentTitle
            };
            _context.DocumentAuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
