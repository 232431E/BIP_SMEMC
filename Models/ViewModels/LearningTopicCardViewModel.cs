namespace BIP_SMEMC.Models
{
    public class LearningTopicCardViewModel
    {
        public int TopicId { get; set; }

        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;

        public int EstimatedMinutes { get; set; }
        public int Points { get; set; }

        public string DifficultyLabel { get; set; } = string.Empty;

        public bool Unlocked { get; set; }
        public bool Passed { get; set; }
    }
}

