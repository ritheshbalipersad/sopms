using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    public class StructuredSopHistories
    {
        [Key]
        public int Id { get; set; }

        public int? SopId { get; set; }  // Foreign key to StructuredSop
        public string? PropertyName { get; set; }  // e.g., "Title", "ControlledBy", "Revision"
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? ChangedBy { get; set; }
        public string? ChangedByEmail { get; set; }
        public DateTime? ChangedAt { get; set; }
    }


}
