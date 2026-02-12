using System.Collections.Generic;

namespace BIP_SMEMC.Models
{
    public class LearningHubIndexViewModel
    {
        public string UserId { get; set; } = string.Empty;

        public int TotalPoints { get; set; }

        public LearningDifficulty ActiveDifficulty { get; set; }

        public List<LearningTopicCardViewModel> Topics { get; set; } = new();
    }
}

