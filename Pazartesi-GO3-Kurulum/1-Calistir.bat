@echo off
chcp 65001 >nul
rem DEMİRBEY FINANCE AI – Logo GO 3 keşif aracını yalnızca bu oturum için çalıştırır (sistem ayarı değiştirmez)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Logo-Baglanti-Testi-ve-Kesif.ps1"
