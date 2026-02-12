using Newtonsoft.Json;
using Postgrest.Models;
using Postgrest.Attributes;
using System.ComponentModel.DataAnnotations.Schema;
namespace BIP_SMEMC.Models
{
    [Postgrest.Attributes.Table("news_articles")]
    public class NewsArticleModel : BaseModel
    {
        [Postgrest.Attributes.PrimaryKey("id", false)]
        public int Id { get; set; }

        [Postgrest.Attributes.Column("title")]
        public string Title { get; set; }

        [Postgrest.Attributes.Column("summary")]
        public string Summary { get; set; }

        [Postgrest.Attributes.Column("source")]
        public string Source { get; set; }

        [Postgrest.Attributes.Column("url")]
        public string Url { get; set; }

        [Postgrest.Attributes.Column("date")]
        public DateTime Date { get; set; }

        [Postgrest.Attributes.Column("industries")]
        public List<string> Industries { get; set; } = new();

        [Postgrest.Attributes.Column("regions")]
        public List<string> Regions { get; set; } = new();
    }

    [Postgrest.Attributes.Table("categories_industries")]
    public class IndustryModel : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Postgrest.Attributes.Column("name")]
        public string Name { get; set; }

        [Postgrest.Attributes.Column("score_adjustment")]
        public int ScoreAdjustment { get; set; }
    }

    [Postgrest.Attributes.Table("categories_regions")]
    public class RegionModel : BaseModel
    {
        [Postgrest.Attributes.PrimaryKey("id", false)] public int Id { get; set; }
        [Postgrest.Attributes.Column("name")] public string Name { get; set; }
    }

    [Postgrest.Attributes.Table("news_outlook")]
    public class NewsOutlookModel : BaseModel
    {
        [Postgrest.Attributes.PrimaryKey("id", false)]
        public int Id { get; set; }

        [Postgrest.Attributes.Column("industry")]
        [JsonProperty("industry")] // Ensures Gemini JSON maps to this C# prop
        public string Industry { get; set; }

        [Postgrest.Attributes.Column("region")]
        [JsonProperty("region")]
        public string Region { get; set; }

        [Postgrest.Attributes.Column("outlook_summary")]
        [JsonProperty("outlook_summary")]
        public string OutlookSummary { get; set; }

        [Postgrest.Attributes.Column("key_events")]
        [JsonProperty("key_events")]
        public string KeyEvents { get; set; }

        [Postgrest.Attributes.Column("top_leaders")]
        [JsonProperty("top_leaders")]
        public List<string> TopLeaders { get; set; } = new();

        [Postgrest.Attributes.Column("date")]
        [JsonIgnore] // Don't expect this from Gemini, we set it manually
        public DateTime Date { get; set; }
    }
}