namespace BIP_SMEMC.Models;

public class Reward
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Partner { get; set; } = "";
    public string Description { get; set; } = "";
    public int PointsCost { get; set; }
    public string Category { get; set; } = "General";
}

