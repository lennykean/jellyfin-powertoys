using System;

namespace JellyfinPowertoys.Jellywrap.Configuration;

public class UserSettings
{
    public Guid UserId { get; set; }
    public bool Generate { get; set; } = true;
    public bool? IsKid { get; set; }
    public string? TemplateId { get; set; }
}
