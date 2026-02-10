namespace BIP_SMEMC.Models
{
    public class BudgetViewModel
    {
        // Top Section Data
        // This holds the filtered transaction list (Actuals)
        public List<TransactionModel> AllTransactions { get; set; } = new();

        // This holds the Tier 1 categories (e.g., "Marketing", "Utilities")
        public List<CategoryModel> ExpenseCategories { get; set; } = new();

        // This holds the saved budget targets from the database
        public List<BudgetModel> BudgetRecords { get; set; } = new();

        public DateTime LatestDataDate { get; set; } = DateTime.Today;

        // Calculated Property: Only show categories with activity
        public List<CategoryModel> ActiveCategories => ExpenseCategories
            .Where(c => AllTransactions.Any(t => t.ParentCategoryName == c.Name) ||
                        BudgetRecords.Any(b => b.CategoryId == c.Id))
            .ToList();

        public List<BudgetItem> Budgets { get; set; } = new();
        public List<ExpenseItem> AllExpenses { get; set; } = new();

        public decimal TotalBudget => Budgets.Sum(b => b.TargetAmount);
        public decimal TotalSpent => AllExpenses.Sum(e => e.Amount);
        public decimal Remaining => TotalBudget - TotalSpent;
        public int OverBudgetCount => Budgets.Count(b => GetActualForCategory(b.Category) > b.TargetAmount);

        // Helper to sum individual expenses for a specific category
        public decimal GetActualForCategory(string category) =>
            AllExpenses.Where(e => e.Category == category).Sum(e => e.Amount);

    }

    public class BudgetItem
    {
        public string Category { get; set; }
        public decimal TargetAmount { get; set; }
    }

    public class ExpenseItem
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public decimal Amount { get; set; }
        public string MonthLabel => Date.ToString("MMM yy"); // e.g., "Dec 25"
    }
}