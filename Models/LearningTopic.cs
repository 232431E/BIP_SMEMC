using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("learning_topics")]
public class LearningTopic : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required, MaxLength(80)]
    [Column("category")]
    public string Category { get; set; } = "";

    [Required, MaxLength(120)]
    [Column("title")]
    public string Title { get; set; } = "";

    [Required, MaxLength(300)]
    [Column("summary")]
    public string Summary { get; set; } = "";

    [Column("estimated_minutes")]
    public int EstimatedMinutes { get; set; }

    public List<LearningModule> Modules { get; set; } = new();
}
