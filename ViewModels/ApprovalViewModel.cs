using SOPMSApp.Models;

namespace SOPMSApp.ViewModels
{
    public class ApprovalViewModel
    {
        public List<DocRegister> PendingApprovals { get; set; } = new List<DocRegister>();
        public List<StructuredSop> PendingSopApprovals { get; set; } = new List<StructuredSop>();
        public List<DocRegister> PendingDeletions { get; set; } = new List<DocRegister>();
    }
}
