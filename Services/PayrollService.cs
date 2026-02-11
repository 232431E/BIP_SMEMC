using BIP_SMEMC.Models;
using BIP_SMEMC.Models.SupabaseModels;
using Supabase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIP_SMEMC.Services
{
    public class PayrollService
    {
        private readonly Supabase.Client _client;
        private const string TEST_USER_EMAIL = "dummy@sme.com";

        public PayrollService(Supabase.Client client)
        {
            _client = client;
        }

        // --- FETCH EMPLOYEES ---
        public async Task<List<Employee>> GetAllEmployeesAsync()
        {
            try
            {
                var response = await _client.From<EmployeeModel>()
                                            .Where(x => x.UserId == TEST_USER_EMAIL)
                                            .Get();

                return response.Models.Select(e => new Employee
                {
                    Id = e.Id,
                    EmployeeId = e.EmployeeId,
                    Name = e.Name,
                    Email = e.Email,
                    Position = e.Position,
                    Age = e.Age ?? 0,
                    MonthlySalary = e.MonthlySalary ?? 0,
                    OvertimeHourlyRate = e.OvertimeHourlyRate ?? 0,
                    DateJoined = e.DateJoined ?? DateTime.Now,
                    CPFRate = e.CPFRate ?? 20.00m,
                    NRIC = e.NRIC
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllEmployeesAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<Employee?> GetEmployeeByIdAsync(string id)
        {
            try
            {
                var response = await _client.From<EmployeeModel>()
                                            .Where(x => x.Id == id)
                                            .Get();

                var e = response.Model;
                if (e == null) return null;

                return new Employee
                {
                    Id = e.Id,
                    EmployeeId = e.EmployeeId,
                    Name = e.Name,
                    Email = e.Email,
                    Position = e.Position,
                    Age = e.Age ?? 0,
                    MonthlySalary = e.MonthlySalary ?? 0,
                    OvertimeHourlyRate = e.OvertimeHourlyRate ?? 0,
                    DateJoined = e.DateJoined ?? DateTime.Now,
                    CPFRate = e.CPFRate ?? 20.00m,
                    NRIC = e.NRIC
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetEmployeeByIdAsync] Error: {ex.Message}");
                throw;
            }
        }

        // --- CRUD OPERATIONS ---
        public async Task AddEmployeeAsync(Employee employee)
        {
            try
            {
                var model = new EmployeeModel
                {
                    UserId = TEST_USER_EMAIL,
                    EmployeeId = employee.EmployeeId,
                    Name = employee.Name,
                    Email = employee.Email,
                    Position = employee.Position,
                    Age = employee.Age,
                    MonthlySalary = employee.MonthlySalary,
                    OvertimeHourlyRate = employee.OvertimeHourlyRate,
                    DateJoined = employee.DateJoined,
                    CPFRate = employee.CPFRate,
                    NRIC = employee.NRIC
                };

                await _client.From<EmployeeModel>().Insert(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AddEmployeeAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateEmployeeAsync(Employee employee)
        {
            try
            {
                var response = await _client.From<EmployeeModel>()
                                          .Where(x => x.Id == employee.Id)
                                          .Get();

                var update = response.Model;

                if (update != null)
                {
                    update.EmployeeId = employee.EmployeeId;
                    update.Name = employee.Name;
                    update.Email = employee.Email;
                    update.Position = employee.Position;
                    update.Age = employee.Age;
                    update.MonthlySalary = employee.MonthlySalary;
                    update.OvertimeHourlyRate = employee.OvertimeHourlyRate;
                    update.DateJoined = employee.DateJoined;
                    update.CPFRate = employee.CPFRate;
                    update.NRIC = employee.NRIC;

                    await update.Update<EmployeeModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateEmployeeAsync] Error: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteEmployeeAsync(string id)
        {
            try
            {
                await _client.From<EmployeeModel>().Where(x => x.Id == id).Delete();
                await _client.From<PayrollLogModel>().Where(x => x.EmployeeId == id).Delete();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeleteEmployeeAsync] Error: {ex.Message}");
                throw;
            }
        }

        // --- OPTIMIZED CALCULATION METHODS ---

        // 1. Single Employee Calculation (Fixes PGRST100 Error)
        public async Task<CalculatedPayroll> CalculatePayrollAsync(Employee employee, int month, int year)
        {
            try
            {
                // FIX: Query ONLY by EmployeeId to avoid complex query string parsing errors.
                // We filter for the specific month/year in memory.
                var response = await _client.From<PayrollLogModel>()
                                            .Where(x => x.EmployeeId == employee.Id)
                                            .Get();

                var logEntry = response.Models.FirstOrDefault(x => x.SalaryMonth == month && x.SalaryYear == year);

                return CalculatePayrollInMemory(employee, logEntry, month, year);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalculatePayrollAsync] Error: {ex.Message}");
                throw;
            }
        }

        // 2. Batch Summary Calculation (Fixes Performance/Time Consuming issue)
        public async Task<PayrollSummary> GetPayrollSummaryAsync(int month, int year)
        {
            try
            {
                var employees = await GetAllEmployeesAsync();

                // BATCH FETCH: Get ALL logs for the selected period in ONE request
                var logsResponse = await _client.From<PayrollLogModel>()
                                                .Where(x => x.SalaryMonth == month && x.SalaryYear == year)
                                                .Get();
                var logs = logsResponse.Models;

                var calculations = new List<CalculatedPayroll>();

                foreach (var emp in employees)
                {
                    // Match in memory (Fast)
                    var logEntry = logs.FirstOrDefault(l => l.EmployeeId == emp.Id);
                    calculations.Add(CalculatePayrollInMemory(emp, logEntry, month, year));
                }

                return new PayrollSummary
                {
                    MonthYear = new DateTime(year, month, 1).ToString("MMMM yyyy"),
                    TotalPayroll = calculations.Sum(c => c.NetPay),
                    NumberOfEmployees = employees.Count,
                    SelectedMonth = month,
                    SelectedYear = year
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetPayrollSummary] Error: {ex.Message}");
                throw;
            }
        }

        // 3. Batch List Retrieval
        public async Task<List<CalculatedPayroll>> GetAllPayrollCalculationsAsync(int month, int year)
        {
            try
            {
                var employees = await GetAllEmployeesAsync();

                // Batch Fetch
                var logsResponse = await _client.From<PayrollLogModel>()
                                                .Where(x => x.SalaryMonth == month && x.SalaryYear == year)
                                                .Get();
                var logs = logsResponse.Models;

                var calculations = new List<CalculatedPayroll>();

                foreach (var emp in employees)
                {
                    var logEntry = logs.FirstOrDefault(l => l.EmployeeId == emp.Id);
                    calculations.Add(CalculatePayrollInMemory(emp, logEntry, month, year));
                }

                return calculations;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllPayrollCalculations] Error: {ex.Message}");
                throw;
            }
        }

        // --- HELPER: Pure Logic Calculation ---
        private CalculatedPayroll CalculatePayrollInMemory(Employee employee, PayrollLogModel? logEntry, int month, int year)
        {
            decimal overtimeHours = logEntry?.OtHours ?? 0;

            var calculation = new CalculatedPayroll
            {
                Employee = employee,
                Month = new DateTime(year, month, 1).ToString("MMMM yyyy"),
                BaseSalary = employee.MonthlySalary,
                OvertimeHours = overtimeHours,
                HasTimeEntry = overtimeHours > 0
            };

            calculation.OvertimePay = overtimeHours * employee.OvertimeHourlyRate;
            calculation.GrossPay = calculation.BaseSalary + calculation.OvertimePay;

            // CPF Logic
            calculation.EmployeeCPF = calculation.GrossPay * (employee.CPFRate / 100m);
            calculation.EmployerCPF = calculation.GrossPay * 0.17m;
            calculation.TotalCPF = calculation.EmployeeCPF + calculation.EmployerCPF;
            calculation.NetPay = calculation.GrossPay - calculation.EmployeeCPF;

            return calculation;
        }

        // --- OVERTIME ENTRY ---
        public async Task AddOrUpdateOvertimeEntryAsync(string employeeId, int month, int year, decimal hours)
        {
            try
            {
                // Similar safe query strategy: Fetch logs for employee, check month locally
                var response = await _client.From<PayrollLogModel>()
                                            .Where(x => x.EmployeeId == employeeId)
                                            .Get();

                var existingLog = response.Models.FirstOrDefault(x => x.SalaryMonth == month && x.SalaryYear == year);

                if (existingLog != null)
                {
                    existingLog.OtHours = hours;
                    await existingLog.Update<PayrollLogModel>();
                }
                else
                {
                    var newLog = new PayrollLogModel
                    {
                        EmployeeId = employeeId,
                        SalaryMonth = month,
                        SalaryYear = year,
                        OtHours = hours,
                        BaseSalary = 0, // Will be calculated on display
                        GrossSalary = 0,
                        NetPay = 0
                    };
                    await _client.From<PayrollLogModel>().Insert(newLog);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AddOrUpdateOvertime] Error: {ex.Message}");
                throw;
            }
        }
    }
}