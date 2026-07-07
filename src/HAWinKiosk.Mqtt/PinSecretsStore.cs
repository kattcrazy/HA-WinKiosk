using System.IO;
using System.Text;
using System.Text.Json;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk.Mqtt;

/// <summary>PIN + reset Q&amp;A kept out of settings.yaml (base64-encoded JSON).</summary>
public static class PinSecretsStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HA-WinKiosk",
        "pin-secrets.json");

    public static string FilePath => StorePath;

    private sealed class EncodedSecrets
    {
        public string? Pin { get; set; }
        public string? PinResetQuestion { get; set; }
        public string? PinResetAnswer { get; set; }
    }

    public static bool ApplyTo(KioskConfig kiosk)
    {
        if (File.Exists(StorePath))
        {
            var secrets = LoadEncoded();
            if (secrets != null)
            {
                kiosk.Pin = Decode(secrets.Pin);
                kiosk.PinResetQuestion = Decode(secrets.PinResetQuestion);
                kiosk.PinResetAnswer = Decode(secrets.PinResetAnswer);
            }

            return false;
        }

        if (!HasSecrets(kiosk))
            return false;

        Persist(kiosk);
        return true;
    }

    public static void Persist(KioskConfig kiosk)
    {
        try
        {
            if (!HasSecrets(kiosk))
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
                return;
            }

            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var encoded = new EncodedSecrets
            {
                Pin = Encode(kiosk.Pin),
                PinResetQuestion = Encode(kiosk.PinResetQuestion),
                PinResetAnswer = Encode(kiosk.PinResetAnswer)
            };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(encoded));
        }
        catch
        {
            // Best-effort; secrets remain in memory for this session.
        }
    }

    private static EncodedSecrets? LoadEncoded()
    {
        try
        {
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<EncodedSecrets>(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasSecrets(KioskConfig kiosk) =>
        !string.IsNullOrEmpty(kiosk.Pin)
        || !string.IsNullOrWhiteSpace(kiosk.PinResetQuestion)
        || !string.IsNullOrWhiteSpace(kiosk.PinResetAnswer);

    private static string? Encode(string? value) =>
        string.IsNullOrEmpty(value) ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string? Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch
        {
            return null;
        }
    }
}
