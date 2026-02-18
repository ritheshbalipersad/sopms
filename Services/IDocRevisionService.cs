using Microsoft.AspNetCore.Http;
using SOPMSApp.Models;

namespace SOPMSApp.Services
{
    public interface IDocRevisionService
    {
        Task<(bool success, string message)> ReviseDocumentAsync(DocRegister sop, IFormFile revisedOriginal, IFormFile? revisedPdf, DateTime effectiveDate, string documentType);
    }
}
