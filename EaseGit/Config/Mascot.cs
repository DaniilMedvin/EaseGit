using System.Text.Json.Serialization;

namespace EaseGit.Config;

/// <summary>Картинка в правом нижнем углу панели diff.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Mascot>))]
public enum Mascot
{
    None,
    Claude,
    ChatGpt,
    Gemini,
}
