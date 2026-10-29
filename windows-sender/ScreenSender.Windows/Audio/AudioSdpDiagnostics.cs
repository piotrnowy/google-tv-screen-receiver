namespace ScreenSender.Windows.Audio;

public static class AudioSdpDiagnostics
{
    public static void LogAndValidate(string sdp, bool offer, Action<string>? log = null)
    {
        log ??= Console.WriteLine;
        string label = offer ? "Offer" : "Answer";
        var section = new List<string>();
        string direction = "sendrecv";
        bool inAudio = false, seenMedia = false;
        foreach (string line in sdp.Split('\n').Select(value => value.TrimEnd('\r')))
        {
            if (line.StartsWith("m=", StringComparison.Ordinal))
            {
                if (inAudio) break;
                seenMedia = true;
                inAudio = line.StartsWith("m=audio ", StringComparison.Ordinal);
            }
            if (!seenMedia || inAudio)
                if (line is "a=sendonly" or "a=recvonly" or "a=sendrecv" or "a=inactive") direction = line[2..];
            if (inAudio) section.Add(line);
        }
        log($"[Audio SDP] {label}:");
        foreach (string line in section.Where(value => value.StartsWith("m=", StringComparison.Ordinal) ||
                     value.StartsWith("a=mid:", StringComparison.Ordinal) || value.StartsWith("a=rtpmap:", StringComparison.Ordinal) ||
                     value.StartsWith("a=fmtp:", StringComparison.Ordinal) || value is "a=sendonly" or "a=recvonly" or "a=sendrecv" or "a=inactive")) log(line);
        log($"[Audio SDP] {label} effective direction: {direction}");
        if (section.Count == 0) Fail("no m=audio line");
        var media = section[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (media.Length < 4 || !int.TryParse(media[1].Split('/')[0], out int port)) Fail("invalid audio m-line");
        else if (port == 0) Fail("m=audio port 0 (audio rejected)");
        if (direction == "inactive") Fail("a=inactive (audio disabled)");
        if (offer ? direction != "sendonly" : direction is not ("recvonly" or "sendrecv"))
            Fail($"incompatible direction {direction}; expected {(offer ? "sendonly" : "recvonly/sendrecv")}");
        bool opus = section.Any(line =>
        {
            if (!line.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase)) return false;
            string[] mapping = line[9..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return mapping.Length == 2 && mapping[1].Equals("opus/48000/2", StringComparison.OrdinalIgnoreCase) && media.Skip(3).Contains(mapping[0]);
        });
        if (!opus) Fail("no compatible OPUS/48000/2 codec in the audio m-line");

        void Fail(string reason)
        {
            log($"[Audio SDP] {label} rejected: {reason}");
            throw new InvalidDataException($"Audio SDP {label}: {reason}");
        }
    }
}
