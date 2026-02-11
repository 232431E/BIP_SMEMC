using System;
using System.ComponentModel.DataAnnotations;

namespace BIP_SMEMC.Models
{
    public class Employee
    {
        public string Id { get; set; }

        [Required(ErrorMessage = "Employee ID is required")]
        [Display(Name = "Employee ID")]
        public string EmployeeId { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [Display(Name = "Full Name")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; }

        [Display(Name = "NRIC")]
        public string NRIC { get; set; }

        [Required(ErrorMessage = "Position is required")]
        public string Position { get; set; }

        [Required]
        [Range(18, 100)]
        public int Age { get; set; }

        [Required]
        [Range(0.01, 999999999)]
        [Display(Name = "Monthly Salary")]
        public decimal MonthlySalary { get; set; }

        [Required]
        [Display(Name = "Overtime Hourly Rate")]
        public decimal OvertimeHourlyRate { get; set; }

        [Display(Name = "CPF Rate (%)")]
        public decimal CPFRate { get; set; } = 20.00m;

        [Display(Name = "Date Joined")]
        [DataType(DataType.Date)]
        public DateTime DateJoined { get; set; } = DateTime.Now;
    }

}
