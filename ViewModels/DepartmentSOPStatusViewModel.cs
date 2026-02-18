using MathNet.Numerics.RootFinding;

namespace SOPMSApp.ViewModels
{
    public class DepartmentSOPStatusItem
    {
        public string? Department { get; set; }

        public int ActiveCount { get; set; }
        public int RenewCount { get; set; }
        public int ExpiredCount { get; set; }
        public int TotalCount { get; set; }
        public int ReviewedLast30Days { get; set; }
        public double ActivePercentage { get; set; }
        public double RenewPercentage { get; set; }
        public double ExpiredPercentage { get; set; }
        public double Compliance_Rate { get; set; }
        public double ReviewedLast30DaysPercentage { get; set; }
    }
    public class Classification
    {
        public string? Area { get; set; }
        public string? Department { get; set; }
        public string? DocumentType { get; set; }
    }

    public class DepartmentSOPStatusViewModel
    {
        public List<DepartmentSOPStatusItem> DepartmentData { get; set; }
        public List<string> Areas { get; set; }
        public int PendingCount { get; set; }
        public int TotalExpired { get; set; }
        public int ActiveSopCount { get; set; }
        public int DepartmentCount { get; set; }
        public double ComplianceRate { get; set; }
        public double AverageReviewDays { get; set; }
        public int TotalReviewedLast30Days { get; set; } 
        public int TotalDocs { get; set; }
        public int TotalRenew { get; set; }
    }


}
