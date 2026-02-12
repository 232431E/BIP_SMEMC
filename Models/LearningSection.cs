using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("learning_sections")]
public class LearningSection : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required]
    [Column("module_id")]
    public int ModuleId { get; set; }

    public LearningModule? Module { get; set; }

    [Column("order")]
    public int Order { get; set; }

    [Required, MaxLength(120)]
    [Column("heading")]
    public string Heading { get; set; } = "";

    [Required]
    [Column("body")]
    public string Body { get; set; } = "";

    [Column("video_url")]
    public string? VideoUrl { get; set; }

    [Column("image_url")]
    public string? ImageUrl { get; set; }
}
