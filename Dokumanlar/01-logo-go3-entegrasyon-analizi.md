# 01 · Logo GO 3 Entegrasyon Analizi

> Durum: **Taslak – sahada doğrulanacak.** Bu belge, Logo GO 3'ün genel veritabanı yapısına dayanır. Tablo ve alan adları Logo sürümüne, firma/dönem numarasına ve kuruluma göre değişebilir. Canlı bağlantıdan önce `sql/01-logo-kesif.sql` betiği müşterinin sunucusunda çalıştırılıp sonuçlar bu belgeye işlenmelidir.

## 1. Entegrasyon yöntemi seçenekleri

| Yöntem | Açıklama | Okuma | Yazma | Lisans / bağımlılık | Öneri |
|---|---|---|---|---|---|
| **A. Doğrudan SQL Server (salt okunur)** | Logo'nun MS SQL veritabanına `db_datareader` yetkili ayrı bir kullanıcıyla bağlanmak | ✅ Tam | ❌ (bilerek kapalı) | Ek Logo lisansı gerekmez | **1. aşama için seçildi** |
| B. Logo Objects (LObjects / UnityObjects) | Logo'nun COM nesne katmanı; iş kurallarını uygulayarak kayıt oluşturur | ✅ | ✅ | Ayrı "Logo Objects" lisansı, Windows + COM | 2. aşama (yazma gerekirse) |
| C. Logo REST servisi | Bazı Logo ürünlerinde bulunan REST katmanı | ✅ | ✅ | Kurulu sürümde varlığı **doğrulanmalı** | Varsa değerlendirilir |
| D. Excel / dışa aktarım | Logo raporlarının dosya olarak alınması | ⚠ Manuel | ❌ | Yok | Yalnızca yedek yöntem |

**Karar:** İlk etapta **A – salt okunur SQL**. Muhasebe verisi hiçbir koşulda değiştirilmez. Bunu garantilemek için:

1. Ayrı bir SQL login (`demirbey_ro`) yalnızca `db_datareader` rolüyle oluşturulur, ayrıca `DENY INSERT, UPDATE, DELETE, EXECUTE` uygulanır (`sql/00-salt-okunur-kullanici.sql`).
2. Uygulama bağlantı dizesinde `ApplicationIntent=ReadOnly` kullanılır.
3. Kod tarafında Logo erişim katmanı (`LogoReadOnlyRepository`) yalnızca `SELECT` çalıştırır ve sorgular `WITH (NOLOCK)` yerine `READ COMMITTED SNAPSHOT` varsa onu kullanır; yoksa kısa süreli okuma yapılır (Logo kullanıcılarını kilitlememek için).
4. Senkronizasyon Logo verisini **PostgreSQL'deki kendi şemamıza** kopyalar; ekranlar ve hesaplamalar Logo'ya değil bu kopyaya bakar.

## 2. Logo veritabanı adlandırma kuralı

Logo tabloları firma ve dönem numarasıyla adlandırılır:

- Firma bazlı: `LG_{FFF}_{TABLO}` → ör. `LG_001_CLCARD`
- Dönem bazlı: `LG_{FFF}_{PP}_{TABLO}` → ör. `LG_001_01_CSCARD`
- Firma ve dönem listesi: `L_CAPIFIRM`, `L_CAPIPERIOD`

`FFF` (firma no) ve `PP` (dönem no) yapılandırmadan okunur (`appsettings.json → Logo:FirmNo / PeriodNo`). Yıl devrinde yeni dönem açılır; senkronizasyon **birden fazla dönemi** okuyabilmelidir (açık çekler eski dönemde kalmış olabilir).

## 3. Modül – tablo eşlemesi

