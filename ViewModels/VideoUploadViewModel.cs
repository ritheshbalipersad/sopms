using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;

namespace SOPMSApp.Models.ViewModels
{
    public class VideoUploadViewModel
    {
        public string SopNumber { get; set; }
        public IFormFile VideoFile { get; set; }

        public string DocumentType { get; set; }
        public string Department { get; set; }
        public string DocType { get; set; } // Description
        public string Revision { get; set; }
        public DateTime? LastReviewDate { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public string[] Area { get; set; }

        public IEnumerable<SelectListItem>? ExistingSops { get; set; }
        public List<SelectListItem> Departments { get; set; } = new();
        public List<SelectListItem> Documents { get; set; } = new();
        public List<string> Areas { get; set; } = new();


        public Dictionary<string, ExistingSopDetail> ExistingSopDetails { get; set; } = new();


        public class ExistingSopDetail
        {
            public string SopNumber { get; set; }
            public string Department { get; set; }
            public string DocumentType { get; set; }  // Rename from DocType for clarity
            public string Description { get; set; }   // Rename from DocumentType for clarity
            public string Revision { get; set; }
            public DateTime? LastReviewDate { get; set; }
            public DateTime? EffectiveDate { get; set; }
            public List<string> Areas { get; set; } = new();
        }
    }
}
