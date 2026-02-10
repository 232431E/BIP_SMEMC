using Postgrest.Attributes;
using Postgrest.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIP_SMEMC.Models
{
    // Use the fully qualified name to resolve ambiguity
    [Postgrest.Attributes.Table("transactions")]
    public class TransactionModel : BaseModel
    {
        [Postgrest.Attributes.PrimaryKey("id", false)]
        public int? Id { get; set; }

        [Postgrest.Attributes.Column("user_id")]
        public string UserId { get; set; }

        [Postgrest.Attributes.Column("date")]
        public DateTime Date { get; set; }

        [Postgrest.Attributes.Column("description")]
        public string Description { get; set; }

        [Postgrest.Attributes.Column("debit")]
        public decimal Debit { get; set; }

        [Postgrest.Attributes.Column("credit")]
        public decimal Credit { get; set; }

        [Postgrest.Attributes.Column("type")]
        public string Type { get; set; }

        [Postgrest.Attributes.Column("balance")]
        public decimal Balance { get; set; }

        [Postgrest.Attributes.Column("category_id")]
        public int? CategoryId { get; set; }

        [Postgrest.Attributes.Column("tran_month")]
        public int? TranMonth { get; set; }

        [Postgrest.Attributes.Column("tran_year")]
        public int? TranYear { get; set; }

        // This stops Supabase from looking for this column in your table.
        [NotMapped]
        public string CategoryName { get; set; }

        [NotMapped]
        public string? ParentCategoryName { get; set; }
    }
}