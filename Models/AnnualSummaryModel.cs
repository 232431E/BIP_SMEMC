using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Postgrest.Attributes.Table("annual_summaries")]
    public class AnnualSummaryModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public int? Id { get; set; }
        [Postgrest.Attributes.Column("user_id")]
        public string? UserId { get; set; }
        [Postgrest.Attributes.Column("category_id")]
        public int? CategoryId { get; set; }
        [Postgrest.Attributes.Column("year")]
        public int Year { get; set; }
        [Postgrest.Attributes.Column("annual_total_actual")]
        public decimal AnnualTotalActual { get; set; }
        [Postgrest.Attributes.Column("report_type")] // 'PL' or 'BS'
        public string ReportType { get; set; }
    }
}
