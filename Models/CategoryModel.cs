using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Table("categories")]
    public class CategoryModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public int? Id { get; set; }

        [Column("name")]
        public string Name { get; set; }

        [Column("type")] // 'Income', 'Expense', 'Asset', 'Liability'
        public string Type { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }

        [Column("account_code")]
        public string AccountCode { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;
    }
}