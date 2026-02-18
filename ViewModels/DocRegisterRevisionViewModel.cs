//using Spire.Presentation;

namespace SOPMSApp.ViewModels
{
    public class DocRegisterRevisionViewModel
    {
        public int Id { get; set; }
        public string SopNumber { get; set; }
        public string uniqueNumber { get; set; }
        public string FileName { get; set; }
        public string DocType { get; set; }
        public string OriginalFile { get; set; }
        public string Revision { get; set; }
        public string Department { get; set; }
        public string DocumentType { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public string Author { get; set; }
        public string FilePath { get; set; }
        public string ChangeDescription { get; set; }
        public string VideoPath { get; set; }
        public IFormFile RevisedOriginalFile { get; set; }
        public IFormFile RevisedPdfFile { get; set; }


        public string PdfUrl
        {
            get
            {
                if (string.IsNullOrEmpty(FileName) && string.IsNullOrEmpty(OriginalFile))
                    return string.Empty;

                var fileName = !string.IsNullOrEmpty(FileName) && FileName.ToLower() != "n/a"
                    ? FileName
                    : OriginalFile;

                if (string.IsNullOrEmpty(fileName))
                    return string.Empty;

                var safeFileName = Uri.EscapeDataString(Path.GetFileName(fileName));
                var safeDocType = Uri.EscapeDataString(DocType ?? "");

                return $"/FileAccess/GetPdf?fileName={safeFileName}&docType={safeDocType}";
            }
        }

        public string VideoUrl => !string.IsNullOrEmpty(VideoPath)
            ? $"/FileAccess/GetVideo?videoPath={Uri.EscapeDataString(VideoPath)}"
            : string.Empty;

    }

}
