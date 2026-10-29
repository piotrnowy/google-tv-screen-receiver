using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenSender.Windows.Signaling;

// Matches app/src/main/java/pl/screenreceiver/tv/network/SignalingMessage.kt.
public abstract record SignalingMessage(string Type);
public sealed record HelloMessage(string DeviceName) : SignalingMessage("hello");
public sealed record OfferMessage(string Sdp) : SignalingMessage("offer");
public sealed record AnswerMessage(string Sdp) : SignalingMessage("answer");
public sealed record IceMessage(string? SdpMid, int SdpMLineIndex, string Candidate) : SignalingMessage("ice");
public sealed record ByeMessage() : SignalingMessage("bye");
public sealed record VolumeMessage(double Value) : SignalingMessage("volume");
public sealed record MuteMessage(bool Value) : SignalingMessage("mute");
public enum RemoteVolumeAction { Up, Down, MuteToggle }
public sealed record VolumeActionMessage(RemoteVolumeAction Action) : SignalingMessage("volume");
public sealed record GetVolumeMessage() : SignalingMessage("getVolume");
public sealed record VolumeStateMessage(int Current, int Max, bool Muted, bool Available = true) : SignalingMessage("volumeState");

public static class SignalingCodec
{
    public const int MaxLineChars = 1_048_576;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Encode(SignalingMessage message)
    {
        if (message is VolumeActionMessage action)
            return action.Action switch
            {
                RemoteVolumeAction.Up => "{\"type\":\"volume\",\"action\":\"up\"}",
                RemoteVolumeAction.Down => "{\"type\":\"volume\",\"action\":\"down\"}",
                RemoteVolumeAction.MuteToggle => "{\"type\":\"volume\",\"action\":\"muteToggle\"}",
                _ => throw new ArgumentOutOfRangeException(nameof(message)),
            };
        if (message is GetVolumeMessage) return "{\"type\":\"getVolume\"}";
        if (message is VolumeStateMessage state)
            return JsonSerializer.Serialize(new { type = "volumeState", current = state.Current, max = state.Max, muted = state.Muted, available = state.Available }, Options);
        if (message is VolumeMessage volume && (!double.IsFinite(volume.Value) || volume.Value < 0 || volume.Value > 1))
            throw new ArgumentOutOfRangeException(nameof(message), "Remote volume must be finite and between 0 and 1.");
        return JsonSerializer.Serialize(message, message.GetType(), Options);
    }

    public static bool TryParse(string line, out SignalingMessage? message)
    {
        message = null;
        if (line.Length > MaxLineChars) return false;
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 });
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object || !Text(json, "type", out var type)) return false;
            switch (type)
            {
                case "hello" when Text(json, "deviceName", out var name):
                    message = new HelloMessage(name[..Math.Min(name.Length, 256)]);
                    break;
                case "offer" when Text(json, "sdp", out var offer): message = new OfferMessage(offer); break;
                case "answer" when Text(json, "sdp", out var answer): message = new AnswerMessage(answer); break;
                case "ice":
                    string? mid = null;
                    if (json.TryGetProperty("sdpMid", out var midValue) && midValue.ValueKind != JsonValueKind.Null)
                    {
                        if (midValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(mid = midValue.GetString())) return false;
                    }
                    if (!json.TryGetProperty("sdpMLineIndex", out var index) || index.ValueKind != JsonValueKind.Number ||
                        !index.TryGetInt32(out int number) || number < 0 || !Text(json, "candidate", out var candidate)) return false;
                    message = new IceMessage(mid, number, candidate);
                    break;
                case "bye": message = new ByeMessage(); break;
                case "volume" when json.TryGetProperty("action", out var actionValue):
                    if (actionValue.ValueKind != JsonValueKind.String) return false;
                    message = actionValue.GetString() switch { "up" => new VolumeActionMessage(RemoteVolumeAction.Up), "down" => new VolumeActionMessage(RemoteVolumeAction.Down), "muteToggle" => new VolumeActionMessage(RemoteVolumeAction.MuteToggle), _ => null };
                    break;
                case "getVolume": message = new GetVolumeMessage(); break;
                case "volumeState" when json.TryGetProperty("current", out var current) && current.TryGetInt32(out int currentValue) &&
                    json.TryGetProperty("max", out var max) && max.TryGetInt32(out int maxValue) && currentValue >= 0 && maxValue >= currentValue &&
                    json.TryGetProperty("muted", out var muted) && muted.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    bool available = true;
                    if (json.TryGetProperty("available", out var availableValue))
                    {
                        if (availableValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                        available = availableValue.GetBoolean();
                    }
                    message = new VolumeStateMessage(currentValue, maxValue, muted.GetBoolean(), available); break;
                case "volume" when !json.TryGetProperty("action", out _) && json.TryGetProperty("value", out var volume) && volume.ValueKind == JsonValueKind.Number &&
                    volume.TryGetDouble(out double level) && double.IsFinite(level) && level >= 0 && level <= 1:
                    message = new VolumeMessage(level); break;
                case "mute" when json.TryGetProperty("value", out var mute) && mute.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    message = new MuteMessage(mute.GetBoolean()); break;
            }
            return message is not null;
        }
        catch (JsonException) { return false; }
    }

    private static bool Text(JsonElement json, string key, out string value)
    {
        value = "";
        if (!json.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()!;
        return !string.IsNullOrWhiteSpace(value);
    }
}
