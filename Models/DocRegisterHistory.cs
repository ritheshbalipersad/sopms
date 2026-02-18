using System;
using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    public class DocRegisterHistory
    {
        public int Id { get; set; }

        [Required]
        public int DocRegisterId { get; set; }

        [Required]
        public string SopNumber { get; set; }

        [Required]
        public string OriginalFile { get; set; }

        [Required]
        public string FileName { get; set; }

        public string Department { get; set; }

        public string Revision { get; set; }

        public DateTime? EffectiveDate { get; set; }

        public DateTime? LastReviewDate { get; set; }

        public DateTime UploadDate { get; set; }

        public string Status { get; set; }

        public string DocumentType { get; set; }

        public string RevisedBy { get; set; }

        public DateTime RevisedOn { get; set; }
        public string ChangeDescription { get; set; }

        public virtual DocRegister DocRegister { get; set; }
    }
}
