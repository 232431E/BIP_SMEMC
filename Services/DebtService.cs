using BIP_SMEMC.Models;
using System.Diagnostics;

namespace BIP_SMEMC.Services
{
    public class DebtService
    {
        private readonly Supabase.Client _supabase;
        private readonly FinanceService _financeService;

        public DebtService(Supabase.Client supabase, FinanceService financeService)
        {
            _supabase = supabase;
            _financeService = financeService;
        }

        // --- 1. AUTO-SYNC LOGIC ---
        public async Task SyncDebtsFromTransactions(string userEmail)
        {
            try
            {
                var existingRes = await _supabase.From<DebtModel>().Where(d => d.UserId == userEmail).Get();
                var existingKeys = new HashSet<string>(existingRes.Models.Select(d => $"{d.Description}-{d.StartDate:yyyyMMdd}"));

                var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
                var transactions = await _financeService.GetUserTransactions(userEmail, new DateTime(2020, 1, 1), latestDate);

                var newDebts = new List<DebtModel>();

                foreach (var t in transactions)
                {
                    bool isLoan = t.Description.Contains("Loan", StringComparison.OrdinalIgnoreCase) ||
                                  t.Description.Contains("Financing", StringComparison.OrdinalIgnoreCase);

                    if (isLoan && t.Credit > 0)
                    {
                        string key = $"{t.Description}-{t.Date:yyyyMMdd}";

                        if (!existingKeys.Contains(key))
                        {
                            newDebts.Add(new DebtModel
                            {
                                Id = Guid.NewGuid().ToString(),
                                UserId = userEmail,
                                Creditor = DetermineCreditor(t.Description),
                                PrincipalAmount = t.Credit,
                                InterestRate = 5.5m,
                                StartDate = t.Date,
                                DueDate = t.Date.AddYears(1),
                                Description = t.Description
                            });
                            existingKeys.Add(key);
                        }
                    }
                }

                if (newDebts.Any())
                {
                    await _supabase.From<DebtModel>().Insert(newDebts);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DEBT SYNC ERROR] {ex.Message}"); }
        }

        private string DetermineCreditor(string desc)
        {
            if (desc.Contains("DBS")) return "DBS Bank";
            if (desc.Contains("OCBC")) return "OCBC Bank";
            if (desc.Contains("UOB")) return "UOB Bank";
            return "General Creditor";
        }

        // --- 2. CRUD METHODS (FIXING COMPILER ERRORS) ---

        public async Task<List<CalculatedDebt>> GetAllDebts(string userEmail)
        {
            var res = await _supabase.From<DebtModel>().Where(d => d.UserId == userEmail).Get();
            return res.Models.Select(CalculateDebtDetails).ToList();
        }

        public async Task<DebtModel> GetDebtById(string id)
        {
            var res = await _supabase.From<DebtModel>().Where(d => d.Id == id).Get();
            return res.Models.FirstOrDefault();
        }

        public async Task AddDebt(DebtModel debt)
        {
            await _supabase.From<DebtModel>().Insert(debt);
        }

        public async Task UpdateDebt(DebtModel debt)
        {
            await _supabase.From<DebtModel>().Update(debt);
        }

        public async Task DeleteDebt(string id)
        {
            await _supabase.From<DebtModel>().Where(d => d.Id == id).Delete();
        }

        // --- 3. CALCULATION HELPER ---
        public CalculatedDebt CalculateDebtDetails(DebtModel debt)
        {
            var principal = debt.PrincipalAmount;
            var rate = debt.InterestRate / 100m;
            var totalDays = Math.Max(1, (debt.DueDate - debt.StartDate).Days);
            var timeInYears = totalDays / 365m;
            var interestAmount = principal * rate * timeInYears;
            var totalAmount = principal + interestAmount;
            var daysRemaining = (debt.DueDate - DateTime.Now).Days;

            string status = daysRemaining < 0 ? "Overdue" : daysRemaining <= 30 ? "Upcoming" : "Normal";

            return new CalculatedDebt
            {
                Id = debt.Id,
                UserId = debt.UserId,
                Creditor = debt.Creditor,
                PrincipalAmount = debt.PrincipalAmount,
                InterestRate = debt.InterestRate,
                StartDate = debt.StartDate,
                DueDate = debt.DueDate,
                Description = debt.Description,
                InterestAmount = interestAmount,
                TotalAmount = totalAmount,
                DaysRemaining = daysRemaining,
                Status = status
            };
        }
        
            // Helper wrappers for Stats used in Controller
        public async Task<List<CalculatedDebt>> GetOverdueDebts(string email)
        {
            var all = await GetAllDebts(email);
            return all.Where(d => d.Status == "Overdue").ToList();
        }

        public async Task<List<CalculatedDebt>> GetUpcomingDebts(string email)
        {
            var all = await GetAllDebts(email);
            return all.Where(d => d.Status == "Upcoming").ToList();
        }

        public async Task<decimal> GetTotalOwed(string email)
        {
            var all = await GetAllDebts(email);
            return all.Sum(d => d.TotalAmount);
        }
    }
}