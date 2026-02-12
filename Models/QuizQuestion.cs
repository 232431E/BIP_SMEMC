using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("learning_quiz_questions")]
public class QuizQuestion : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required]
    [Column("learning_module_id")]
    public int LearningModuleId { get; set; }

    public LearningModule? Module { get; set; }

    [Required]
    [Column("question")]
    public string Question { get; set; } = "";

    public List<QuizOption> Options { get; set; } = new();
}
