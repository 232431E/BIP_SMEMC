namespace BIP_SMEMC.Models
{
    public class CashFlowViewModel
    {
        public List<ChartDataPoint> ChartData { get; set; } = new();
        public List<NewsArticleModel> News { get; set; } = new(); 
        public decimal CurrentBalance { get; set; } = 25000;
        public decimal AvgDailyExpense { get; set; }
        public decimal AvgDailyIncome { get; set; }

        public List<NewsOutlookModel> Outlooks { get; set; } = new();

        // Preference Data
        public UserModel UserPreferences { get; set; }
        public List<IndustryModel> AllIndustries { get; set; } = new();
        public List<RegionModel> AllRegions { get; set; } = new();
    }

    public class ChartDataPoint
    {
        public string Date { get; set; }
        public decimal? Actual { get; set; }
        public decimal? Predicted { get; set; }
        public decimal? Scenario { get; set; }
    }

    public class NewsArticle
    {
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Source { get; set; }
        public string Date { get; set; }

        public string AiIndustryOutlook { get; set; } = "Retrieving AI Outlook...";
    }
}

    