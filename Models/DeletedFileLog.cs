using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SOPMSApp.Models
{
    [Table("DeletedFileLogs")]
    public class DeletedFileLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // Use nvarchar(max) but with MaxLength for compatibility
        [Column(TypeName = "nvarchar(max)")]
        [MaxLength(100)] // Matches Server 26's nvarchar(100)
        public string SOPNumber { get; set; } = string.Empty;

        [Column(TypeName = "nvarchar(max)")]
        [MaxLength(255)] // Matches Server 26's nvarchar(255)
        public string FileName { get; set; } = string.Empty;

        [Column(TypeName = "nvarchar(max)")]
        [MaxLength(255)] // Matches Server 26's nvarchar(255)
        public string OriginalFileName { get; set; } = string.Empty;

        [Column(TypeName = "nvarchar(max)")]
        [MaxLength(100)] // Matches Server 26's nvarchar(100)
        public string DeletedBy { get; set; } = string.Empty;

        // CRITICAL: Use datetime for compatibility with both servers
        [Column(TypeName = "datetime")]
        public DateTime DeletedOn { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        [MaxLength(500)] // Matches Server 26's nvarchar(500)
        public string Reason { get; set; } = string.Empty;

        [Column(TypeName = "nvarchar(500)")]
        public string? UserEmail { get; set; }

        // Fixed length columns (same on both servers)
        [Column(TypeName = "nvarchar(100)")]
        public string? DocType { get; set; }

        [Column(TypeName = "nvarchar(100)")]
        public string? Department { get; set; }

        [Column(TypeName = "nvarchar(100)")]
        public string? Area { get; set; }

        [Column(TypeName = "nvarchar(50)")]
        public string? Revision { get; set; }

        [Column(TypeName = "nvarchar(100)")]
        public string? UniqueNumber { get; set; }

        [Column(TypeName = "nvarchar(100)")]
        public string? ContentType { get; set; }

        public long FileSize { get; set; }

        [Column(TypeName = "nvarchar(150)")]
        public string? Author { get; set; } = string.Empty;

        [Column(TypeName = "nvarchar(150)")]
        public string? DepartmentSupervisor { get; set; }

        [Column(TypeName = "nvarchar(150)")]
        public string? SupervisorEmail { get; set; }

        [Column(TypeName = "nvarchar(50)")]
        public string? Status { get; set; } = "Archived";

        [Column(TypeName = "datetime")]
        public DateTime? EffectiveDate { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? UploadDate { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? ArchivedOn { get; set; }

        public bool? WasApproved { get; set; }

        public int? OriginalDocRegisterId { get; set; }
    }
}