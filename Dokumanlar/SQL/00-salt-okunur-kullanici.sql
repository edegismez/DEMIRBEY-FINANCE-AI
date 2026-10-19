/* =====================================================================
   DEMİRBEY FINANCE AI – Logo GO 3 için SALT OKUNUR SQL kullanıcısı
   Sistem yöneticisi tarafından SQL Server Management Studio'da çalıştırılır.
   - <LOGO_DB> : Logo GO 3 veritabanı adı (ör. GO3DB)
   - Parolayı güçlü bir değerle değiştirin; bu dosyaya gerçek parola yazmayın.
   ===================================================================== */

USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'demirbey_ro')
    CREATE LOGIN [demirbey_ro] WITH PASSWORD = N'<GUCLU_PAROLA>', CHECK_POLICY = ON, DEFAULT_DATABASE = [<LOGO_DB>];
GO

USE [<LOGO_DB>];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'demirbey_ro')
    CREATE USER [demirbey_ro] FOR LOGIN [demirbey_ro];
GO

-- Yalnızca okuma
ALTER ROLE [db_datareader] ADD MEMBER [demirbey_ro];
GO

-- Yazma ve prosedür çalıştırma açıkça yasak
DENY INSERT, UPDATE, DELETE, EXECUTE, ALTER, REFERENCES TO [demirbey_ro];
GO

-- Kontrol: aşağıdaki sorgu yalnızca db_datareader döndürmelidir
SELECT r.name AS rol
FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
WHERE u.name = N'demirbey_ro';
GO
