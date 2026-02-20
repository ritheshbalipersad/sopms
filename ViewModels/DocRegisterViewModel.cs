using SOPMSApp.Models;

namespace SOPMSApp.Models // or SOPMSApp.ViewModels if you prefer
{
    public class DocRegisterViewModel
    {
        public DocRegister Document { get; set; }
        public DocRegister Department { get; set; }
        public FileInfoResult FileInfo { get; set; }
        public string PdfUrl { get; set; }
        public string VideoUrl { get; set; }
        public string OtherFileUrl { get; set; }
        public string DownloadUrl { get; set; }
        /// <summary>URL to download the original uploaded file (from Originals folder).</summary>
        public string OriginalDownloadUrl { get; set; }
        public string StatusClass { get; set; }
    }

    public class FileInfoResult
    {
        public string DisplayName { get; set; }
        public bool IsPdf { get; set; }
        public bool IsVideo { get; set; }
        public string FileExtension { get; set; }
    }
}