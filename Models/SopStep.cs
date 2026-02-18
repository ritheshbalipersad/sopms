namespace SOPMSApp.Models
{
    public class SopStep
    {
        public int Id { get; set; }
        public int StepNumber { get; set; }
        public string Instructions { get; set; }
        public string? KeyPoints { get; set; }
        public string? ImagePath { get; set; } // Comma-separated image paths
        public string? KeyPointImagePath { get; set; } // Single key point image path
        public int StructuredSopId { get; set; }

        // Navigation property - back to parent SOP
        public StructuredSop StructuredSop { get; set; }
    }
}
