using System.Security.Cryptography;
using System.Text;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Service;

// What reading a document produced, before any rule touched it, kept by its bytes and the
// readers that made it. A run that imports the same documents again replays them.
public sealed class Readings(string dir)
{
    static readonly byte[] Setup = Encoding.UTF8.GetBytes($"{RapidOcr.Name}|{RapidOcr.MaxImageDimension}|{Scan.Dpi}|{Tagger.Name}");

    public OcrResp? Find(byte[] data)
    {
        var path = PathOf(data);
        return File.Exists(path) ? Json.Deserialize<OcrResp>(File.ReadAllBytes(path)) : null;
    }

    public void Keep(byte[] data, OcrResp reading)
    {
        Directory.CreateDirectory(dir);
        var path = PathOf(data);
        var temp = path + "." + Path.GetRandomFileName();
        File.WriteAllText(temp, Json.Serialize(reading));
        File.Move(temp, path, overwrite: true);
    }

    string PathOf(byte[] data)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Setup);
        hash.AppendData(data);
        return Path.Combine(dir, Convert.ToHexStringLower(hash.GetHashAndReset()) + ".json");
    }
}
