using System.Globalization;
using System.Text;

namespace DoorsHandler.Core;

/// <summary>Categories of DOORS failures, each with its own explanation for the user.</summary>
public enum DoorsErrorKind
{
    Unknown = 0,

    /// <summary>The DOORS COM server is not registered: the DOORS client is not installed on this machine.</summary>
    NotInstalled,

    /// <summary>Windows found DOORS but could not start it or attach to it.</summary>
    StartFailed,

    /// <summary>Windows refused access to the DOORS process (usually an administrator/normal user mismatch).</summary>
    AccessDenied,

    /// <summary>DOORS is running but is blocked by an open dialog or another script.</summary>
    Busy,

    /// <summary>DOORS did not answer in time (often because its login window is waiting).</summary>
    Timeout,

    /// <summary>DOORS was closed or crashed after we attached to it.</summary>
    ConnectionLost,

    /// <summary>The DXL script did not run or returned an unexpected result.</summary>
    ScriptFailed,

    /// <summary>The requested folder/project does not exist or is not visible to the user.</summary>
    RootNotFound,
}

/// <summary>Maps COM HRESULTs returned by the DOORS automation server to <see cref="DoorsErrorKind"/>.</summary>
public static class DoorsErrorClassifier
{
    public const int RegDbClassNotRegistered = unchecked((int)0x80040154); // REGDB_E_CLASSNOTREG
    public const int ServerExecFailure = unchecked((int)0x80080005);       // CO_E_SERVER_EXEC_FAILURE
    public const int FileNotFound = unchecked((int)0x80070002);            // ERROR_FILE_NOT_FOUND
    public const int AccessDenied = unchecked((int)0x80070005);            // E_ACCESSDENIED
    public const int CallRejected = unchecked((int)0x80010001);            // RPC_E_CALL_REJECTED
    public const int ServerCallRetryLater = unchecked((int)0x8001010A);    // RPC_E_SERVERCALL_RETRYLATER
    public const int ServerUnavailable = unchecked((int)0x800706BA);       // RPC_S_SERVER_UNAVAILABLE
    public const int CallFailed = unchecked((int)0x800706BE);              // RPC_S_CALL_FAILED
    public const int Disconnected = unchecked((int)0x80010108);            // RPC_E_DISCONNECTED
    public const int ServerDied = unchecked((int)0x80010114);              // RPC_E_SERVER_DIED_DNE

    /// <param name="hresult">The HRESULT of the COM exception.</param>
    /// <param name="duringStartup">True while creating the DOORS COM object, false for calls on an existing one.</param>
    public static DoorsErrorKind FromHResult(int hresult, bool duringStartup) => hresult switch
    {
        RegDbClassNotRegistered => DoorsErrorKind.NotInstalled,
        ServerExecFailure or FileNotFound => DoorsErrorKind.StartFailed,
        AccessDenied => DoorsErrorKind.AccessDenied,
        CallRejected or ServerCallRetryLater => DoorsErrorKind.Busy,
        ServerUnavailable or CallFailed or Disconnected or ServerDied =>
            duringStartup ? DoorsErrorKind.StartFailed : DoorsErrorKind.ConnectionLost,
        _ => duringStartup ? DoorsErrorKind.StartFailed : DoorsErrorKind.ScriptFailed,
    };
}

