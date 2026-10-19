# DEMİRBEY FINANCE AI

Logo GO 3 entegre finans, çek, kredi kartı, müşteri riski ve nakit akışı yönetim sistemi.

## Klasörler

| Klasör | İçerik |
|---|---|
| `Pazartesi-GO3-Kurulum/` | **Şirkette yapılacaklar:** kontrol listesi (PDF), Logo'ya salt okunur bağlantı testi ve keşif aracı (`1-Calistir.bat`), salt okunur SQL kullanıcısı betiği |
| `Prototip/index.html` | Onaylanan tıklanabilir prototip (örnek veri, tek dosya). Çift tıklayıp tarayıcıda açılır. |
| `Dokumanlar/` | Logo GO 3 entegrasyon analizi |
| `Dokumanlar/SQL/` | Salt okunur Logo kullanıcısı ve keşif betiği (yalnızca SELECT) |
| `Sunucu/` | ASP.NET Core (.NET 10) API + hesaplama servisleri + xUnit testleri – tüm cihazların bağlandığı merkez |
| `Ortak-Uygulama-Kodu/` | Tauri 2 + React + TypeScript – Windows, Mac ve iPad'in **ortak** arayüz kodu |
| `Windows/` | Windows kurulum dosyası üretme adımları ve hazır kurulum dosyaları |
| `Mac/` | MacBook uygulaması üretme adımları |
| `iPad/` | iPad uygulaması üretme adımları |

## Sunucu (ASP.NET Core)

```
Sunucu/src/Demirbey.Domain          Modeller (çek, kart, kredi, nakit olayı)
Sunucu/src/Demirbey.Application     Hesaplama servisleri
  Cheques.cs           Logo durum → kategori, ağırlıklı vade, çifte sayım koruması
  CashFlow.cs          7/15/30/60/90/180 gün nakit akışı, ilk nakit açığı
  WeeklyReport.cs      Gün gün haftalık rapor + iskonto/teminat çekleri + WhatsApp metni
  Notifications.cs     7/3/1/0 gün hatırlatmaları, toplu kart özeti, "ödeme bilgisi gir" hatırlatması
  SupplierPayables.cs  Tedarikçi: vade girince önerilen çek/ödeme tarihi
  CustomerRisk.cs      Müşteri limiti, risk sınıfı, satışı durdur / limit açıldı
  CustomerOverdue.cs   Fatura bazında vade aşımı, vade farkı, proforma
  InvestmentVault.cs   Yatırım kasası: ortalama maliyet, piyasa değeri, TCMB kurları, kasa şifresi
  CalendarSync.cs      Google Takvim: eksik ödemeleri ekler, var olanlara dokunmaz
  StockRisk.cs         Stok = GO 3 giriş − çıkış; alım noktası / risk seviyesi uyarıları
  BankLimits.cs        Findeks banka limit/risk, revize vadesi uyarıları, KMH ile nakit açığı karşılama
  DocumentImport.cs    Findeks PDF okuyucu (bankalar logodan tanınır), banka ödeme planı okuyucu
  FinanceCosts.cs      İskonto kesintisi, KMH devre sonu faizi, kredi faizi, alacak zararları, yıl sonu raporu, muaf cariler (Çalışkanlar)
Sunucu/src/Demirbey.Infrastructure  Logo salt okunur okuyucu (Dapper), örnek veri
Sunucu/src/Demirbey.Api             REST uç noktaları
Sunucu/tests/Demirbey.Tests         Hesaplama testleri
```

Çalıştırma (.NET 10 SDK gerekir):

```bash
cd Sunucu && dotnet test
cd Sunucu/src/Demirbey.Api && dotnet run   # http://localhost:5080/api/health
```

`appsettings.json → Logo:ConnectionString` boşsa örnek veri kullanılır. Doluysa yalnızca `demirbey_ro` (salt okunur) kullanıcısıyla bağlanılmalıdır.

## Windows / Mac / iPad istemcisi (ortak kod)

Ayrıntılı adımlar `Windows/`, `Mac/` ve `iPad/` klasörlerindeki README dosyalarında.

```bash
cd Ortak-Uygulama-Kodu && npm install && npm run dev  # tarayıcıda http://localhost:5173
npm run tauri dev                                     # masaüstü penceresi (Rust gerekir)
npm run tauri build                                   # Windows: .msi/.exe · macOS: .dmg
npm run tauri ios init && npm run tauri ios build     # iPad (Xcode + Apple Developer hesabı)
```

Şu an istemcide **Ana Panel** ve **Haftalık Rapor** canlı API'ye bağlı; diğer ekranlar prototipten sırayla taşınacak.

## Mimari: her cihaz eşzamanlı

```
Logo GO 3 (Windows · SQL Server)
        │  salt okunur senkron (5–30 dk)
        ▼
DEMİRBEY sunucusu (ASP.NET Core + PostgreSQL)  ──►  WhatsApp Business Cloud API
        │  HTTPS + anlık bildirim (SignalR)
        ├── Windows uygulaması
        ├── MacBook uygulaması
        ├── iPad uygulaması
        └── Mobil web paneli
```

Mac ve iPad Logo'ya doğrudan bağlanmaz. Logo'ya yalnızca sunucu bağlanır, tüm cihazlar aynı sunucuyu kullanır. Bu yüzden biri bir ödeme girdiğinde diğer cihazlar da aynı anda günceller.

## Güvenlik notları

- Logo'ya yazma yok: veritabanı kullanıcısı `db_datareader`, kodda `EnsureSelect` denetimi, `ApplicationIntent=ReadOnly`.
- Şahsi finans verileri ayrı şemada, yalnızca sahibi ve sistem yöneticisi görür.
- **Kredi kartı numarası:** veritabanında AES-256 ile şifreli saklanır (anahtar Windows DPAPI / anahtar kasasında). Ekranda maskeli gösterilir, açmak için yeniden kimlik doğrulama istenir. WhatsApp, rapor ve loglarda yalnızca son 4 hane yer alır. **CVV, son kullanma + CVV birlikte ve kart şifresi hiçbir zaman istenmez ve saklanmaz.**
- WhatsApp'taki "Ödendi" yanıtı kullanıcı beyanıdır; banka/Logo kaydıyla doğrulanana kadar kesinleşmiş sayılmaz.
