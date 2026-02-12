using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("learning_quiz_options")]
public class QuizOption : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required]
    [Column("quiz_question_id")]
    public int QuizQuestionId { get; set; }

    public QuizQuestion? Question { get; set; }

    [Required]
    [Column("text")]
    public string Text { get; set; } = "";

    [Column("is_correct")]
    public bool IsCorrect { get; set; }
}
