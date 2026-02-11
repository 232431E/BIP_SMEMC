using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Table("ai_responses")]
    public class AIResponseModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("user_id")]
        public string? UserId { get; set; }

        [Column("feature_type")]
        public string FeatureType { get; set; }

        [Column("response_text")]
        public string ResponseText { get; set; }

        // Added to fix CS0117 error
        [Column("justification")]
        public string? Justification { get; set; }

        // Added to fix CS0117 error
        [Column("version_tag")]
        public string? VersionTag { get; set; }

        [Column("date_key")]
        public DateTime DateKey { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}