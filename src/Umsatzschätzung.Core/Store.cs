namespace Umsatzschätzung;

// Der Speicher selbst ist nicht zu erreichen: Datei gesperrt, Platte voll, Rechte fehlen.
public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
