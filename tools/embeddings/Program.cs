using System.IO.Compression;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Suggest;

// The embedding cache of the shipped rules, warmed by indexing them once without a Gewerbe,
// which covers every ware: the page fetches it instead of running the encoder in the browser.
var db = Path.Combine(args[0], "embeddings.db");
Directory.CreateDirectory(args[0]);
File.Delete(db);
var rs = RuleStore.Seed();
using (var encoder = new Encoder(new OrtWeights(Path.Combine(AppContext.BaseDirectory, "models"))))
    await new EncoderRanking(encoder, new EmbeddingStore(args[0])).Rank(rs, "", new InvoiceLine { Name = "Pils" }, DateOnly.FromDateTime(DateTime.Today), 1);

using var from = File.OpenRead(db);
using var to = new BrotliStream(File.Create(db + ".br"), CompressionLevel.Optimal);
from.CopyTo(to);
