using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JellyfinPowertoys.ThumbnailPreviews;

[ApiController]
[Authorize]
[Route("PowerToys/ThumbnailPreviews/Configuration")]
public class ConfigurationController : ControllerBase
{
    [HttpGet]
    public ActionResult GetConfiguration()
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return NotFound();
        }
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            config.PreviewDuration,
            config.FrameMinDuration,
            config.LoopPreview,
            config.Resolutions,
            config.ShowTrailerPreview,
            config.EnableTrailerLengthLimit,
            config.TrailerMaxLengthSeconds,
            config.OnlyShowSilentTrailers,
            config.PlayTrailerAudio,
            config.EnableHoverPlay,
            config.MouseLingerDelay,
        });
    }
}
