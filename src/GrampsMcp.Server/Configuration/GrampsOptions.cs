using System.ComponentModel.DataAnnotations;

namespace GrampsMcp.Server.Configuration;

public sealed class GrampsOptions
{
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string ApiUrl { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Username { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Password { get; set; } = string.Empty;

    public bool AllowWrites { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxPageSize { get; set; } = 100;

    [Required(AllowEmptyStrings = false)]
    public string DefaultProfile { get; set; } = "self,families,events";
}
