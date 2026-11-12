# doors-handler

IBM DOORS (Classic, 9.x) istemcisine bağlanıp veritabanındaki **modülleri listeleyen** Windows masaüstü uygulaması (WPF, .NET 8).

## Özellikler

- Makinede çalışan DOORS istemcisine COM üzerinden bağlanır; kullanıcı, veritabanı ve DOORS sürümünü gösterir.
- Seçilen klasör/projeden (ya da `/` ile tüm veritabanından) başlayarak **Formal, Link ve Descriptive** modülleri listeler; istenirse alt klasörleri de tarar.
- Tür filtresi (tüm modüller / formal / link / descriptive / klasörler) ve ad, yol, açıklama veya Unique ID içinde anlık arama.
- Klasör ya da projeye çift tıklayınca onun içine girer, modüle çift tıklayınca tam yolunu panoya kopyalar; `↑` ile üst klasöre çıkılır.
- **Seçili modüllerin içeriğini JSON olarak dışa aktarır**: Ctrl/Shift ile bir veya birden fazla formal modül seçilir; her modül DOORS'ta salt okunur açılır, tüm nesneleri (başlık, metin, seviye, üst nesne, oluşturma/değiştirme bilgisi ve kullanıcı tanımlı öznitelikler) tek bir JSON dosyasına yazılır. Ayrıntılar aşağıda.
- Görünen listeyi CSV olarak dışa aktarır (Excel'de Türkçe karakterler doğru görünsün diye UTF-8 BOM ve `;` ayraç kullanılır).
- Erişim izni olmayan klasörler taramayı durdurmaz; uyarı olarak ayrıca gösterilir.
- Bağlantı ya da listeleme başarısız olursa pencerenin üstünde bir hata bandı çıkar: **ne olduğu**, **nedeni** ve **ne yapılabileceği** Türkçe açıklanır; *Tekrar dene*, *Ayrıntıları kopyala* (destek ekibine göndermek için) ve teknik ayrıntılar (HRESULT, yetki, mimari) aynı yerdedir.
- DOORS iki dakika içinde yanıt vermezse (ör. oturum açma penceresi arkada kaldıysa) bağlantı zaman aşımıyla sonlanır; bağlanırken *İptal* ile beklemeyi bırakabilirsiniz.
- **Demo modu**: DOORS kurulu olmayan bir makinede arayüzü örnek verilerle denemek için.

## Gereksinimler

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (geliştirme için .NET 8 SDK)
- Aynı makinede kurulu ve lisanslı **IBM DOORS 9.x istemcisi** (demo modu için gerekmez)

## Çalıştırma

```bash
dotnet run --project src/DoorsHandler.Desktop
```

Demo verisiyle açmak için:

```bash
dotnet run --project src/DoorsHandler.Desktop -- --demo
```

Dağıtılabilir bir klasör oluşturmak için:

```bash
dotnet publish src/DoorsHandler.Desktop -c Release -r win-x64 --self-contained false -o publish
```

Testler (Windows, Linux veya macOS'ta çalışır):

```bash
dotnet test
```

## Kullanım

1. **Bağlantı** listesinden *IBM DOORS (yerel istemci)* seçili iken **Bağlan**'a basın.
   DOORS açık değilse Windows onu başlatır ve DOORS'un kendi oturum açma penceresi gelir; giriş yaptıktan sonra uygulama bağlanır.
2. Bağlantı kurulunca **Kök klasör** alanındaki yol (varsayılan `/`) otomatik olarak listelenir.
3. Başka bir yer için yolu yazıp Enter'a basın (ör. `/Araç Projesi/Gereksinimler`) ya da listede bir klasöre çift tıklayın.
4. Büyük veritabanlarında tüm ağacı taramak zaman alabilir; *Alt klasörleri de tara* seçeneğini kapatarak yalnızca o klasörün içeriğini alabilirsiniz.

## JSON dışa aktarma

1. Listeden bir veya birden fazla **formal** modül seçin (Ctrl ile tek tek, Shift ile aralık, Ctrl+A ile tümü).
2. **Seçili modülleri JSON'a aktar…** düğmesine (ya da sağ tık menüsündeki aynı komuta) basıp dosya adını seçin.
3. Modüller sırayla okunur; durum çubuğu hangi modülün okunduğunu gösterir, *İptal* ile durdurulabilir (iptal edilirse dosya yazılmaz).

Davranış:

- Modüller DOORS'ta **salt okunur ve görünmez** açılır; okuma bittiğinde kapatılır. Kullanıcının zaten açık tuttuğu bir modüle dokunulmaz, açık kalır.
- Görünümdeki filtreler dikkate alınmaz; silinmiş (soft-deleted) nesneler hariç **tüm nesneler** belge sırasıyla yazılır.
- Link ve Descriptive modüller atlanır (nesne içerikleri yoktur); kaç öğenin atlandığı durum çubuğunda yazar.
- Açılamayan bir modül (silinmiş, izin yok, kilitli) dışa aktarmayı durdurmaz: JSON'da o modül için `error` alanı yazılır ve uyarılar listesinde gösterilir. Hiçbir modül okunamazsa dosya yazılmaz ve nedeni hata bandında açıklanır.
- DOORS bağlantısı koparsa dışa aktarma durur, dosya yazılmaz.

Dosya biçimi (UTF-8, Türkçe karakterler olduğu gibi):

```json
{
  "formatVersion": 1,
  "exportedAt": "2026-11-12T22:40:00+03:00",
  "database": "ProdDB",
  "exportedBy": "jdoe",
  "doorsVersion": "9.7.2.3",
  "modules": [
    {
      "name": "Yazılım Gereksinimleri",
      "fullName": "/Araç Projesi/01 Gereksinimler/Yazılım Gereksinimleri",
      "uniqueId": "00001a2b",
      "description": "SRS",
      "attributeDefinitions": [ { "name": "Öncelik", "type": "Priority" } ],
      "objectCount": 2,
      "objects": [
        {
          "absoluteNumber": 1, "identifier": "SRS-1", "level": 1,
          "heading": "1 Giriş", "text": "",
          "createdBy": "jdoe", "createdOn": "01.10.2026",
          "lastModifiedBy": "jdoe", "lastModifiedOn": "05.11.2026",
          "attributes": { "Öncelik": "" }
        },
        {
          "absoluteNumber": 2, "identifier": "SRS-2", "level": 2, "parentAbsoluteNumber": 1,
          "heading": "", "text": "Sistem açılışta 2 saniye içinde hazır olmalıdır.",
          "createdBy": "jdoe", "createdOn": "01.10.2026",
          "lastModifiedBy": "amy", "lastModifiedOn": "07.11.2026",
          "attributes": { "Öncelik": "Yüksek" }
        }
      ]
    },
    { "name": "Silinmiş", "fullName": "/Araç Projesi/Silinmiş", "uniqueId": "…", "error": "'/Araç Projesi/Silinmiş' modülü açılamadı …" }
  ]
}
```

- `level` ve `parentAbsoluteNumber` ile modül ağacı yeniden kurulabilir (en üst seviyedeki nesnelerde `parentAbsoluteNumber` yoktur).
- `attributes` içinde modülün **kullanıcı tanımlı** nesne öznitelikleri DOORS'taki adlarıyla yer alır; sistem öznitelikleri yalnızca yukarıdaki alanlar olarak yazılır.
- `text` düz metindir (zengin metin biçimlendirmesi ve OLE nesneleri dahil edilmez). Tarihler DOORS'un gösterdiği biçimdedir.

## Nasıl çalışır?

DOORS istemcisi `DOORS.Application` adıyla bir COM otomasyon sunucusu sağlar. Uygulama bu nesneye geç bağlama (late binding) ile bağlanır, böylece derleme için DOORS tür kütüphanesine ya da interop DLL'ine ihtiyaç yoktur:

1. `runStr(dxl)` ile DOORS içinde bir DXL betiği çalıştırılır.
2. Betik sonucu `oleSetResult(...)` ile döndürür, uygulama bunu `result` özelliğinden okur.
3. Sonuç sekmeyle ayrılmış basit bir satır protokolüdür (`OK`, `ERR`, `S`, `I`, `W` satırları); alan değerleri kaçışlanır (escape), böylece açıklamalardaki sekme ya da satır sonları protokolü bozmaz.

COM çağrıları ayrı bir STA iş parçacığında yapılır, böylece DOORS uzun bir tarama yaparken arayüz donmaz.

```
src/
  DoorsHandler.Core/        Platformdan bağımsız mantık (Windows gerektirmez, test edilebilir)
    Dxl/DxlScripts.cs         DOORS'a gönderilen DXL betikleri
    Dxl/DxlResultParser.cs    DXL çıktısının ayrıştırılması
    DxlDoorsClient.cs         IDoorsClient: betikleri bir IDxlExecutor ile çalıştırır
    DemoDoorsClient.cs        DOORS olmadan deneme için örnek veri
    CsvExporter.cs            CSV dışa aktarma
    ModuleJsonExporter.cs     Modül içeriğini okuyup JSON'a yazma
  DoorsHandler.Desktop/     WPF uygulaması
    Doors/ComDxlExecutor.cs   DOORS COM köprüsü (DOORS.Application)
    Doors/StaWorker.cs        COM çağrıları için ayrı STA iş parçacığı
    ViewModels/MainViewModel.cs
    MainWindow.xaml
tests/
  DoorsHandler.Core.Tests/  xUnit testleri
```

Yeni bir DOORS işlemi eklemek için `DxlScripts` içine betiği, `DxlResultParser` içine ayrıştırmayı, `IDoorsClient` içine metodu eklemek yeterlidir; COM katmanına dokunmak gerekmez.

## Sorun giderme

Bir hata olduğunda uygulama nedenini kendisi sınıflandırıp hata bandında gösterir. Olası nedenler:

| Hata bandındaki neden | Ne zaman olur | Ne yapmalı |
| --- | --- | --- |
| DOORS istemcisi bulunamadı | `DOORS.Application` COM sunucusu kayıtlı değil (`REGDB_E_CLASSNOTREG`) | DOORS'u kurun ya da kurulumu onarın. Denemek için *Demo verisi*ni seçin. |
| DOORS başlatılamadı | Windows DOORS'u açamadı ya da çalışan DOORS'a bağlanamadı (`CO_E_SERVER_EXEC_FAILURE`) | DOORS'u kendiniz açıp oturum açın, sonra *Tekrar dene*. |
| Erişim reddedildi | Biri yönetici olarak, diğeri normal çalışıyor (`E_ACCESSDENIED`) | DOORS'u ve bu uygulamayı aynı yetkiyle çalıştırın. |
| DOORS meşgul | DOORS'ta açık bir iletişim kutusu ya da çalışan başka bir DXL var (`RPC_E_CALL_REJECTED`) | DOORS'taki açık pencereleri kapatın, sonra *Tekrar dene*. |
| Zaman aşımı | DOORS 2 dakika içinde yanıt vermedi; çoğunlukla oturum açma penceresi arkada kalmıştır | DOORS penceresini bulup oturum açın, sonra *Tekrar dene*. |
| Bağlantı koptu | DOORS bağlandıktan sonra kapatıldı ya da çöktü | DOORS'u yeniden açın, sonra *Tekrar dene*. |
| DOORS komutu çalıştıramadı | DXL betiği hata verdi ya da boş yanıt döndü; DOORS'ta bir DXL hata penceresi açılmış olabilir | Hata metnini not edin, *Ayrıntıları kopyala* ile destek ekibine iletin. |
| Klasör bulunamadı | Kök klasör yolu yanlış ya da görme izniniz yok | Yolu `/Proje/Klasör` biçiminde kontrol edin ya da `/` yazın. |

*Teknik ayrıntılar* bölümünde zaman, hata türü, HRESULT, uygulamanın 32/64-bit olup olmadığı ve yönetici olarak çalışıp çalışmadığı yer alır.

## Kapsam dışı

DOORS Next (DNG, Jazz/ELM) DXL ve COM yerine OSLC/REST API kullandığı için bu sürümde desteklenmiyor. `IDoorsClient` arayüzü, ileride OSLC tabanlı bir istemcinin aynı arayüze eklenebileceği şekilde tasarlandı.
