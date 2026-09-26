using System.Runtime.Versioning;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Web;

[assembly: SupportedOSPlatform("browser")]

// No reader, tagger or model runs in the page: invoices come in as e-invoices, wares are mapped by hand.
await Host.Start(Services.Local(new RuleStore("/work/rules", RuleStore.Seed()), new CaseStore("/work/cases"), Release.Version), args[0]);
