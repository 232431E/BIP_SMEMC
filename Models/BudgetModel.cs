using Postgrest.Attributes;
using Postgrest.Models;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace BIP_SMEMC.Models
{
    [Postgrest.Attributes.Table("budgets")]
    public class BudgetModel : BaseModel
    {
        [Postgrest.Attributes.PrimaryKey("id", false)]
        public int Id { get; set; }

        [Postgrest.Attributes.Column("user_id")]
        public string UserId { get; set; }

        [Postgrest.Attributes.Column("category_id")]
        public int CategoryId { get; set; }

        [Postgrest.Attributes.Column("month")]
        public int Month { get; set; }

        [Postgrest.Attributes.Column("year")]
        public int Year { get; set; }

        [Postgrest.Attributes.Column("budget_amount")]
        public decimal BudgetAmount { get; set; }

        [Postgrest.Attributes.Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        [JsonIgnore]
        public string? CategoryName { get; set; }

    }
}