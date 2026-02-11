using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using BIP_SMEMC.Models; // Gets the 'Employee' class
using BIP_SMEMC.Services;

namespace BIP_SMEMC.Controllers
{
    public class PayrollController : Controller
    {
        private readonly PayrollService _payrollService;

        public PayrollController(PayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        // GET: Payroll/Index
        public async Task<IActionResult> Index(int? month, int? year, string search)
        {
            int selectedMonth = month ?? DateTime.Now.Month;
            int selectedYear = year ?? DateTime.Now.Year;

            var summary = await _payrollService.GetPayrollSummaryAsync(selectedMonth, selectedYear);
            var payrollCalculations = await _payrollService.GetAllPayrollCalculationsAsync(selectedMonth, selectedYear);

            if (!string.IsNullOrWhiteSpace(search))
            {
                payrollCalculations = payrollCalculations.Where(p =>
                    p.Employee.EmployeeId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    p.Employee.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            ViewBag.Summary = summary;
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SearchTerm = search;

            return View(payrollCalculations);
        }

        // GET: Payroll/Create
        public IActionResult Create()
        {
            return View(new Employee { DateJoined = DateTime.Now });
        }

        // POST: Payroll/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        // FIX: Accept 'Employee' (View Model), not 'EmployeeModel'
        public async Task<IActionResult> Create(Employee employee)
        {
            try
            {
                ModelState.Remove("Id");

                if (string.IsNullOrEmpty(employee.NRIC)) employee.NRIC = "";

                if (!ModelState.IsValid)
                {
                    return View(employee);
                }

                await _payrollService.AddEmployeeAsync(employee);

                TempData["SuccessMessage"] = "Employee added successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error saving employee: {ex.Message}");
                return View(employee);
            }
        }

        // GET: Payroll/Edit/5
        public async Task<IActionResult> Edit(string id)
        {
            // This returns an 'Employee' view model
            var employee = await _payrollService.GetEmployeeByIdAsync(id);
            if (employee == null)
            {
                return NotFound();
            }
            return View(employee);
        }

        // POST: Payroll/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        // FIX: Accept 'Employee' (View Model)
        public async Task<IActionResult> Edit(Employee employee)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(employee);
                }

                await _payrollService.UpdateEmployeeAsync(employee);
                TempData["SuccessMessage"] = "Employee updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error updating employee: {ex.Message}");
                return View(employee);
            }
        }

        // POST: Payroll/DeleteEmployee/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEmployee(string id)
        {
            await _payrollService.DeleteEmployeeAsync(id);
            TempData["SuccessMessage"] = "Employee deleted successfully!";
            return RedirectToAction("Index");
        }

        // GET: Payroll/EnterOvertime
        public async Task<IActionResult> EnterOvertime()
        {
            ViewBag.Employees = await _payrollService.GetAllEmployeesAsync();
            return View();
        }

        // POST: Payroll/EnterOvertime
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnterOvertime(string employeeId, int month, int year, decimal overtimeHours)
        {
            try
            {
                await _payrollService.AddOrUpdateOvertimeEntryAsync(employeeId, month, year, overtimeHours);
                TempData["SuccessMessage"] = "Overtime hours recorded successfully!";
                return RedirectToAction("Index", new { month = month, year = year });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error recording overtime: {ex.Message}";
                return RedirectToAction("EnterOvertime");
            }
        }

        // GET: Payroll/GeneratePayslip/5
        public async Task<IActionResult> GeneratePayslip(string id, int? month, int? year)
        {
            var employee = await _payrollService.GetEmployeeByIdAsync(id);
            if (employee == null)
            {
                return NotFound();
            }

            int selectedMonth = month ?? DateTime.Now.Month;
            int selectedYear = year ?? DateTime.Now.Year;

            var payrollCalculation = await _payrollService.CalculatePayrollAsync(employee, selectedMonth, selectedYear);
            return View("Payslip", payrollCalculation);
        }

        // POST: Payroll/SendAllPayslips
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendAllPayslips(int month, int year)
        {
            var employees = await _payrollService.GetAllEmployeesAsync();
            TempData["SuccessMessage"] = $"Payslips sent successfully to {employees.Count} employees for {new DateTime(year, month, 1).ToString("MMMM yyyy")}!";
            return RedirectToAction("Index", new { month = month, year = year });
        }
    }
}