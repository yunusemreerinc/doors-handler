using DoorsHandler.Core.Dxl;

namespace DoorsHandler.Core;

/// <summary>
/// Offline client with a small sample database, for trying the UI on machines without DOORS.
/// </summary>
public sealed class DemoDoorsClient : IDoorsClient
{
    private static readonly DoorsItem[] Database =
    [
        Item(DoorsItemType.Project, "/Araç Projesi", "Örnek proje"),
        Item(DoorsItemType.Folder, "/Araç Projesi/01 Gereksinimler", ""),
        Item(DoorsItemType.Formal, "/Araç Projesi/01 Gereksinimler/Sistem Gereksinimleri", "SyRS"),
        Item(DoorsItemType.Formal, "/Araç Projesi/01 Gereksinimler/Yazılım Gereksinimleri", "SRS"),
        Item(DoorsItemType.Formal, "/Araç Projesi/01 Gereksinimler/Donanım Gereksinimleri", "HRS"),
        Item(DoorsItemType.Folder, "/Araç Projesi/02 Tasarım", ""),
        Item(DoorsItemType.Formal, "/Araç Projesi/02 Tasarım/Yazılım Mimarisi", "SAD"),
        Item(DoorsItemType.Descriptive, "/Araç Projesi/02 Tasarım/Ham Notlar", "İçe aktarılmış belge"),
        Item(DoorsItemType.Folder, "/Araç Projesi/03 Test", ""),
        Item(DoorsItemType.Formal, "/Araç Projesi/03 Test/Sistem Test Senaryoları", "STP"),
        Item(DoorsItemType.Link, "/Araç Projesi/03 Test/Doğrulama Bağlantıları", "Test → Gereksinim"),
        Item(DoorsItemType.Link, "/Araç Projesi/Türetme Bağlantıları", "Satisfies"),
        Item(DoorsItemType.Project, "/Uydu Projesi", ""),
        Item(DoorsItemType.Formal, "/Uydu Projesi/Görev Gereksinimleri", "MRD"),
        Item(DoorsItemType.Formal, "/Uydu Projesi/Arayüz Kontrol Dokümanı", "ICD"),
        Item(DoorsItemType.Folder, "/Şablonlar", "Kurumsal şablonlar"),
        Item(DoorsItemType.Formal, "/Şablonlar/Gereksinim Şablonu", ""),
    ];

    public bool IsConnected { get; private set; }

    public async Task<DoorsSessionInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        IsConnected = true;
        return new DoorsSessionInfo("demo", "Demo Veritabanı", "9.7 (demo)");
    }

    public async Task<DoorsListing> ListItemsAsync(string rootPath, bool recursive, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Önce DOORS'a bağlanın.");
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);

        var root = DxlScripts.NormalizePath(rootPath);
        if (root != "/" && !Database.Any(i => i.FullName == root && !i.IsModule))
        {
            throw new DoorsException(DoorsErrorKind.RootNotFound, $"DOORS'ta '{root}' klasörü/projesi bulunamadı ya da erişim izniniz yok.");
        }

        var prefix = root == "/" ? "/" : root + "/";
        var items = Database
            .Where(i => i.FullName.StartsWith(prefix, StringComparison.Ordinal))
            .Where(i => recursive || i.ParentPath == root)
            .ToList();
        return new DoorsListing(items, []);
    }

    public void Dispose() => IsConnected = false;

    private static DoorsItem Item(DoorsItemType type, string path, string description) =>
        new(type, path[(path.LastIndexOf('/') + 1)..], path, Id(path), description);

    private static string Id(string path) => ((uint)StableHash(path)).ToString("x8");

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in s) h = h * 31 + c;
            return h;
        }
    }
}
