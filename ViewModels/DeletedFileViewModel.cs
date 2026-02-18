using SOPMSApp.Models;

namespace SOPMSApp.ViewModels
{
    public class DeletedFileViewModel
    {
        // Instead of containing the entire entity, use individual properties
        public int Id { get; set; }
        public string SOPNumber { get; set; }
        public string FileName { get; set; }
        public string OriginalFileName { get; set; }
        public int Revision { get; set; }  // Correct type
        public string RevisionDisplay => $"Rev {Revision}"; // Display version

        // Add other properties you need for the view
        public string DocType { get; set; }
        public string Department { get; set; }
        public string Area { get; set; }
        public string DeletedBy { get; set; }
        public DateTime DeletedOn { get; set; }
        public string Reason { get; set; }

        // File existence check
        public bool OriginalFileExists { get; set; }
        public bool PdfFileExists { get; set; }
        public string OriginalFilePath { get; set; }
        public string PdfFilePath { get; set; }

    }
}
