using BIP_SMEMC.Models;

namespace BIP_SMEMC.Services
{
    public class LearningService
    {
        private readonly Supabase.Client _supabase;
        private readonly RewardsService _rewards;

        private const string USER_ID = "Admin";

        public LearningService(Supabase.Client supabase, RewardsService rewards)
        {
            _supabase = supabase;
            _rewards = rewards;
        }

        /* ===================== TOPICS ===================== */

        public async Task<List<LearningTopic>> GetTopicsAsync()
        {
            var topicsRes = await _supabase.From<LearningTopic>()
                .Order("id", Postgrest.Constants.Ordering.Ascending)
                .Get();

            return topicsRes.Models;
        }

        public async Task<LearningTopic?> GetTopicWithModulesAsync(int topicId)
        {
            var topicRes = await _supabase.From<LearningTopic>()
                .Filter("id", Postgrest.Constants.Operator.Equals, topicId)
                .Get();

            var topic = topicRes.Models.FirstOrDefault();
            if (topic == null) return null;

            var modulesRes = await _supabase.From<LearningModule>()
                .Filter("topic_id", Postgrest.Constants.Operator.Equals, topicId)
                .Get();

            var modules = modulesRes.Models;
            if (!modules.Any())
            {
                topic.Modules = new List<LearningModule>();
                return topic;
            }

            var moduleIds = modules.Select(m => m.Id).ToHashSet();
            var sectionsRes = await _supabase.From<LearningSection>().Get();
            var sections = sectionsRes.Models.Where(s => moduleIds.Contains(s.ModuleId)).ToList();

            var sectionsByModule = sections
                .GroupBy(s => s.ModuleId)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).ToList());

            foreach (var module in modules)
            {
                module.Sections = sectionsByModule.TryGetValue(module.Id, out var list)
                    ? list
                    : new List<LearningSection>();
            }

