namespace BIP_SMEMC.Models;

public class LearningQuizResult
{
    public int TopicId { get; set; }
    public LearningDifficulty Difficulty { get; set; }

    public int PercentageScore { get; set; }   // 0-100
    public bool Passed { get; set; }

    public int CorrectAnswers { get; set; }
    public int TotalQuestions { get; set; }

    public int PointsAwarded { get; set; }     // total points stored in progress
    public int PointsToAward { get; set; }     // how many points to add now (0 if already awarded)
}

