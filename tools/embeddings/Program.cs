using System.IO.Compression;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Suggest;

// The embedding cache of the shipped rules, warmed by indexing them once without a Gewerbe,
// which covers every ware: the program ships it and the page fetches it, instead of running
// the encoder over them first. Warmed beside the target and moved there whole, so two builds
// warming the same rules never leave a half-written one.
var target = Path.GetFullPath(args[0]);
var warm = Directory.CreateDirectory(Path.TrimEndingDirectorySeparator(target) + ".part" + Environment.ProcessId).FullName;
var db = Path.Combine(warm, "embeddings.db");
var rs = RuleStore.Seed();
using (var encoder = new Encoder(new OrtWeights(Path.Combine(AppContext.BaseDirectory, "models"))))
    await new Wares(encoder, new EmbeddingStore(warm)).Index(rs, "", CancellationToken.None);

using (var from = File.OpenRead(db))
using (var to = new BrotliStream(File.Create(db + ".br"), CompressionLevel.Optimal))
    from.CopyTo(to);

Directory.CreateDirectory(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target))!);
try
{
    Directory.Move(warm, target);
}
catch (IOException) when (File.Exists(Path.Combine(target, "embeddings.db")))
{
    Directory.Delete(warm, true);
}
