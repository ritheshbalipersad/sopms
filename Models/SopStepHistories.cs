using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    public class SopStepHistories
    {
        [Key]
        public int Id { get; set; }

        public int? StepId { get; set; }  // Foreign key to SopStep
        public int? SopId { get; set; }   // Link back to parent SOP
        public string? PropertyName { get; set; }  // e.g., "Instructions", "ImagePath"
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? ChangedBy { get; set; }
        public string? ChangedByEmail { get; set; }
        public DateTime? ChangedAt { get; set; }
    }
}
