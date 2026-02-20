using System;
using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    /// <summary>
    /// Audit trail for document lifecycle events (upload, approve, delete, archive, etc.) in DocRet.
    /// </summary>
    public class DocumentAuditLog
    {
        public int Id { get; set; }

        /// <summary>DocRegister.Id when the event occurred (may be null if document was later deleted).</summary>
        public int? DocRegisterId { get; set; }

        [Required]
        [MaxLength(100)]
        public string SopNumber { get; set; } = "";

        /// <summary>Event type: Uploaded, Approved, Deleted, Archived, PendingApproval, ReturnedForReview, Revised, etc.</summary>
        [Required]
        [MaxLength(64)]
        public string Action { get; set; } = "";

        [Required]
        [MaxLength(256)]
        public string PerformedBy { get; set; } = "";

        public DateTime PerformedAtUtc { get; set; }

        [MaxLength(2000)]
        public string? Details { get; set; }

        /// <summary>Optional: document title/file name at time of event.</summary>
        [MaxLength(500)]
        public string? DocumentTitle { get; set; }
    }
}
