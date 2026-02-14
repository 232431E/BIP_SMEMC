using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_resources")]
public class CommunityResource : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required, Column("title")]
    public string Title { get; set; } = "";

    [Required, Column("author")]
    public string Author { get; set; } = "";

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Required, Column("summary")]
    public string Summary { get; set; } = "";

    [Column("tag_primary")]
    public string TagPrimary { get; set; } = "";

    [Column("tag_secondary")]
    public string TagSecondary { get; set; } = "";

    [Column("file_type")]
    public string FileType { get; set; } = "";

    [Column("file_name")]
    public string FileName { get; set; } = "";

    [Column("file_path")]
    public string FilePath { get; set; } = "";

    [Column("file_url")]
    public string FileUrl { get; set; } = "";

    [Column("file_size")]
    public long? FileSize { get; set; }

    [Column("download_count")]
    public int DownloadCount { get; set; }

    [Column("points_reward")]
    public int PointsReward { get; set; }
}
