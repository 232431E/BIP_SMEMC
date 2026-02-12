using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;

namespace BIP_SMEMC.Controllers
{
    public class DebtController : Controller
    {
        private readonly DebtService _debtService;

        // FIX: Inject the service instead of creating it with 'new'
        public DebtController(DebtService debtService)
        {
            _debtService = debtService;
        }

        public async Task<IActionResult> Index()
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return RedirectToAction("Login", "Account");
            }
            // 1. Check for new debts from ledger
            await _debtService.SyncDebtsFromTransactions(userEmail);

            // 2. Load Dashboard Data
            var debts = await _debtService.GetAllDebts(userEmail);

            ViewBag.OverdueDebts = debts.Where(d => d.Status == "Overdue").ToList();
            ViewBag.UpcomingDebts = debts.Where(d => d.Status == "Upcoming").ToList();
            ViewBag.TotalOwed = debts.Sum(d => d.TotalAmount);
            ViewBag.DebtCount = debts.Count;

            return View(debts);
        }

        // GET: Debt/Create
        public IActionResult Create()
        {
            return View(new DebtModel
            {
                StartDate = DateTime.Now,
                DueDate = DateTime.Now.AddYears(1)
            });
        }

        // POST: Debt/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DebtModel debt)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return RedirectToAction("Login", "Account");
            }
            // Validation Cleanup
            ModelState.Remove("Id");
            ModelState.Remove("Description");
            ModelState.Remove("UserId");

            if (debt.DueDate <= debt.StartDate)
                ModelState.AddModelError("DueDate", "Due date must be after start date");

            if (!ModelState.IsValid) return View(debt);

            try
            {
                debt.UserId = userEmail;
                debt.Id = Guid.NewGuid().ToString();
                await _debtService.AddDebt(debt);
                TempData["SuccessMessage"] = "Debt added successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(debt);
            }
        }

        // GET: Debt/Edit/5
        public async Task<IActionResult> Edit(string id)
        {
            var debt = await _debtService.GetDebtById(id);
            if (debt == null) return NotFound();
            return View(debt);
        }

        // POST: Debt/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(DebtModel debt)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            ModelState.Remove("Description");
            ModelState.Remove("UserId");
            if (!ModelState.IsValid) return View(debt);

            try
            {
                debt.UserId = userEmail;
                await _debtService.UpdateDebt(debt);
                TempData["SuccessMessage"] = "Debt updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(debt);
            }
        }

        // GET: Details
        public async Task<IActionResult> Details(string id)
        {
            var debt = await _debtService.GetDebtById(id);
            if (debt == null) return NotFound();

            var calculated = _debtService.CalculateDebtDetails(debt);
            return View(calculated);
        }

        // POST: Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            await _debtService.DeleteDebt(id);
            TempData["SuccessMessage"] = "Debt deleted successfully!";
            return RedirectToAction("Index");
        }
    }
}