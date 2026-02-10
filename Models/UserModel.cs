using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Table("users")] // Maps this class to your 'users' table in Supabase
    public class UserModel : BaseModel
    {
        [PrimaryKey("email", false)] // Matches 'email TEXT PRIMARY KEY'
        public string Email { get; set; }

        [Column("password_hash")] // Required by your SQL schema
        public string PasswordHash { get; set; } = "temporary_hash"; // Placeholder for dummy users

        [Column("full_name")]
        public string FullName { get; set; }

        [Column("industries")]
        public List<string> Industries { get; set; } = new();

        [Column("region")] // Note: Mapping singular column to plural property
        public List<string> Regions { get; set; } = new();

        [Column("role")]
        public string Role { get; set; } = "SME Owner";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}