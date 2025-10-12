using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using HttpResponseTransformer.Transforms;

using Microsoft.AspNetCore.Http;

namespace JellyfinPowertoys.RemoteTrailers
{
    internal class InjectPluginConfig(string pluginName) : IResponseTransform
    {
        public bool ShouldTransform(HttpContext context) => context.Request.Path.Equals("/web/config.json");

        public void ExecuteTransform(HttpContext context, ref byte[] content)
        {
            var config = JsonNode.Parse(content) ?? new JsonObject();
            var plugins = config["plugins"];

            if (plugins is not JsonArray pluginsArray)
            {
                pluginsArray = [];
                config["plugins"] = pluginsArray;
            }
            pluginsArray.Add(pluginName);

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                config.WriteTo(writer);
            }
            content = buffer.ToArray();
        }
    }
}
