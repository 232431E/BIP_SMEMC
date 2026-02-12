using Newtonsoft.Json;

namespace BIP_SMEMC.Models;

public class RandomQuizQuestionRow
{
    [JsonProperty("question_id")]
    public long QuestionId { get; set; }

    [JsonProperty("question_text")]
    public string QuestionText { get; set; } = "";

    [JsonProperty("option_id")]
    public long OptionId { get; set; }

    [JsonProperty("option_text")]
    public string OptionText { get; set; } = "";

    [JsonProperty("is_correct")]
    public bool IsCorrect { get; set; }
}
