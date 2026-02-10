using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Table("debts")]
    public class DebtModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; }

        [Column("user_id")]
        public string UserId { get; set; }

        [Column("creditor")]
        public string Creditor { get; set; }

        [Column("principal_amount")]
        public decimal PrincipalAmount { get; set; }

        [Column("interest_rate")]
        public decimal InterestRate { get; set; }

        [Column("start_date")]
        public DateTime StartDate { get; set; }

        [Column("due_date")]
        public DateTime DueDate { get; set; }

        [Column("description")]
        public string Description { get; set; }
    }
}