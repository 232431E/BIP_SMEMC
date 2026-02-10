using System.Collections.Generic;

namespace BIP_SMEMC.Models
{
    public class ExpenseManagementViewModel
    {
        public List<TransactionModel> Transactions { get; set; } = new();
        public List<CategoryModel> ExpenseSubCategories { get; set; } = new();
        public decimal TotalExpenses { get; set; }
        public string? ErrorMessage { get; set; }

        // Debugging/Preview
        public List<string> ExcelHeaders { get; set; } = new();
        public List<List<string>> ExcelPreviewRows { get; set; } = new();

        // Used for Charting
        public List<string> Categories { get; set; } = new();
    }
}