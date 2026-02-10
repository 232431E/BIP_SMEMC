using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    // This attribute tells Supabase which table to link to
    [Table("ai_responses")]
    public class AIResponseModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("feature_type")]
        public string FeatureType { get; set; }

        [Column("response_text")]
        public string ResponseText { get; set; }

        [Column("justification")]
        public string Justification { get; set; }

        [Column("version_tag")]
        public string VersionTag { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}