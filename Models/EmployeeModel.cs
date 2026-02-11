using System;
using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes; // Add this namespace
using Postgrest.Models;     // Add this namespace

namespace BIP_SMEMC.Models.SupabaseModels
{
    [Table("employees")]
    public class EmployeeModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Required(ErrorMessage = "Employee ID is required")]
        [Display(Name = "Employee ID")]
        [Column("employee_id")]
        public string EmployeeId { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [Display(Name = "Full Name")]
        [Column("name")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        [Display(Name = "Email")]
        [Column("email")]
        public string Email { get; set; }

        [Display(Name = "NRIC")]
        [Column("nric")]
        public string NRIC { get; set; }  // OPTIONAL - for payslip display

        [Required(ErrorMessage = "Position is required")]
        [Display(Name = "Position")]
        [Column("position")]
        public string Position { get; set; }

        [Required(ErrorMessage = "Age is required")]
        [Display(Name = "Age")]
        [Range(18, 100, ErrorMessage = "Age must be between 18 and 100")]
        [Column("age")]
        public int? Age { get; set; }

        [Required(ErrorMessage = "Monthly salary is required")]
        [Display(Name = "Monthly Salary")]
        [Range(0.01, 999999999, ErrorMessage = "Monthly salary must be greater than 0")]
        [Column("monthly_salary")]
        public decimal? MonthlySalary { get; set; }

        [Required(ErrorMessage = "Overtime hourly rate is required")]
        [Display(Name = "Overtime Hourly Rate")]
        [Range(0.01, 999999999, ErrorMessage = "Overtime hourly rate must be greater than 0")]
        [Column("overtime_hourly_rate")]
        public decimal? OvertimeHourlyRate { get; set; }

        [Display(Name = "CPF Rate (%)")]
        [Column("cpf_rate")]
        public decimal? CPFRate { get; set; } = 20.00m;  // Default 20%

        [Display(Name = "Date Joined")]
        [DataType(DataType.Date)]
        [Column("date_joined")]
        public DateTime? DateJoined { get; set; }

        //public Employee()
        //{
        //    Id = Guid.NewGuid().ToString();
        //    DateJoined = DateTime.Now;
        //    CPFRate = 20;
        //    UserId = "dummy@sme.com"; // Default for testing
        //}
    }
}