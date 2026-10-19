/* =====================================================================
   DEMİRBEY FINANCE AI – Logo GO 3 KEŞİF BETİĞİ (yalnızca SELECT)
   Amaç: kurulu Logo yapısını doğrulamak. Hiçbir veriyi değiştirmez.
   demirbey_ro kullanıcısıyla çalıştırılabilir. Çıktıyı Excel'e kaydedip
   docs/01-logo-go3-entegrasyon-analizi.md belgesine işleyin.
   ===================================================================== */

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; -- Logo kullanıcılarını kilitlememek için (yalnızca keşif)

DECLARE @Firm   CHAR(3) = '001';   -- <-- firma no
DECLARE @Period CHAR(2) = '01';    -- <-- dönem no
DECLARE @F  SYSNAME = 'LG_' + @Firm + '_';
DECLARE @FP SYSNAME = 'LG_' + @Firm + '_' + @Period + '_';
DECLARE @sql NVARCHAR(MAX);

/* 1. Sunucu ve sürüm */
SELECT @@VERSION AS sql_server_surumu, DB_NAME() AS veritabani;

/* 2. Firmalar ve dönemler */
IF OBJECT_ID('L_CAPIFIRM') IS NOT NULL
    SELECT NR AS firma_no, NAME AS firma_adi FROM L_CAPIFIRM ORDER BY NR;
IF OBJECT_ID('L_CAPIPERIOD') IS NOT NULL
    SELECT FIRMNR AS firma_no, NR AS donem_no, BEGDATE, ENDDATE, ACTIVE FROM L_CAPIPERIOD ORDER BY FIRMNR, NR;

/* 3. İlgili tabloların varlığı ve satır sayıları */
SELECT t.name AS tablo, SUM(p.rows) AS satir
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.name IN (
    @F+'CLCARD', @F+'BNCARD', @F+'BNACCOUNT', @F+'ITEMS', @F+'UNITSETL', @F+'PRCLIST',
    @F+'BNCREDITCARD', @F+'BNCREDITLINE',
    @FP+'CSCARD', @FP+'CSROLL', @FP+'CSTRANS', @FP+'CLFLINE', @FP+'CLFICHE', @FP+'PAYTRANS',
    @FP+'BNFICHE', @FP+'BNFLINE', @FP+'INVOICE', @FP+'STFICHE', @FP+'STLINE', @FP+'GNTOTST')
GROUP BY t.name ORDER BY t.name;

/* 4. Adında kredi / CREDIT geçen tablolar (banka kredisi modülü tespiti) */
SELECT name AS kredi_ile_ilgili_tablo FROM sys.tables
WHERE name LIKE @F + '%CREDIT%' OR name LIKE @FP + '%CREDIT%' ORDER BY name;

/* 5. Çek/senet belge türü ve durum dağılımı (kategori eşlemesi için kritik) */
SET @sql = N'SELECT DOC AS belge_turu, CURRSTAT AS durum, COUNT(*) AS adet, SUM(AMOUNT) AS toplam,
                    MIN(DUEDATE) AS en_erken_vade, MAX(DUEDATE) AS en_gec_vade
             FROM ' + QUOTENAME(@FP+'CSCARD') + N' GROUP BY DOC, CURRSTAT ORDER BY DOC, CURRSTAT;';
IF OBJECT_ID(@FP+'CSCARD') IS NOT NULL EXEC sp_executesql @sql;

/* 6. Çek bordro türleri */
SET @sql = N'SELECT TRCODE AS bordro_turu, COUNT(*) AS adet FROM ' + QUOTENAME(@FP+'CSROLL') + N' GROUP BY TRCODE ORDER BY TRCODE;';
IF OBJECT_ID(@FP+'CSROLL') IS NOT NULL EXEC sp_executesql @sql;

/* 7. Stok hareket türleri */
SET @sql = N'SELECT TRCODE, IOCODE, COUNT(*) AS adet, SUM(LINENET) AS net_tutar
             FROM ' + QUOTENAME(@FP+'STLINE') + N' WHERE CANCELLED = 0 AND LINETYPE = 0
             GROUP BY TRCODE, IOCODE ORDER BY TRCODE, IOCODE;';
IF OBJECT_ID(@FP+'STLINE') IS NOT NULL EXEC sp_executesql @sql;

/* 8. Kullanılan birimler (ton dönüşümü için) */
SET @sql = N'SELECT TOP 50 CODE, NAME, CONVFACT1, CONVFACT2 FROM ' + QUOTENAME(@F+'UNITSETL') + N' ORDER BY CODE;';
IF OBJECT_ID(@F+'UNITSETL') IS NOT NULL EXEC sp_executesql @sql;

/* 9. Artımlı senkron için değişiklik tarihi alanlarının varlığı */
SELECT TABLE_NAME AS tablo, COLUMN_NAME AS alan
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN (@FP+'CSCARD', @FP+'STLINE', @FP+'INVOICE', @F+'CLCARD')
  AND COLUMN_NAME LIKE 'CAPIBLOCK_%DATE'
ORDER BY TABLE_NAME, COLUMN_NAME;

/* 10. CSCARD alan listesi (alan adı doğrulaması) */
SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @FP + 'CSCARD' ORDER BY ORDINAL_POSITION;
