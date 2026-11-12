using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class Presets
{
    public static readonly JsonObject Builtin = JsonNode.Parse(
        """
        {
          "fade": [ { "prop": "opacity", "keys": [[0, 0], [0.5, 1]] } ],
          "fade-up": [ { "prop": "y", "keys": [[0, "+40"], [0.6, "+0", "out"]] }, { "prop": "opacity", "keys": [[0, 0], [0.5, 1]] } ],
          "fade-down": [ { "prop": "y", "keys": [[0, "-40"], [0.6, "+0", "out"]] }, { "prop": "opacity", "keys": [[0, 0], [0.5, 1]] } ],
          "slide-left": [ { "prop": "x", "keys": [[0, "+120"], [0.6, "+0", "out"]] }, { "prop": "opacity", "keys": [[0, 0], [0.3, 1]] } ],
          "slide-right": [ { "prop": "x", "keys": [[0, "-120"], [0.6, "+0", "out"]] }, { "prop": "opacity", "keys": [[0, 0], [0.3, 1]] } ],
          "slide-up": [ { "prop": "y", "keys": [[0, "+120"], [0.6, "+0", "out"]] }, { "prop": "opacity", "keys": [[0, 0], [0.3, 1]] } ],
          "pop": [ { "prop": "scale", "keys": [[0, 0.8], [0.5, 1, "back"]] }, { "prop": "opacity", "keys": [[0, 0], [0.3, 1]] } ],
          "zoom-in": [ { "prop": "scale", "keys": [[0, 1.15], [0.7, 1, "out"]] }, { "prop": "opacity", "keys": [[0, 0], [0.5, 1]] } ],
          "blur-in": [ { "prop": "blur", "keys": [[0, 24], [0.7, 0, "out"]] }, { "prop": "opacity", "keys": [[0, 0], [0.5, 1]] } ],
          "typewriter": [ { "prop": "chars", "keys": [[0, 0], [1.2, 1]] } ],
          "count-up": [ { "prop": "count", "keys": [[0, 0], ["{{slide.duration | mul:0.4}}", 1, "out"]] } ],
          "float": [ { "prop": "y", "wave": { "amplitude": 8, "period": "{{period | default:4}}" } } ],
          "sway": [ { "prop": "rotation", "wave": { "amplitude": 3, "period": "{{period | default:4}}" } } ],
          "pulse": [ { "prop": "scale", "loop": "{{period | default:2}}",
                       "keys": [[0, 1], ["{{period | default:2 | div:2}}", 1.05, "sine-in-out"], ["{{period | default:2}}", 1, "sine-in-out"]] } ],
          "twinkle": [ { "prop": "opacity", "loop": "{{period | default:1.6}}",
                         "keys": [[0, 1], ["{{period | default:1.6 | div:2}}", 0.45, "sine-in-out"], ["{{period | default:1.6}}", 1, "sine-in-out"]] } ]
        }
        """)!.AsObject();
}
