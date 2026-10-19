# =====================================================================
#  DEMİRBEY FINANCE AI – Logo GO 3 bağlantı testi ve keşif aracı
#  YALNIZCA OKUMA: Logo veritabanına hiçbir kayıt yazmaz, değiştirmez, silmez.
#  Çıktı: müşteri/ çek kaydı içermez; yalnızca tablo adları, satır sayıları,
#  durum kodu dağılımları ve alan adları. Şifre dosyaya yazılmaz.
# =====================================================================
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}
Add-Type -AssemblyName System.Data

function Baslik([string]$t) { Write-Host ''; Write-Host ('== ' + $t + ' ==') -ForegroundColor Cyan }
function Uyari([string]$t)  { Write-Host $t -ForegroundColor Yellow }

Baslik 'DEMİRBEY FINANCE AI · Logo GO 3 keşif'
Write-Host 'Bu araç Logo veritabanına YALNIZCA okuma sorgusu gönderir.'

# Bu bilgisayardaki SQL Server servisleri (yol göstermek için)
try {
  $svc = Get-Service | Where-Object { $_.Name -like 'MSSQL*' -or $_.Name -like 'SQLAgent*' }
  if ($svc) { Write-Host 'Bu bilgisayarda bulunan SQL Server servisleri:'; $svc | ForEach-Object { Write-Host ('   ' + $_.Name + '  [' + $_.Status + ']') } }
  else { Uyari 'Bu bilgisayarda SQL Server servisi görünmüyor; Logo veritabanı başka bir sunucuda olabilir.' }
} catch {}

$server = Read-Host 'SQL Server adı (ör. .\LOGO  |  BILGISAYARADI\SQLEXPRESS  |  192.168.1.10)'
$win    = Read-Host 'Windows oturumunuzla mı bağlanılsın? (E/H)'
$user = $null; $plain = $null
if ($win -notmatch '^[EeYy]') {
  $user  = Read-Host 'SQL kullanıcı adı (önerilen: demirbey_ro)'
  $sec   = Read-Host 'Şifre' -AsSecureString
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}
function BaglantiMetni([string]$db) {
  $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
  $b['Data Source'] = $server; $b['Initial Catalog'] = $db; $b['ApplicationIntent'] = 'ReadOnly'; $b['Connect Timeout'] = 15
  if ($user) { $b['User ID'] = $user; $b['Password'] = $plain } else { $b['Integrated Security'] = $true }
  return $b.ConnectionString
}

# Yalnızca SELECT / WITH sorgularına izin verilir
function Sorgu([string]$db, [string]$sql) {
  # tırnak içindeki metinler (ör. yetki kontrolündeki 'INSERT') denetime takılmasın
  $kontrol = [regex]::Replace($sql, "'[^']*'", "''")
  if ($kontrol -notmatch '^\s*(SELECT|WITH)\b' -or $kontrol -match '\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT)\b') { throw 'Yalnızca okuma sorgusu çalıştırılabilir.' }
  $cn = New-Object System.Data.SqlClient.SqlConnection (BaglantiMetni $db)
  try {
    $cn.Open(); $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 120
    $da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd; $t = New-Object System.Data.DataTable; [void]$da.Fill($t); return ,$t
  } finally { $cn.Close() }
}

Baslik 'Bağlantı testi'
try { $v = Sorgu 'master' 'SELECT @@VERSION AS surum, @@SERVERNAME AS sunucu'; Write-Host ('Bağlantı başarılı: ' + $v.Rows[0].sunucu) -ForegroundColor Green }
catch { Uyari ('Bağlantı kurulamadı: ' + $_.Exception.Message); Uyari 'Sunucu adını, kullanıcıyı ve SQL Server servisinin çalıştığını kontrol edin.'; Read-Host 'Kapatmak için Enter'; exit 1 }

$dbs = Sorgu 'master' "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name"
Write-Host 'Veritabanları:'; $dbs.Rows | ForEach-Object { Write-Host ('   ' + $_.name) }
$db = Read-Host 'Logo GO 3 veritabanının adı (yukarıdaki listeden)'

$cikti = Join-Path $PSScriptRoot ('Logo-Kesif-' + (Get-Date -Format 'yyyyMMdd-HHmm'))
New-Item -ItemType Directory -Path $cikti -Force | Out-Null
function Kaydet([string]$ad, $tablo) { $tablo | Export-Csv -Path (Join-Path $cikti ($ad + '.csv')) -NoTypeInformation -Encoding UTF8 -Delimiter ';' }

Kaydet '01-surum' $v
$firmalar = $null
try { $firmalar = Sorgu $db 'SELECT NR AS firma_no, NAME AS firma_adi FROM L_CAPIFIRM ORDER BY NR'; Kaydet '02-firmalar' $firmalar
      Write-Host 'Logo firmaları:'; $firmalar.Rows | ForEach-Object { Write-Host ('   ' + $_.firma_no + '  ' + $_.firma_adi) } }
catch { Uyari 'L_CAPIFIRM okunamadı – veritabanı adı Logo GO 3 veritabanı olmayabilir.' }
try { Kaydet '03-donemler' (Sorgu $db 'SELECT FIRMNR AS firma_no, NR AS donem_no, BEGDATE, ENDDATE, ACTIVE FROM L_CAPIPERIOD ORDER BY FIRMNR, NR') } catch {}

