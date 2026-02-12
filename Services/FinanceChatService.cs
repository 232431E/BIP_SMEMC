using System.Text.Json;
using System.Text.RegularExpressions;
using BIP_SMEMC.Models;
using BIP_SMEMC.ViewModels.Chat;

namespace BIP_SMEMC.Services;

public class FinanceChatService
{
    private readonly GeminiService _geminiService;
    private readonly Services.Finance.FinancialDataService _financialDataService;
    private readonly Supabase.Client _supabase; // CHANGED

    public FinanceChatService(
        GeminiService geminiService,
        Services.Finance.FinancialDataService financialDataService,
        Supabase.Client supabase) // CHANGED
    {
        _geminiService = geminiService;
        _financialDataService = financialDataService;
        _supabase = supabase;
    }
    private static readonly string[] FinanceKeywords =
    {
        "revenue", "expense", "profit", "margin", "cash", "flow", "budget", "cost", "pricing", "invoice",
        "liability", "asset", "balance", "p&l", "pl", "gl", "financial", "opex", "cogs", "runway", "savings"
    };

    private static readonly string[] NonFinanceKeywords =
    {
        "animal", "movie", "music", "football", "weather", "game", "recipe", "horoscope", "celebrity", "trivia"
    };

    public async Task<List<ChatMessageViewModel>> GetHistoryAsync(string userId, int? reportYear = null, int take = 30)
    {
        var query = _supabase.From<ChatMessageModel>().Where(x => x.UserId == userId);
        if (reportYear.HasValue)
        {
            query = query.Where(x => x.ReportYear == reportYear.Value);
        }

        var response = await query
            .Order("created_at", Postgrest.Constants.Ordering.Descending)
            .Limit(take)
            .Get();

        return response.Models.OrderBy(x => x.CreatedAt).Select(x => new ChatMessageViewModel
        {
            Role = x.Role,
            Message = x.Message,
            CreatedAt = x.CreatedAt
        }).ToList();
    }

    private async Task SaveMessageAsync(string userId, int reportYear, string role, string message)
    {
        var model = new ChatMessageModel
        {
            UserId = userId,
            ReportYear = reportYear,
            Role = role,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };
        await _supabase.From<ChatMessageModel>().Insert(model);
    }
    public async Task<(bool Success, string Error, ChatStructuredResponse? Response)> AskAsync(string userId, int reportYear, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return (false, "Message cannot be empty.", null);
        }

        if (!IsFinanceTopic(message))
        {
            return (true, string.Empty, new ChatStructuredResponse
            {
                Answer = "I can only help with finance/accounting/cashflow questions for your business. Ask about revenue, expenses, margins, goals, or profit improvements.",
                ActionItems = new List<string>
                {
                    "Ask about your expense drivers.",
                    "Ask how to improve margin.",
                    "Ask how close you are to your goal."
                },
                UsedNumbers = new UsedNumbers()
            });
        }

        var context = await _financialDataService.GetFinancialContextAsync(userId, reportYear);
        if (context is null)
        {
            return (false, "No report data found. Upload a report first.", null);
        }

        var contextJson = JsonSerializer.Serialize(context);
        var systemInstruction = "You are SME Finance Assistant. Only answer finance/accounting/cashflow/business performance questions using the provided context. " +
                                "Do not answer unrelated topics. Do not reveal private data outside provided context. " +
                                "Do not claim access to external bank or personal accounts.";

        await SaveMessageAsync(userId, reportYear, "user", message);

        var gemini = await _geminiService.GenerateFinanceChatJsonAsync(systemInstruction, contextJson, message);
        if (!gemini.Success)
        {
            var err = gemini.QuotaExceeded
                ? "API quota exceeded. Please try again later."
                : gemini.Content;
            await SaveMessageAsync(userId, reportYear, "assistant", err);
            return (false, err, null);
        }

        var parsed = ParseStructuredResponse(gemini.Content, context);
        await SaveMessageAsync(userId, reportYear, "assistant", JsonSerializer.Serialize(parsed));
        return (true, string.Empty, parsed);
    }

    public async Task<string> BuildContextPreviewAsync(string userId, int reportYear)
    {
        var context = await _financialDataService.GetFinancialContextAsync(userId, reportYear);
        return context is null
            ? "No financial context available yet."
            : JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true });
    }

    
    private static ChatStructuredResponse ParseStructuredResponse(string raw, FinancialContextSummary context)
    {
        var cleaned = CleanJson(raw);

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var answer = root.TryGetProperty("answer", out var answerEl)
                ? answerEl.GetString() ?? string.Empty
                : "I analyzed your financial report and shared the top actions.";

            var actionItems = new List<string>();
            if (root.TryGetProperty("actionItems", out var actionsEl) && actionsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in actionsEl.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        actionItems.Add(val);
                    }
                }
            }

            if (actionItems.Count < 3)
            {
                actionItems.AddRange(DefaultActions(context).Take(3 - actionItems.Count));
            }

            var used = new UsedNumbers
            {
                Revenue = context.Revenue,
                Expenses = context.Expenses,
                NetProfit = context.NetProfit,
                ProfitMargin = context.ProfitMargin,
                TopExpenseCategories = context.TopExpenseCategories.Select(x => x.Category).Take(6).ToList()
            };

            return new ChatStructuredResponse
            {
                Answer = answer,
                ActionItems = actionItems.Take(6).ToList(),
                UsedNumbers = used
            };
        }
        catch
        {
            return new ChatStructuredResponse
            {
                Answer = "I could not parse the model output cleanly, but here is a grounded response based on your numbers.",
                ActionItems = DefaultActions(context).Take(4).ToList(),
                UsedNumbers = new UsedNumbers
                {
                    Revenue = context.Revenue,
                    Expenses = context.Expenses,
                    NetProfit = context.NetProfit,
                    ProfitMargin = context.ProfitMargin,
                    TopExpenseCategories = context.TopExpenseCategories.Select(x => x.Category).Take(6).ToList()
                }
            };
        }
    }

    private static IEnumerable<string> DefaultActions(FinancialContextSummary context)
    {
        yield return "Cut 5-10% from top expense categories with supplier renegotiation and spend controls.";
        yield return "Review pricing/discount policy to lift margin by 1-3 percentage points.";
        yield return "Track weekly cash-in vs cash-out and enforce invoice follow-ups for overdue receivables.";
        if (context.Goal is not null)
        {
            yield return "Align monthly execution plan to the active profit goal and monitor variance each month.";
        }
    }

    private static bool IsFinanceTopic(string message)
    {
        var text = message.ToLowerInvariant();

        if (NonFinanceKeywords.Any(text.Contains) && !FinanceKeywords.Any(text.Contains))
        {
            return false;
        }

        return FinanceKeywords.Any(text.Contains)
               || Regex.IsMatch(text, "\b(sme|business|company|accounting|profitability|budgeting|forecast)\b", RegexOptions.IgnoreCase);
    }

    private static string CleanJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        var trimmed = raw.Trim();
        trimmed = Regex.Replace(trimmed, "^```json", string.Empty, RegexOptions.IgnoreCase).Trim();
        trimmed = Regex.Replace(trimmed, "^```", string.Empty).Trim();
        trimmed = Regex.Replace(trimmed, "```$", string.Empty).Trim();

        return trimmed;
    }
}

