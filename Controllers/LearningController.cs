using Microsoft.AspNetCore.Mvc;
using BIP_SMEMC.Models;
using BIP_SMEMC.Services;

namespace BIP_SMEMC.Controllers
{
    public class LearningController : Controller
    {
        private readonly LearningService _learning;
        private readonly RewardsService _rewards;

        public LearningController(LearningService learning, RewardsService rewards)
        {
            _learning = learning;
            _rewards = rewards;
        }

        private string? GetCurrentUserId()
        {
            return HttpContext.Session.GetString("UserEmail");
        }

        // ===================== LEARNING HUB =====================

        [HttpGet]
        public async Task<IActionResult> Index(LearningDifficulty difficulty = LearningDifficulty.Beginner)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

            var topics = await _learning.GetTopicsAsync();
            var totalPoints = await _rewards.GetPointsAsync(userId);

            var vm = new LearningHubIndexViewModel
            {
                UserId = userId,
                TotalPoints = totalPoints,
                ActiveDifficulty = difficulty
            };

            foreach (var t in topics)
            {
                var progress = await _learning.GetProgressAsync(userId, t.Id, difficulty);

                vm.Topics.Add(new LearningTopicCardViewModel
                {
                    TopicId = t.Id,
                    Title = t.Title,
                    Category = "General",
                    Summary = "Learn essential concepts in this topic.",
                    EstimatedMinutes = 10,
                    Points = difficulty switch
                    {
                        LearningDifficulty.Beginner => 100,
                        LearningDifficulty.Intermediate => 200,
                        LearningDifficulty.Advanced => 300,
                        _ => 0
                    },
                    DifficultyLabel = difficulty.ToString(),
                    Unlocked = await _learning.IsUnlockedAsync(userId, t.Id, difficulty),
                    Passed = progress.Passed
                });
            }

            return View(vm);
        }

        // ===================== TOPIC PAGE =====================

        [HttpGet]
        public async Task<IActionResult> Topic(int id, LearningDifficulty difficulty = LearningDifficulty.Beginner)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

            var topic = await _learning.GetTopicWithModulesAsync(id);
            if (topic == null)
                return RedirectToAction(nameof(Index));

            topic.Modules = topic.Modules
                .Where(m => m.Difficulty == difficulty)
                .OrderBy(m => m.Id)
                .ToList();

            ViewData["TotalPoints"] = await _rewards.GetPointsAsync(userId);
            ViewData["Topic"] = topic;
            ViewData["Difficulty"] = difficulty;

            return View(topic);
        }

        // ===================== QUIZ PAGE =====================

        [HttpGet]
        public async Task<IActionResult> Quiz(int topicId, LearningDifficulty difficulty)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

            var module = await _learning.GetModuleAsync(topicId, difficulty);
            if (module == null)
                return RedirectToAction(nameof(Index));

            var topic = await _learning.GetTopicWithModulesAsync(topicId);
            if (topic == null)
                return RedirectToAction(nameof(Index));

            var vm = new LearningQuizPageViewModel
            {
                UserId = userId,
                TotalPoints = await _rewards.GetPointsAsync(userId),
                Topic = topic,
                Difficulty = difficulty,
                Questions = module.QuizQuestions.ToList()
            };

            return View(vm);
        }

        // ===================== SUBMIT QUIZ (FIXED) =====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitQuiz(int topicId, LearningDifficulty difficulty)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

            var selectedOptionIds = new List<int>();

            foreach (var key in Request.Form.Keys)
            {
                if (!key.StartsWith("q_")) continue;

                if (!int.TryParse(key.Replace("q_", ""), out var questionId)) continue;
                if (!int.TryParse(Request.Form[key], out var selectedOptionId)) continue;

                selectedOptionIds.Add(selectedOptionId);
            }

            var result = await _learning.EvaluateQuizAsync(
                userId,
                topicId,
                difficulty,
                selectedOptionIds
            );

            return View("QuizResult", result);
        }
    }
}