do { $firm = Read-Host 'Firma no (3 hane, ör. 001)' } until ($firm -match '^\d{3}$')
do { $per  = Read-Host 'Dönem no (2 hane, ör. 01)' } until ($per -match '^\d{2}$')
$F = 'LG_' + $firm + '_'; $FP = 'LG_' + $firm + '_' + $per + '_'

Baslik 'Keşif sorguları çalışıyor'
$adimlar = [ordered]@{
  '04-tablolar'        = "SELECT t.name AS tablo, SUM(p.rows) AS satir FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE t.name LIKE '$F%' GROUP BY t.name ORDER BY t.name"
  '05-cek-durumlari'   = "SELECT DOC AS belge_turu, CURRSTAT AS durum, COUNT(*) AS adet, SUM(AMOUNT) AS toplam, MIN(DUEDATE) AS en_erken, MAX(DUEDATE) AS en_gec FROM [${FP}CSCARD] GROUP BY DOC, CURRSTAT ORDER BY DOC, CURRSTAT"
  '06-cek-bordrolari'  = "SELECT TRCODE AS bordro_turu, COUNT(*) AS adet FROM [${FP}CSROLL] GROUP BY TRCODE ORDER BY TRCODE"
  '07-stok-hareketleri'= "SELECT TRCODE, IOCODE, COUNT(*) AS adet, SUM(AMOUNT) AS miktar, SUM(LINENET) AS net_tutar FROM [${FP}STLINE] WHERE CANCELLED=0 AND LINETYPE=0 GROUP BY TRCODE, IOCODE ORDER BY TRCODE, IOCODE"
  '08-fatura-turleri'  = "SELECT TRCODE, COUNT(*) AS adet, SUM(NETTOTAL) AS toplam FROM [${FP}INVOICE] WHERE CANCELLED=0 GROUP BY TRCODE ORDER BY TRCODE"
  '09-birimler'        = "SELECT CODE, NAME, CONVFACT1, CONVFACT2 FROM [${F}UNITSETL] ORDER BY CODE"
  '10-banka-hesaplari' = "SELECT CODE, DEFINITION_, CURRENCY FROM [${F}BNACCOUNT] ORDER BY CODE"
  '11-kredi-tablolari' = "SELECT name AS tablo FROM sys.tables WHERE name LIKE '$F%CREDIT%' OR name LIKE '$FP%CREDIT%' ORDER BY name"
  '12-cek-alanlari'    = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='${FP}CSCARD' ORDER BY ORDINAL_POSITION"
  '13-degisiklik-tarihi-alanlari' = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ('${FP}CSCARD','${FP}STLINE','${FP}INVOICE','${F}CLCARD') AND COLUMN_NAME LIKE 'CAPIBLOCK_%DATE'"
  '14-cari-sayilari'   = "SELECT CARDTYPE, COUNT(*) AS adet FROM [${F}CLCARD] GROUP BY CARDTYPE ORDER BY CARDTYPE"
  '15-yetki-kontrolu'  = "SELECT SUSER_SNAME() AS kullanici, HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','SELECT') AS okuma, HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','INSERT') AS ekleme, HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','UPDATE') AS guncelleme, HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','DELETE') AS silme"
}
$ozet = New-Object System.Collections.Generic.List[string]
$ozet.Add('DEMİRBEY FINANCE AI – Logo GO 3 keşif özeti'); $ozet.Add('Tarih: ' + (Get-Date)); $ozet.Add('Sunucu: ' + $server + '  Veritabanı: ' + $db + '  Firma: ' + $firm + '  Dönem: ' + $per); $ozet.Add('')
foreach ($ad in $adimlar.Keys) {
  try { $t = Sorgu $db $adimlar[$ad]; Kaydet $ad $t; Write-Host ('   ✓ ' + $ad + ' (' + $t.Rows.Count + ' satır)') -ForegroundColor Green; $ozet.Add('OK    ' + $ad + '  ' + $t.Rows.Count + ' satır') }
  catch { Write-Host ('   – ' + $ad + ': ' + $_.Exception.Message) -ForegroundColor DarkYellow; $ozet.Add('YOK   ' + $ad + '  ' + $_.Exception.Message) }
}
try {
  $y = Sorgu $db $adimlar['15-yetki-kontrolu']
  if ($y.Rows[0].ekleme -eq 1 -or $y.Rows[0].guncelleme -eq 1 -or $y.Rows[0].silme -eq 1) {
    Uyari 'DİKKAT: Bu kullanıcının yazma yetkisi var. Canlı kullanımda yalnızca okuma yetkili "demirbey_ro" kullanıcısı kullanılmalı (2-Salt-Okunur-Kullanici-Olustur.sql).'
    $ozet.Add(''); $ozet.Add('UYARI: bağlanan kullanıcının yazma yetkisi var; salt okunur kullanıcı oluşturulmalı.')
  } else { Write-Host 'Kullanıcı yalnızca okuma yetkili. ✓' -ForegroundColor Green; $ozet.Add(''); $ozet.Add('Kullanıcı yalnızca okuma yetkili.') }
} catch {}
$ozet | Out-File -FilePath (Join-Path $cikti '00-OZET.txt') -Encoding UTF8

$zip = $cikti + '.zip'
Compress-Archive -Path (Join-Path $cikti '*') -DestinationPath $zip -Force
Baslik 'Bitti'
Write-Host ('Sonuç dosyası: ' + $zip) -ForegroundColor Green
Write-Host 'Bu ZIP dosyasını Claude ile paylaşın. İçinde şifre, müşteri veya çek kaydı yoktur; göndermeden önce açıp bakabilirsiniz.'
Read-Host 'Kapatmak için Enter'
