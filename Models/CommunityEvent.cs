using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_events")]
public class CommunityEvent : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required, Column("title")]
    public string Title { get; set; } = "";

    [Required, Column("event_type")]
    public string EventType { get; set; } = "";

    [Required, Column("category")]
    public string Category { get; set; } = "";

    [Column("host_name")]
    public string HostName { get; set; } = "";

    [Column("host_title")]
    public string HostTitle { get; set; } = "";

    [Column("start_at")]
    public DateTime StartAt { get; set; }

    [Column("timezone")]
    public string Timezone { get; set; } = "";

    [Column("is_online")]
    public bool IsOnline { get; set; }

    [Column("location")]
    public string Location { get; set; } = "";

    [Column("seats_booked")]
    public int SeatsBooked { get; set; }

    [Column("seats_total")]
    public int SeatsTotal { get; set; }

    [Column("status_label")]
    public string? StatusLabel { get; set; }

    [Column("is_registered")]
    public bool IsRegistered { get; set; }

    [Column("reminder_set")]
    public bool ReminderSet { get; set; }

    [Column("action_label")]
    public string? ActionLabel { get; set; }
}
