using BIP_SMEMC.Models.SupabaseModels;

namespace BIP_SMEMC.Models
{
    public class CalculatedPayroll
    {
        public Employee Employee { get; set; }
        public string Month { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal OvertimePay { get; set; }
        public decimal GrossPay { get; set; }
        public decimal EmployeeCPF { get; set; }
        public decimal EmployerCPF { get; set; }
        public decimal TotalCPF { get; set; }
        public decimal NetPay { get; set; }
        public bool HasTimeEntry { get; set; }
    }
}
