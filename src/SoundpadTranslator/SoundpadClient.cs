using System.IO.Pipes;
using System.Text;
using System.Xml.Linq;

namespace SoundpadTranslator;

sealed record SoundInfo(int Index, string Title, string Url)
{
    public override string ToString() => $"{Index}. {Title}";
}

/// <summary>Client for Soundpad's Remote Control API on the \\.\pipe\sp_remote_control named pipe.</summary>
sealed class SoundpadClient
{
    const string PipeName = "sp_remote_control";
    const int ConnectTimeoutMs = 1000;

    readonly Lock gate = new();

    public string Send(string command)
    {
        lock (gate)
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(ConnectTimeoutMs);
            try
            {
                pipe.ReadMode = PipeTransmissionMode.Message;
            }
            catch (IOException)
            {
                // Byte-mode pipe: a single large read below is enough for our commands.
            }

            var request = Encoding.UTF8.GetBytes(command);
            pipe.Write(request);
            pipe.Flush();

            using var response = new MemoryStream();
            var buffer = new byte[64 * 1024];
            do
            {
                int read = pipe.Read(buffer);
                if (read == 0)
                    break;
                response.Write(buffer, 0, read);
            } while (pipe.ReadMode == PipeTransmissionMode.Message && !pipe.IsMessageComplete);

            return Encoding.UTF8.GetString(response.ToArray()).TrimEnd('\0');
        }
    }

    static void EnsureOk(string command, string response)
    {
        if (!response.StartsWith("R-200", StringComparison.Ordinal))
            throw new IOException($"Soundpad: {command} -> {response}");
    }

    public void PlaySound(int index)
    {
        var command = $"DoPlaySound({index})";
        EnsureOk(command, Send(command));
    }

    public void StopSound() => EnsureOk("DoStopSound()", Send("DoStopSound()"));

    public void TogglePause() => EnsureOk("DoTogglePause()", Send("DoTogglePause()"));

    public IReadOnlyList<SoundInfo> GetSounds()
    {
        var xml = Send("GetSoundlist()");
        if (xml.StartsWith("R-", StringComparison.Ordinal))
            throw new IOException($"Soundpad: GetSoundlist() -> {xml}");

        return XDocument.Parse(xml)
            .Descendants("Sound")
            .Select(e =>
            {
                var url = (string?)e.Attribute("url") ?? "";
                var title = (string?)e.Attribute("title");
                if (string.IsNullOrWhiteSpace(title))
                    title = Path.GetFileNameWithoutExtension(url);
                return new SoundInfo((int?)e.Attribute("index") ?? 0, title, url);
            })
            .Where(s => s.Index > 0)
            .ToList();
    }
}
