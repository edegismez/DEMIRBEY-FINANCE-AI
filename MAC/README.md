# MacBook uygulaması

macOS sürümü `../Ortak-Uygulama-Kodu` klasöründeki ortak koddan üretilir.

## Uygulama üretme (Mac'te)

Gerekenler: Node.js 20+, Rust (rustup), Xcode Command Line Tools.

```bash
cd ../Ortak-Uygulama-Kodu
npm install
npm run tauri build
```

Çıktılar:
- `Ortak-Uygulama-Kodu/src-tauri/target/release/bundle/macos/DEMIRBEY FINANCE AI.app`
- `Ortak-Uygulama-Kodu/src-tauri/target/release/bundle/dmg/*.dmg`

Başka Mac'lere dağıtırken uyarı çıkmaması için Apple Developer hesabıyla imzalama ve notarization yapılır.

## Bağlantı

Mac uygulaması Logo GO 3'e doğrudan bağlanmaz; merkezi DEMİRBEY sunucusuna bağlanır.
Ofis dışında kullanım için sunucuya VPN veya HTTPS ile güvenli erişim gerekir.
