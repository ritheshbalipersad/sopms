using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SOPMSApp.Models
{
    public class DocRegister
    {
        
       
        [Key]
        public int Id { get; set; }


        // 🔹 Identification 
        public required string SopNumber { get; set; }
        public string? DocumentType { get; set; } // Description
        public required string uniqueNumber { get; set; }
        public string DocType { get; set; }
        public string Department { get; set; }
        public string? Area { get; set; }
        public string Revision { get; set; }


        // 🔹 File Information
        public string FileName { get; set; }
        public string OriginalFile { get; set; }
        public string? DocumentPath { get; set; }
        public string? VideoPath { get; set; }
        public string ContentType { get; set; }
        public long FileSize { get; set; }


        // 🔹 Authoring / ownership
        public string Author { get; set; }
        public string? UserEmail { get; set; }
        public string? DepartmentSupervisor { get; set; }
        public string? SupervisorEmail { get; set; }


        // 🔹 Administrative metadata
        public string? Changed { get; set; }
        public string Status { get; set; } = "Pending Approval";
        public string? ReviewStatus { get; set; }
        public string? ApprovalStage { get; set; }
        public bool? ManagerApproved { get; set; }
        public DateTime? ManagerApprovedDate { get; set; }
        public bool? AdminApproved { get; set; }
        public DateTime? AdminApprovedDate { get; set; }
        public string? ApprovedBy { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime UploadDate { get; set; } = DateTime.Now;
        [DataType(DataType.Date)]
        public DateTime? LastReviewDate { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? ReturnedDate { get; set; }


        // 🔹 Deletion & Archival info
        public string? DeletionReason { get; set; }
        public string? DeletionRequestedBy { get; set; }
        public DateTime? DeletionRequestedOn { get; set; }
        public bool? IsArchived { get; set; } = false;
        public DateTime? ArchivedOn { get; set; }


        // 🔹 Relationships
        public bool? IsStructured { get; set; }
        public int? StructuredSopId { get; set; }
        public ICollection<StructuredSop> StructuredSops { get; set; } = new List<StructuredSop>();
    }
}
