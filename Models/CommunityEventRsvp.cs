using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_event_rsvps")]
public class CommunityEventRsvp : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("event_id")]
    public int EventId { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("reminder_set")]
    public bool ReminderSet { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

