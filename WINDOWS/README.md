# Windows uygulaması

Windows sürümü `../Ortak-Uygulama-Kodu` klasöründeki ortak koddan üretilir (Mac ve iPad ile aynı arayüz).

## Kurulum dosyası üretme (Windows bilgisayarda)

Gerekenler: Node.js 20+, Rust (rustup), Visual Studio C++ Build Tools, WebView2 (Windows 10/11'de hazır).

```bash
cd ../Ortak-Uygulama-Kodu
npm install
npm run tauri build
```

Çıktılar:
- `Ortak-Uygulama-Kodu/src-tauri/target/release/bundle/msi/*.msi`
- `Ortak-Uygulama-Kodu/src-tauri/target/release/bundle/nsis/*-setup.exe`

Hazır kurulum dosyaları dağıtım için bu klasöre (`Windows/`) kopyalanır.

## Bağlantı

Uygulama merkezi DEMİRBEY sunucusuna bağlanır (`../Sunucu`). Logo GO 3'e doğrudan bağlanmaz.
Sunucu, Logo'nun kurulu olduğu Windows sunucuda veya aynı ağdaki bir makinede çalıştırılır.
