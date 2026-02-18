using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SOPMSApp.Models
{
    public class StructuredSop
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "SOP Number is required")]
        [StringLength(50, ErrorMessage = "SOP Number cannot exceed 50 characters")]
        [Display(Name = "SOP Number")]
        public string SopNumber { get; set; }

        [Required(ErrorMessage = "Title is required")]
        [StringLength(500, ErrorMessage = "Title cannot exceed 500 characters")]
        public string Title { get; set; }

        // 🔗 Sync link to DocRegisters
        [ForeignKey("DocRegister")]
        public int? DocRegisterId { get; set; }
        public DocRegister? DocRegister { get; set; }

        [Required]
        [StringLength(10)]
        public string Revision { get; set; } = "0";

        [Required]
        [DataType(DataType.Date)]
        public DateTime EffectiveDate { get; set; }

        [Required]
        public string ControlledBy { get; set; }

        public string? ApprovedBy { get; set; }

        public string? UserEmail { get; set; }

        public string? DepartmentSupervisor { get; set; }
        public string? SupervisorEmail { get; set; }

        public string Signatures { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Required]
        public string DocType { get; set; }

        public string Status { get; set; } = "Draft";
        public string? ReviewStatus { get; set; }
        public string? Area { get; set; }
        public string? ReviewedBy { get; set; } = "Pending";

        // 🧩 Structured content
        public List<SopStep> Steps { get; set; } = new();

        // ⏱️ Lifecycle / audit
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ArchivedOn { get; set; }
        public DateTime? ReturnedDate { get; set; }
        public string? RejectionReason { get; set; }
        public string? ApprovalStage { get; set; }

        public DateTime? ManagerApprovedDate { get; set; }
        public bool? ManagerApproved { get; set; }
        public DateTime? AdminApprovedDate { get; set; }
        public bool? AdminApproved { get; set; }

        // 🆕 New fields for better sync & clarity
        public bool? IsSyncedToDocRegister { get; set; } = false;
        public DateTime? SyncedDate { get; set; }
    }
}
