using System.Reflection;

namespace Umsatzschaetzung.App.Platform;

public static class Release
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    // mike stellt jeden getaggten Stand unter seiner Nummer bereit, jeden Stand von main unter "dev".
    public static string Docs { get; } = Version.Split('+')[0] switch
    {
        "" or "0.0.0" => "dev",
        var tagged => tagged,
    };
}
