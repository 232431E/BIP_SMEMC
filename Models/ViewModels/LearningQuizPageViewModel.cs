using System.Collections.Generic;

namespace BIP_SMEMC.Models
{
    public class LearningQuizPageViewModel
    {
        public string UserId { get; set; } = string.Empty;

        public int TotalPoints { get; set; }

        public LearningTopic Topic { get; set; } = null!;

        public LearningDifficulty Difficulty { get; set; }

        public List<QuizQuestion> Questions { get; set; } = new();

        // Optional – shown after submit
        public LearningQuizResult? Result { get; set; }
    }
}

