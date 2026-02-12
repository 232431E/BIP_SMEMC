using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("learning_modules")]
public class LearningModule : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required]
    [Column("topic_id")]
    public int TopicId { get; set; }

    public LearningTopic? Topic { get; set; }

    [Required]
    [Column("difficulty")]
    public LearningDifficulty Difficulty { get; set; }

    [Required, MaxLength(140)]
    [Column("title")]
    public string Title { get; set; } = "";

    public List<LearningSection> Sections { get; set; } = new();

    public List<QuizQuestion> QuizQuestions { get; set; } = new();
}
