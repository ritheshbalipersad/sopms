using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    public class Documents
    {
        [Key]
        public int Id { get; set; }

        [Required]
        
        public string? BulletinName { get; set; }
    }
}
