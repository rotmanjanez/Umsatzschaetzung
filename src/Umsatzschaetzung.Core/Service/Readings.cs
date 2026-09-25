using System.Security.Cryptography;
using System.Text;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Service;

// What reading a document produced, before any rule touched it, kept by its bytes and the
// readers that made it, as the setup names them. A run that imports the same documents
// again replays them.
public sealed class Readings(string dir, string setup)
{
    readonly byte[] prefix = Encoding.UTF8.GetBytes(setup);

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
        hash.AppendData(prefix);
        hash.AppendData(data);
        return Path.Combine(dir, Convert.ToHexStringLower(hash.GetHashAndReset()) + ".json");
    }
}
