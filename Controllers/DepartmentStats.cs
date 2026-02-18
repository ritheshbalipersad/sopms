internal class DepartmentStats
{
    public string Department { get; set; }
    public int ActiveCount { get; set; }
    public int RenewCount { get; set; }
    public int ExpiredCount { get; set; }
    public double ActivePercentage { get; set; }
    public double RenewPercentage { get; set; }
    public double ExpiredPercentage { get; set; }
}