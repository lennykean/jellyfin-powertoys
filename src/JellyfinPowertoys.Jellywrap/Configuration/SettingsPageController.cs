using System.Linq;
using System.Threading.Tasks;

using JellyfinPowertoys.Jellywrap.Context;
using JellyfinPowertoys.Jellywrap.Templates;

using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JellyfinPowertoys.Jellywrap.Configuration;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("PowerToys/Jellywrap")]
public class SettingsPageController(IApplicationPaths applicationPaths, WrapSourceReader sourceReader) : ControllerBase
{
    [HttpGet("Templates")]
    public ActionResult GetTemplates()
    {
        var catalog = TemplateCatalog.Load(
            JellywrapPaths.Templates(applicationPaths, Plugin.Instance?.Configuration.CustomTemplateFolder));

        return Ok(catalog.All
            .Where(template => template.IsValid)
            .OrderBy(template => template.Name)
            .Select(template => new
            {
                template.Id,
                template.Name,
                IsForKids = template.Audience == Template.KidsAudience,
                HasIndividualVideo = template.Bases.ContainsKey(Template.IndividualKind),
                HasServerVideo = template.Bases.ContainsKey(Template.ServerKind),
            }));
    }

    [HttpGet("StarterTemplate")]
    public ActionResult GetStarterTemplate()
    {
        var folder = Plugin.Instance?.Configuration.CustomTemplateFolder;
        return Ok(new { CanAdd = !string.IsNullOrWhiteSpace(folder) && !StarterTemplate.IsIn(folder.Trim()) });
    }

    [HttpPost("StarterTemplate")]
    public ActionResult AddStarterTemplate()
    {
        var folder = Plugin.Instance?.Configuration.CustomTemplateFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return BadRequest();
        }

        StarterTemplate.AddTo(folder.Trim());
        return NoContent();
    }

    [HttpGet("Years")]
    public async Task<ActionResult> GetYears()
    {
        return Ok(await sourceReader.ReadYearsWithPlaysAsync());
    }
}
