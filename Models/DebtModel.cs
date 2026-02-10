using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    // Using explicit alias to avoid conflict with Table attribute
    [Postgrest.Attributes.Table("debts")]
    public class DebtModel : BaseModel
    {
        [Postgrest.Attributes.PrimaryKey("id", true)] // True = Send UUID to DB
        public string Id { get; set; }

        [Postgrest.Attributes.Column("user_id")]
        public string UserId { get; set; }

        [Required(ErrorMessage = "Creditor name is required")]
        [Display(Name = "Creditor Name")]
        [Postgrest.Attributes.Column("creditor")]
        public string Creditor { get; set; }

        [Required(ErrorMessage = "Principal amount is required")]
        [Display(Name = "Principal Amount")]
        [Range(0.01, 999999999, ErrorMessage = "Amount must be greater than 0")]
        [Postgrest.Attributes.Column("principal_amount")]
        public decimal PrincipalAmount { get; set; }

        [Required(ErrorMessage = "Interest rate is required")]
        [Display(Name = "Interest Rate (%)")]
        [Range(0, 100, ErrorMessage = "Interest rate must be between 0 and 100")]
        [Postgrest.Attributes.Column("interest_rate")]
        public decimal InterestRate { get; set; }

        [Required(ErrorMessage = "Start date is required")]
        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        [Postgrest.Attributes.Column("start_date")]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "Due date is required")]
        [Display(Name = "Due Date")]
        [DataType(DataType.Date)]
        [Postgrest.Attributes.Column("due_date")]
        public DateTime DueDate { get; set; }

        [Postgrest.Attributes.Column("description")]
        [Display(Name = "Description")]
        public string Description { get; set; }

        public DebtModel()
        {
            Id = Guid.NewGuid().ToString(); // Generate default ID
        }
    }

    // View Model for the Dashboard (Calculated fields)
    public class CalculatedDebt : DebtModel
    {
        public decimal InterestAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public int DaysRemaining { get; set; }
        public string Status { get; set; } // "Overdue", "Upcoming", "Normal"
    }
}