| Modül | Tablo(lar) | Seviye | Temel alanlar | Not |
|---|---|---|---|---|
| Cari hesaplar (müşteri/tedarikçi) | `CLCARD` | Firma | `LOGICALREF, CODE, DEFINITION_, CARDTYPE, ACTIVE` | `CARDTYPE`: alıcı/satıcı/alıcı+satıcı ayrımı |
| Cari hareketler / bakiye | `CLFLINE`, `CLFICHE`, `PAYTRANS` | Dönem | `CLIENTREF, DATE_, AMOUNT, SIGN, TRCODE, MODULENR` | Borç/alacak için `SIGN` (0 borç, 1 alacak) |
| Çek / senet kartları | `CSCARD` | Dönem | `LOGICALREF, DOC, PORTFOYNO, NEWSERINO, BANKNAME, DUEDATE, AMOUNT, TRCURR, CURRSTAT, OWING` | `DOC`: 1 müşteri çeki, 2 müşteri senedi, 3 kendi çekimiz, 4 kendi senedimiz |
| Çek bordroları | `CSROLL` | Dönem | `LOGICALREF, TRCODE, DATE_, CARDREF (cari), BNACCREF` | Bordro türü (giriş, ciro, tahsile, teminata, iade…) |
| Çek hareket geçmişi | `CSTRANS` | Dönem | `CSREF, ROLLREF, STATNO, DATE_` | Her çekin durum zinciri buradan kurulur |
| Bankalar / hesaplar | `BNCARD`, `BNACCOUNT` | Firma | `CODE, DEFINITION_, IBAN, CURRENCY` | |
| Banka hareketleri | `BNFICHE`, `BNFLINE` | Dönem | `BNACCREF, DATE_, AMOUNT, SIGN, TRCODE` | Ödemenin/tahsilatın **doğrulanması** buradan |
| Banka kredileri | `BNCREDITCARD`, `BNCREDITLINE` (adlar **doğrulanmalı**) | Firma/Dönem | kredi tanımı, ödeme planı satırları | Kurulumda kredi modülü kullanılmıyorsa manuel giriş |
| Faturalar | `INVOICE` | Dönem | `TRCODE, FICHENO, DATE_, CLIENTREF, NETTOTAL, CANCELLED` | |
| Stok fişleri / satırları | `STFICHE`, `STLINE` | Dönem | `STOCKREF, CLIENTREF, TRCODE, IOCODE, AMOUNT, PRICE, LINENET, DISTDISC, DATE_, LINETYPE, CANCELLED, UOMREF` | `LINETYPE = 0` malzeme satırı |
| Stok kartları | `ITEMS`, `UNITSETL` | Firma | `CODE, NAME, UNITSETREF` | Ton dönüşümü için birim seti |
| Stok miktarları | `GNTOTST` | Dönem | `STOCKREF, INVENNO, ONHAND` | `INVENNO = -1` tüm ambarlar toplamı |
| Fiyat listeleri | `PRCLIST` | Firma | `CARDREF, PTYPE, PRICE` | |

### 3.1 `STLINE.TRCODE` – kullanılacak hareket türleri (doğrulanmalı)

| TRCODE | Anlam | Analizde |
|---|---|---|
| 1 | Satınalma | Alış |
| 6 | Satınalma iade | Alıştan düş |
| 7 | Perakende satış | Satış |
| 8 | Toptan satış | Satış |
| 2, 3 | Perakende / toptan satış iade | Satıştan düş |
| 14 | Devir | Maliyet başlangıcı |
| 11, 12, 25, 50, 51 | Fire, sarf, ambar, sayım | Satışa dahil edilmez |

Tüm sorgularda `CANCELLED = 0` ve `LINETYPE = 0` filtrelenir. İskontolar `DISTDISC` ve satır indirimleriyle birlikte **net satır tutarı** (`LINENET`) üzerinden hesaplanır.

### 3.2 Çek durumu (`CSCARD.CURRSTAT`) → uygulama kategorisi

Logo'daki durum kodları kuruluma göre doğrulanmalıdır. Eşleme **kodda sabit değil, yapılandırmada** tutulur (`Logo:ChequeStatusMap`).