/// <summary>What the user is told when an operation fails: what happened, why, and what to do.</summary>
public sealed record DoorsErrorReport(
    string Title,
    DoorsErrorKind Kind,
    string Reason,
    string Suggestion,
    string Details,
    DateTime OccurredAt)
{
    /// <summary>Plain-text version for the clipboard, e.g. to send to IT support.</summary>
    public string ToClipboardText() =>
        $"{Title}\r\nNeden: {Reason}\r\nNe yapabilirsiniz: {Suggestion}\r\n\r\nTeknik ayrıntılar:\r\n{Details}";

    /// <summary>Builds a report for an exception thrown while performing <paramref name="title"/>.</summary>
    /// <param name="title">What failed, e.g. "DOORS bağlantısı kurulamadı".</param>
    /// <param name="exception">The failure.</param>
    /// <param name="context">Extra "name: value" lines for the technical details (mode, root path, ...).</param>
    public static DoorsErrorReport Create(
        string title,
        Exception exception,
        IEnumerable<KeyValuePair<string, string>>? context = null,
        DateTime? occurredAt = null)
    {
        var kind = exception is DoorsException de ? de.Kind : DoorsErrorKind.Unknown;
        var (reason, suggestion) = Explain(kind);

        // These reasons are best explained by the specific message (which folder, which unexpected error).
        if (kind == DoorsErrorKind.RootNotFound) reason = exception.Message;
        else if (kind == DoorsErrorKind.Unknown) reason = $"{reason} {exception.Message}";

        var when = occurredAt ?? DateTime.Now;
        return new DoorsErrorReport(title, kind, reason, suggestion, BuildDetails(exception, kind, context, when), when);
    }

    public static (string Reason, string Suggestion) Explain(DoorsErrorKind kind) => kind switch
    {
        DoorsErrorKind.NotInstalled => (
            "Bu bilgisayarda IBM DOORS istemcisi bulunamadı (DOORS.Application COM sunucusu kayıtlı değil).",
            "DOORS istemcisinin kurulu olduğundan emin olun. Kuruluysa kurulumu onarın ya da BT ekibinizden COM kaydını yenilemesini isteyin. DOORS olmadan denemek için bağlantı türünden \"Demo verisi\"ni seçin."),
        DoorsErrorKind.StartFailed => (
            "Windows DOORS istemcisini başlatamadı ya da çalışan DOORS'a bağlanamadı.",
            "DOORS'u kendiniz açıp oturum açın, sonra \"Tekrar dene\"ye basın. DOORS ile bu uygulama farklı yetkilerle çalışıyorsa (biri \"Yönetici olarak\") ikisini de aynı şekilde açın."),
        DoorsErrorKind.AccessDenied => (
            "Windows, DOORS işlemine erişimi reddetti. Genellikle uygulamalardan biri yönetici olarak, diğeri normal kullanıcı olarak çalıştığında olur.",
            "DOORS'u ve bu uygulamayı aynı yetkiyle çalıştırın (ikisi de normal ya da ikisi de \"Yönetici olarak\"), sonra tekrar deneyin."),
        DoorsErrorKind.Busy => (
            "DOORS meşgul: açık bir pencere (oturum açma, uyarı, ayar penceresi) ya da çalışan başka bir DXL betiği komutu kabul etmiyor.",
            "DOORS penceresine geçip açık iletişim kutularını kapatın veya oturum açmayı tamamlayın, sonra \"Tekrar dene\"ye basın."),
        DoorsErrorKind.Timeout => (
            "DOORS belirlenen süre içinde yanıt vermedi. DOORS oturum açma penceresi başka pencerelerin arkasında bekliyor olabilir.",
            "Görev çubuğundan DOORS penceresini bulup oturum açın, sonra \"Tekrar dene\"ye basın."),
        DoorsErrorKind.ConnectionLost => (
            "DOORS istemcisi bağlantı kurulduktan sonra kapatıldı ya da çöktü.",
            "DOORS'u yeniden açıp oturum açın, sonra \"Tekrar dene\"ye basın."),
        DoorsErrorKind.ScriptFailed => (
            "DOORS komutu çalıştıramadı ya da beklenmeyen bir yanıt döndürdü. DOORS içinde bir DXL hata penceresi açılmış olabilir.",
            "DOORS penceresinde bir hata mesajı varsa metnini not edip pencereyi kapatın. Sorun sürerse \"Ayrıntıları kopyala\" ile bilgileri destek ekibine iletin."),
        DoorsErrorKind.RootNotFound => (
            "İstenen klasör ya da proje bulunamadı.",
            "Kök klasör yolunu kontrol edin (ör. /Proje/Gereksinimler) ya da veritabanının tamamı için / yazın. Klasör varsa görme izniniz olmayabilir."),
        _ => (
            "Beklenmeyen bir hata oluştu.",
            "\"Tekrar dene\"ye basın. Sorun sürerse \"Ayrıntıları kopyala\" ile bilgileri destek ekibine iletin."),
    };

    private static string BuildDetails(
        Exception exception,
        DoorsErrorKind kind,
        IEnumerable<KeyValuePair<string, string>>? context,
        DateTime when)
    {
        var sb = new StringBuilder();
        sb.Append("Zaman: ").AppendLine(when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.Append("Hata türü: ").AppendLine(kind.ToString());
        foreach (var (key, value) in context ?? [])
        {
            sb.Append(key).Append(": ").AppendLine(value);
        }

        var depth = 0;
        for (var e = exception; e is not null; e = e.InnerException, depth++)
        {
            sb.Append(depth == 0 ? "Hata: " : "İç hata: ")
              .Append(e.GetType().Name)
              .Append(" (HRESULT 0x").Append(e.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append("): ")
              .AppendLine(e.Message.Trim());
        }

        return sb.ToString().TrimEnd();
    }
}
