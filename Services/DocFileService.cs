using Microsoft.AspNetCore.Hosting;
using SOPMSApp.Models;
using System.IO;

namespace SOPMSApp.Services
{
    public class DocFileService
    {
        private readonly IWebHostEnvironment _env;

        public DocFileService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public FileInfoResult GetFileInfo(DocRegister doc)
        {
            var result = new FileInfoResult
            {
                DisplayName = !string.IsNullOrWhiteSpace(doc.FileName) && doc.FileName != "N/A"
                    ? doc.FileName
                    : doc.OriginalFile
            };

            if (!string.IsNullOrWhiteSpace(result.DisplayName))
            {
                string extension = Path.GetExtension(result.DisplayName)?.ToLower();
                result.IsPdf = extension == ".pdf";
                result.IsVideo = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" }
                    .Contains(extension);

                result.Path = result.IsVideo
                    ? $"/videos/{result.DisplayName}"
                    : FindDocumentPath(result.DisplayName);
            }

            return result;
        }

        private string FindDocumentPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            try
            {
                var safeFileName = Path.GetFileName(fileName);
                var searchPaths = new[]
                {
                    Path.Combine(_env.WebRootPath, "Upload", safeFileName),
                    Path.Combine(_env.WebRootPath, "Originals", safeFileName)
                };

                foreach (var path in searchPaths)
                {
                    if (File.Exists(path))
                    {
                        return path.Replace(_env.WebRootPath, "").Replace("\\", "/");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return null;
        }

        public string GetStatusClass(string status)
        {
            return status switch
            {
                "Active" => "text-success",
                "Renew" => "text-warning",
                "Expired" => "text-danger",
                _ => "text-secondary"
            };
        }
    }

    public class FileInfoResult
    {
        public string Path { get; set; }
        public bool IsPdf { get; set; }
        public bool IsVideo { get; set; }
        public string DisplayName { get; set; }
    }
}