| Logo durumu (tipik) | Belge | Uygulama kategorisi | Nakit akışına girer mi? |
|---|---|---|---|
| Portföyde | Müşteri çeki | 🔵 Mavi – Portföyde | Hayır (beklemede) |
| Tahsile verildi | Müşteri çeki | 🟢 Yeşil – Tahsilata verilen | Evet, **beklenen** tahsilat |
| Teminata verildi | Müşteri çeki | ⚪ Gri – Teminatta (kredi) | Hayır |
| İskonto / kredi karşılığı | Müşteri çeki | ⚪ Gri – İskontoda (kredi) | Hayır (nakit zaten alınmış) |
| Ciro edildi | Müşteri çeki | — (portföyden çıkar) | Hayır |
| Kendi çekimiz | Firma çeki | 🔴 Kırmızı – Ödenecek (+ banka rengi) | Evet, ödeme |
| Tahsil edildi / ödendi | — | Kapalı | Hayır (gerçekleşmiş) |
| Karşılıksız / protesto | — | ⚠ Riskli | Hayır, ayrıca uyarı |

**Renkler (kullanıcı tanımı):** ödenecek çekte 1. renk her zaman kırmızı, 2. renk banka: Halkbank mavi, Yapı Kredi lacivert, QNB mor, Vakıfbank sarı, Şekerbank açık sarı, TEB koyu yeşil, İş Bankası siyah.

**Çifte sayım kuralı:** Her çek, `LOGICALREF` anahtarıyla tek bir kayıttır ve herhangi bir anda yalnızca **bir** kategoride bulunur. Kategori, `CSTRANS` zincirindeki son harekete göre belirlenir. Nakit akışı yalnızca 🟢 ve 🔴 kategorilerini okur. Bu kural `ChequeCategoryResolver` ve birim testleriyle güvence altındadır.

### 3.3 Cari ödemeler, müşteri limitleri ve vade aşımı

| İhtiyaç | Logo kaynağı | Uygulama servisi |
|---|---|---|
| Tedarikçi borç bakiyesi, açık alış faturaları | `CLCARD` (satıcı), `INVOICE` (TRCODE alış faturası), `CLFLINE` / `PAYTRANS` | `SupplierPaymentPlanner` – FIFO mahsup, ağırlıklı ort. fatura tarihi + vade = önerilen çek tarihi |
| Müşteri açık cari bakiyesi | `CLFLINE` (SIGN), `CLCARD` | `CustomerRiskCalculator` |
| Müşteriden alınıp tahsil edilmemiş çekler | `CSCARD` + `CSTRANS` (son durum) | Risk = cari bakiye + portföy/tahsil/iskonto/teminattaki çekler |
| Ürün stok miktarı ve stok riski | `STLINE` toplamları: `IOCODE` 1-2 giriş, 3-4 çıkış (`CANCELLED = 0`, `LINETYPE = 0`, birim → ton çevrimi); kontrol için `GNTOTST.ONHAND` | `StockRiskCalculator` – stok = giriş − çıkış, son 30 gün satış hızı, kaç gün yeteceği, kullanıcının belirlediği **alım noktası** ("ALIM YAP") ve **risk seviyesi** ("STOK RİSKİNDE") |
| Satış faturaları ve tahsilatlar (valör) | `INVOICE` (satış), `PAYTRANS` (`DATE_`, `PAID`), çek vadeleri | `CustomerOverdueAnalyzer` – vade aşımı, vade farkı, "vadesi nasıl geçti" geçmişi, proforma |

Müşteri limitleri ve risk sınıfları uygulamanın kendi veritabanında tutulur. Logo'da kendi risk limiti alanları kullanılıyorsa (cari kart risk ayarları), keşif betiğiyle bulunup salt okunur olarak karşılaştırma için gösterilir. Satışın Logo içinde fiziksel olarak engellenmesi 2. aşamada (Logo Objects / Logo'nun kendi risk kontrolü) değerlendirilir; 1. aşamada sistem uyarı verir.

### 3.4 Logo'da bulunmayan veriler

