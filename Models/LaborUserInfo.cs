using Microsoft.EntityFrameworkCore;

namespace SOPMSApp.Models
{
    [Keyless]
    public class LaborUserInfo
    {
        
        public string UserGuid { get; set; }
        public string LaborID { get; set; }
        public string LaborName { get; set; }
        public string AccessGroupID { get; set; }
        public string Role { get; set; }
        public string Email { get; set; }
        public string DepartmentID { get; set; }
        public string? Craft { get; set; }
       
    }
}
