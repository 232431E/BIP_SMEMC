namespace BIP_SMEMC.Models;

public class ForumPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Author { get; set; } = "Anonymous";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ForumReply> Replies { get; set; } = new();
}

