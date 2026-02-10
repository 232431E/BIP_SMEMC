using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Table("payroll_logs")]
    public class PayrollLogModel : BaseModel
    {
        // FIX: Change to String to match Employees table strategy
        [PrimaryKey("id", true)]
        public string Id { get; set; }

        [Column("employee_id")]
        public string EmployeeId { get; set; }

        [Column("trans_id")]
        public int TransId { get; set; }

        [Column("gross_salary")]
        public decimal GrossSalary { get; set; }

        [Column("base_salary")]
        public decimal BaseSalary { get; set; }

        [Column("net_pay")]
        public decimal NetPay { get; set; }

        [Column("cpf_amount")]
        public decimal CpfAmount { get; set; }

        [Column("ot_hours")]
        public decimal OtHours { get; set; } = 0;

        [Column("overtime_pay")]
        public decimal OvertimePay { get; set; } = 0;

        [Column("salary_month")]
        public int SalaryMonth { get; set; }

        [Column("salary_year")]
        public int SalaryYear { get; set; }

        [Column("allowances")]
        public decimal Allowances { get; set; } = 0;
    }
}