            topic.Modules = modules.OrderBy(m => m.Id).ToList();
            return topic;
        }

        public async Task<LearningModule?> GetModuleAsync(int topicId, LearningDifficulty difficulty)
        {
            var moduleRes = await _supabase.From<LearningModule>()
                .Filter("topic_id", Postgrest.Constants.Operator.Equals, topicId)
                .Filter("difficulty", Postgrest.Constants.Operator.Equals, (int)difficulty)
                .Get();

            var module = moduleRes.Models.FirstOrDefault();
            if (module == null) return null;

            var rpcParams = new Dictionary<string, object>
            {
                { "p_learning_module_id", module.Id },
                { "p_question_count", 5 }
            };

            var rows = await _supabase.Rpc<List<RandomQuizQuestionRow>>(
                "get_random_quiz_questions",
                rpcParams
            );

            if (rows == null || rows.Count == 0)
            {
                module.QuizQuestions = new List<QuizQuestion>();
                return module;
            }

            var questions = rows
                .GroupBy(r => r.QuestionId)
                .Select(g =>
                {
                    var first = g.First();
                    return new QuizQuestion
                    {
                        Id = (int)first.QuestionId,
                        LearningModuleId = module.Id,
                        Question = first.QuestionText,
                        Options = g.Select(r => new QuizOption
                        {
                            Id = (int)r.OptionId,
                            QuizQuestionId = (int)r.QuestionId,
                            Text = r.OptionText,
                            IsCorrect = r.IsCorrect
                        }).ToList()
                    };
                })
                .ToList();

            module.QuizQuestions = questions;
            return module;
        }

        /* ===================== PROGRESS ===================== */

        public async Task<LearningProgress> GetProgressAsync(int topicId, LearningDifficulty difficulty)
        {
            var progressRes = await _supabase.From<LearningProgress>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, USER_ID)
                .Filter("topic_id", Postgrest.Constants.Operator.Equals, topicId)
                .Filter("difficulty", Postgrest.Constants.Operator.Equals, (int)difficulty)
                .Get();

            var progress = progressRes.Models.FirstOrDefault();
            if (progress != null) return progress;

            var newProgress = new LearningProgress
            {
                UserId = USER_ID,
                TopicId = topicId,
                Difficulty = difficulty,
                Passed = false,
                BestScore = 0,
                PointsAwarded = 0,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var insertRes = await _supabase.From<LearningProgress>().Insert(newProgress);
            return insertRes.Models.FirstOrDefault() ?? newProgress;
        }

        public async Task<bool> IsUnlockedAsync(int topicId, LearningDifficulty difficulty)
        {
            if (difficulty == LearningDifficulty.Beginner)
                return true;

            var previous = difficulty - 1;

            var progressRes = await _supabase.From<LearningProgress>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, USER_ID)
                .Filter("topic_id", Postgrest.Constants.Operator.Equals, topicId)
                .Filter("difficulty", Postgrest.Constants.Operator.Equals, (int)previous)
                .Filter("passed", Postgrest.Constants.Operator.Equals, "true")
                .Get();

            return progressRes.Models.Any();
        }

        /* ===================== QUIZ ===================== */

        public async Task<LearningQuizResult> EvaluateQuizAsync(
            int topicId,
            LearningDifficulty difficulty,
            List<int> selectedOptionIds,
            int passScore = 80
        )
        {
            if (selectedOptionIds == null || selectedOptionIds.Count == 0)
            {
                return new LearningQuizResult
                {
                    TopicId = topicId,
                    Difficulty = difficulty,
                    PercentageScore = 0,
                    Passed = false,
                    CorrectAnswers = 0,
                    TotalQuestions = 0,
                    PointsAwarded = 0
                };
            }

            var optionsRes = await _supabase.From<QuizOption>()
                .Filter("id", Postgrest.Constants.Operator.In, selectedOptionIds)
                .Get();

            var options = optionsRes.Models;
            int total = options.Count;
            int correct = options.Count(o => o.IsCorrect);

            int percentageScore = total == 0
                ? 0
                : (int)Math.Round((double)correct * 100 / total);

            var progress = await GetProgressAsync(topicId, difficulty);

            if (percentageScore > progress.BestScore)
                progress.BestScore = percentageScore;

            bool passed = percentageScore >= passScore;
            progress.Passed = passed;

            if (passed && progress.PointsAwarded == 0)
            {
                int points = difficulty switch
                {
                    LearningDifficulty.Beginner => 100,
                    LearningDifficulty.Intermediate => 200,
                    LearningDifficulty.Advanced => 300,
                    _ => 0
                };

                progress.PointsAwarded = points;
                await _rewards.AddPointsAsync(USER_ID, points, "Completed Learning Quiz");
            }

            progress.UpdatedAtUtc = DateTime.UtcNow;
            await _supabase.From<LearningProgress>().Update(progress);

            return new LearningQuizResult
            {
                TopicId = topicId,
                Difficulty = difficulty,
                PercentageScore = percentageScore,
                Passed = passed,
                CorrectAnswers = correct,
                TotalQuestions = total,
                PointsAwarded = progress.PointsAwarded
            };
        }

        // Add this to LearningService.cs for dashboard controller view
        public async Task<double> GetOverallCompletionAsync(string userId)
        {
            try
            {
                var topics = await GetTopicsAsync();
                if (!topics.Any()) return 0;

                // Count how many modules the user has passed
                var progressRes = await _supabase.From<LearningProgress>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                    .Filter("passed", Postgrest.Constants.Operator.Equals, "true")
                    .Get();

                // Total possible = Topics * 3 (Beginner, Intermediate, Advanced)
                double totalPossible = topics.Count * 3;
                double totalPassed = progressRes.Models.Count;

                return totalPossible > 0 ? Math.Round((totalPassed / totalPossible) * 100, 1) : 0;
            }
            catch { return 0; }
        }
    }
}
