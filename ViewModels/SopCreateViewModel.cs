using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering; // For SelectList if needed
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.ViewModels
{
    public class SopCreateViewModel
    {
        private List<string> selectedAreas = new List<string>();

        [Required(ErrorMessage = "SOP Number is required")]
        [Display(Name = "SOP Number")]
        public string SopNumber { get; set; }

        [Required(ErrorMessage = "Title is required.")]
        [RegularExpression(@"^[a-zA-Z0-9\s\-]+$", ErrorMessage = "Title can only contain letters, numbers, spaces, and hyphens.")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Revision is required")]
        public string Revision { get; set; }

        [Required(ErrorMessage = "Effective Date is required")]
        [Display(Name = "Effective Date")]
        [DataType(DataType.Date)]
        public DateTime EffectiveDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Controlled By is required")]
        [Display(Name = "Controlled By")]
        public string ControlledBy { get; set; }

        public string? ApprovedBy { get; set; }

        public string? Signatures { get; set; }

        [Required(ErrorMessage = "Document Type is required")]
        [Display(Name = "Document Type")]
        public string DocType { get; set; }

        [Required(ErrorMessage = "Please select at least one area.")]
        [Display(Name = "Applicable Areas")]
        public List<string> SelectedAreas { get; set; } = new List<string>();

        public string? Status { get; set; }

       
        [MinLength(1, ErrorMessage = "At least one step is required")]
        public List<SopStepViewModel> Steps { get; set; } = new List<SopStepViewModel>();
        
        // Properties for dropdown options (not posted back)
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public List<SelectListItem> AreaOptions { get; set; } = new List<SelectListItem>();

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public List<SelectListItem> DocumentTypeOptions { get; set; } = new List<SelectListItem>();
    }

    public class SopStepViewModel
    {
        public int StepNumber { get; set; }


        // Text content
        public string Instructions { get; set; }
        public string KeyPoints { get; set; }


        // File uploads
        public List<IFormFile> StepImages { get; set; }
        public IFormFile KeyPointImage { get; set; }


        // Add these for pasted images
        public string StepImagesPaste { get; set; }
        public string KeyPointImagePaste { get; set; }
        public string PastedImages { get; set; }
    }
}