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

        // FETCH
        public async Task<List<Employee>> GetAllEmployeesAsync()
        {
            Console.WriteLine($"[GetAllEmployeesAsync] Fetching employees for: {TEST_USER_EMAIL}");
            try
            {
                var response = await _client.From<EmployeeModel>()
                                            .Where(x => x.UserId == TEST_USER_EMAIL)
                                            .Get();

                Console.WriteLine($"[GetAllEmployeesAsync] Success. Count: {response.Models.Count}");

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
                    NRIC = e.NRIC ?? ""
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
            Console.WriteLine($"[GetEmployeeByIdAsync] Fetching ID: {id}");
            try
            {
                var response = await _client.From<EmployeeModel>()
                                            .Where(x => x.Id == id)
                                            .Get();

                var e = response.Model;

                if (e == null)
                {
                    Console.WriteLine("[GetEmployeeByIdAsync] Employee not found.");
                    return null;
                }

                Console.WriteLine($"[GetEmployeeByIdAsync] Found: {e.Name}");

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
                    NRIC = e.NRIC ?? ""
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetEmployeeByIdAsync] Error: {ex.Message}");
                throw;
            }
        }

        // ADD
        public async Task AddEmployeeAsync(Employee employee)
        {
            Console.WriteLine($"[AddEmployeeAsync] Adding: {employee.Name} ({employee.EmployeeId})");
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

                var response = await _client.From<EmployeeModel>().Insert(model);
                Console.WriteLine($"[AddEmployeeAsync] Success. Inserted ID: {response.Model?.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AddEmployeeAsync] Error: {ex.Message}");
                throw;
            }
        }

        // UPDATE
        public async Task UpdateEmployeeAsync(Employee employee)
        {
            Console.WriteLine($"[UpdateEmployeeAsync] Updating ID: {employee.Id}");
            try
            {
                // Fix: Get the record first
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
                    Console.WriteLine("[UpdateEmployeeAsync] Update successful.");
                }
                else
                {
                    Console.WriteLine("[UpdateEmployeeAsync] Record to update not found.");
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
            Console.WriteLine($"[DeleteEmployeeAsync] Deleting ID: {id}");
            try
            {
                await _client.From<EmployeeModel>().Where(x => x.Id == id).Delete();

                // Also delete logs
                Console.WriteLine("[DeleteEmployeeAsync] Deleting associated logs...");
                await _client.From<PayrollLogModel>().Where(x => x.EmployeeId == id).Delete();

                Console.WriteLine("[DeleteEmployeeAsync] Delete complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeleteEmployeeAsync] Error: {ex.Message}");
                throw;
            }
        }

        // OVERTIME
        public async Task AddOrUpdateOvertimeEntryAsync(string employeeId, int month, int year, decimal hours)
        {
            Console.WriteLine($"[AddOrUpdateOvertime] Emp: {employeeId}, M: {month}, Y: {year}, Hours: {hours}");
            try
            {
                // FIX: Chain .Where() explicitly to avoid parser errors
                var response = await _client.From<PayrollLogModel>()
                                            .Where(x => x.EmployeeId == employeeId)
                                            .Where(x => x.SalaryMonth == month)
                                            .Where(x => x.SalaryYear == year)
                                            .Get();

                var existingLog = response.Model;

                if (existingLog != null)
                {
                    Console.WriteLine($"[AddOrUpdateOvertime] Updating existing log: {existingLog.Id}");
                    existingLog.OtHours = hours;
                    await existingLog.Update<PayrollLogModel>();
                }
                else
                {
                    Console.WriteLine("[AddOrUpdateOvertime] Creating new log.");
                    var newLog = new PayrollLogModel
                    {
                        EmployeeId = employeeId,
                        SalaryMonth = month,
                        SalaryYear = year,
                        OtHours = hours,
                        BaseSalary = 0,
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

        public async Task<CalculatedPayroll> CalculatePayrollAsync(Employee employee, int month, int year)
        {
            Console.WriteLine($"[CalculatePayroll] Start for {employee.Name}, M: {month}, Y: {year}");
            try
            {
                // FIX: Use separate .Where() calls to prevent PGRST100 Parser Error
                var response = await _client.From<PayrollLogModel>()
                                            .Where(x => x.EmployeeId == employee.Id)
                                            .Where(x => x.SalaryMonth == month)
                                            .Where(x => x.SalaryYear == year)
                                            .Get(); // Use Get() instead of Single() for safety

                var logEntry = response.Model;
                Console.WriteLine($"[CalculatePayroll] Log found: {logEntry != null}");

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

                // Calculation logic
                calculation.EmployeeCPF = calculation.GrossPay * (employee.CPFRate / 100m);
                calculation.EmployerCPF = calculation.GrossPay * 0.17m;
                calculation.TotalCPF = calculation.EmployeeCPF + calculation.EmployerCPF;
                calculation.NetPay = calculation.GrossPay - calculation.EmployeeCPF;

                Console.WriteLine($"[CalculatePayroll] Calculated NetPay: {calculation.NetPay}");
                return calculation;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalculatePayroll] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<PayrollSummary> GetPayrollSummaryAsync(int month, int year)
        {
            Console.WriteLine($"[GetPayrollSummary] Generating summary for M: {month}, Y: {year}");
            try
            {
                var employees = await GetAllEmployeesAsync();
                var calculations = new List<CalculatedPayroll>();

                foreach (var emp in employees)
                {
                    // This calls CalculatePayrollAsync internally
                    calculations.Add(await CalculatePayrollAsync(emp, month, year));
                }

                var summary = new PayrollSummary
                {
                    MonthYear = new DateTime(year, month, 1).ToString("MMMM yyyy"),
                    TotalPayroll = calculations.Sum(c => c.NetPay),
                    NumberOfEmployees = employees.Count,
                    SelectedMonth = month,
                    SelectedYear = year
                };

                Console.WriteLine($"[GetPayrollSummary] Total Payroll: {summary.TotalPayroll}");
                return summary;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetPayrollSummary] Error: {ex.Message}");
                throw;
            }
        }

        public async Task<List<CalculatedPayroll>> GetAllPayrollCalculationsAsync(int month, int year)
        {
            Console.WriteLine("[GetAllPayrollCalculations] Start");
            try
            {
                var employees = await GetAllEmployeesAsync();
                var calculations = new List<CalculatedPayroll>();

                foreach (var emp in employees)
                {
                    calculations.Add(await CalculatePayrollAsync(emp, month, year));
                }
                Console.WriteLine($"[GetAllPayrollCalculations] Completed. Count: {calculations.Count}");
                return calculations;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllPayrollCalculations] Error: {ex.Message}");
                throw;
            }
        }
    }
}