| Veri | Kaynak |
|---|---|
| Şahsi kredi kartları | Manuel giriş / Excel içe aktarma |
| Şahsi krediler | Manuel giriş / Excel |
| Ticari kredi kartı ekstreleri | Logo'da yoksa manuel / Excel; banka entegrasyonu ileride |
| İthalat masrafları (navlun, gümrük…) | Logo'da masraf faturası olarak varsa oradan, yoksa manuel dağıtım |
| WhatsApp numaraları, bildirim ayarları | Uygulama veritabanı |

Şahsi veriler **yalnızca PostgreSQL'de**, ayrı şemada (`personal`) ve satır bazlı sahiplik (`owner_user_id`) ile tutulur; Logo'ya hiçbir zaman yazılmaz.

## 4. Senkronizasyon tasarımı

```
Logo SQL Server ──(salt okunur SELECT)──► Sync Worker (ASP.NET Core BackgroundService)
                                              │  değişen kayıtlar (LOGICALREF + CAPIBLOCK_MODIFIEDDATE)
                                              ▼
                                       PostgreSQL  logo.* (kopya)  ─►  Hesaplama servisleri  ─►  API / WhatsApp
                                       PostgreSQL  personal.*  (şahsi, Logo'dan bağımsız)
```

- **Artımlı okuma:** Logo tablolarındaki `CAPIBLOCK_CREADEDDATE` / `CAPIBLOCK_MODIFIEDDATE` alanlarıyla son senkron zamanından sonra değişen kayıtlar alınır. Silinen kayıtlar için günlük tam karşılaştırma yapılır.
- **Sıklık:** çekler ve banka 5 dk, stok/fatura 15 dk, cari 30 dk (ayarlanabilir).
- **İdempotent:** her kayıt `(firm, period, table, logicalref)` anahtarıyla upsert edilir.
- **Denetim:** her senkronizasyon çalışması `sync_runs` tablosuna (süre, satır sayısı, hata) yazılır.

## 5. Birim dönüşümü (ton)

Ürün analizleri ton bazındadır; Logo'da satırlar farklı birimlerle (kg, lt, varil) girilebilir. `UNITSETL` çevrim katsayılarıyla ana birime, ardından ürün bazlı tanımlı **ton katsayısına** çevrilir (sıvılar için yoğunluk gerekir → `product_settings.density_kg_per_lt`). Katsayısı tanımsız ürünler raporda işaretlenir, sessizce yanlış hesaplanmaz.

## 6. Kurulumda yapılacak doğrulama listesi

- [ ] Logo GO 3 sürüm numarası ve SQL Server sürümü
- [ ] Firma no / dönem no listesi (`L_CAPIFIRM`, `L_CAPIPERIOD`)
- [ ] `sql/01-logo-kesif.sql` çıktısı (tablolar, satır sayıları, `CURRSTAT` ve `TRCODE` dağılımları)
- [ ] Banka kredileri modülü kullanılıyor mu?
- [ ] Ticari kredi kartı hareketleri Logo'da nasıl izleniyor (banka hesabı mı, cari mi)?
- [ ] Logo Objects / REST lisansı var mı? (2. aşama için)
- [ ] Uygulama sunucusunun Logo SQL Server'a ağ erişimi (port 1433, VPN)
- [ ] Yedekleme: kurulum öncesi Logo veritabanı yedeği

## 7. Riskler

| Risk | Önlem |
|---|---|
| Logo şemasının sürümle değişmesi | Sorgular tek dosyada (`LogoQueries`), keşif betiği, entegrasyon testleri |
| Çek durum kodlarının farklı olması | Yapılandırılabilir eşleme + kurulumda doğrulama |
| Logo kullanıcılarını yavaşlatma | Artımlı okuma, mesai dışı tam senkron, indeksli alanlar |
| Yanlış nakit tahmini | Beklenen / kesinleşmiş ayrımı, çifte sayım testleri, kaynak gösterimi |
| Şahsi verinin sızması | Ayrı şema, satır bazlı yetki, varsayılan kapalı, denetim kaydı |
