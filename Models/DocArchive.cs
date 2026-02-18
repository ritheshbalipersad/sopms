using System;
using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    public class DocArchive
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string SopNumber { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; }

        [MaxLength(10)]
        public string Revision { get; set; }

        [MaxLength(255)]
        public string FileName { get; set; }

        [MaxLength(100)]
        public string ContentType { get; set; }

        [MaxLength(100)]
        public string Department { get; set; }

        [MaxLength(100)]
        public string Author { get; set; }
        public string? UserEmail { get; set; }  

        [MaxLength(100)]
        public string DocType { get; set; }

        [MaxLength(500)]
        public string Area { get; set; }

        public DateTime? EffectiveDate { get; set; }

        [Required]
        public DateTime ArchivedOn { get; set; }

        [MaxLength(100)]
        public string ArchivedBy { get; set; }

        // Optional: Metadata about source table
        [MaxLength(50)]
        public string SourceTable { get; set; }

        public int? SourceId { get; set; }
        public string Notes { get; set; }
    }
}
