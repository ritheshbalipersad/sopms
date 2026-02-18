using System.ComponentModel.DataAnnotations;

namespace SOPMSApp.Models
{
    public class users
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Username is required.")]
        public required string logon { get; set; }

        //[Required(ErrorMessage = "Password is required.")]
       // [DataType(DataType.Password)]
       public required string Password { get; set; } // In production, hash this

        [Required]
        public required string user_guid { get; set; } // e.g., "Admin" or "User"
        public required string Role { get; set; }
    }

}
