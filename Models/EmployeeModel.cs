using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Table("employees")]
    public class EmployeeModel : BaseModel
    {
        [PrimaryKey("id", true)] // False because we might generate GUIDs in C#
        public string Id { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("name")]
        public string Name { get; set; }

        [Column("role")]
        public string Role { get; set; }

        [Column("employee_id")]
        public string EmployeeId { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("position")]
        public string Position { get; set; }

        [Column("age")]
        public int? Age { get; set; }

        [Column("monthly_salary")]
        public decimal? MonthlySalary { get; set; }

        [Column("overtime_hourly_rate")]
        public decimal? OvertimeHourlyRate { get; set; }

        [Column("date_joined")]
        public DateTime? DateJoined { get; set; }

        [Column("cpf_rate")]
        public decimal CpfRate { get; set; } = 20.00m; // Default

        [Column("nric")]
        public string Nric { get; set; }
    }
}