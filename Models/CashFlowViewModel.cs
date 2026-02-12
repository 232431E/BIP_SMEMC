namespace BIP_SMEMC.Models
{
    public class CashFlowViewModel
    {
        public List<ChartDataPoint> ChartData { get; set; } = new();
        public List<NewsArticleModel> News { get; set; } = new();
        
        // --- NEW METRICS ---
        public decimal CurrentBalance { get; set; }
        public decimal MonthlyFixedBurn { get; set; } // Salaries, Rent, Loans
        public decimal VariableCostRatio { get; set; } // Costs that go up when revenue goes up
        public decimal ProjectedCashIn30Days { get; set; }
        public string CashRunway { get; set; } // "3.5 Months" or "Stable"
        // -------------------


        // Preference Data for news section
        public List<NewsOutlookModel> Outlooks { get; set; } = new();
        public DateTime LatestDataDate { get; set; }

        public UserModel UserPreferences { get; set; }
        public List<IndustryModel> AllIndustries { get; set; } = new();
        public List<RegionModel> AllRegions { get; set; } = new();
        public string AIAnalysis { get; set; }
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

    