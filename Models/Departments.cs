using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    public class DepartmentModel
    {
        [Key]
        public string DepartmentID { get; set; }
        public required string DepartmentName { get; set; }
        public string SupervisorName { get; set; }
    }
}
