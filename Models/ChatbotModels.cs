namespace BIP_SMEMC.Models
{
    public class FinancialReportInput
    {
        public int Year { get; set; } = DateTime.Today.Year;
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal ProfitMargin { get; set; }
        public string? Notes { get; set; }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    public class ChatbotPageViewModel
    {
        public FinancialReportInput Report { get; set; } = new();
        public string? InitialInsight { get; set; }
    }
}
