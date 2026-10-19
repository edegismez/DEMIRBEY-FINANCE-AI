# iPad uygulaması

iPad sürümü `../Ortak-Uygulama-Kodu` klasöründeki ortak koddan Tauri 2 mobil desteğiyle üretilir.

## Uygulama üretme (Mac'te)

Gerekenler: Xcode, Rust iOS hedefleri, Apple Developer hesabı (cihaza yüklemek / TestFlight için).

```bash
cd ../Ortak-Uygulama-Kodu
npm install
rustup target add aarch64-apple-ios aarch64-apple-ios-sim
npm run tauri ios init
npm run tauri ios dev        # iPad simülatöründe deneme
npm run tauri ios build      # .ipa
```

Dağıtım: TestFlight (önerilen) veya şirket içi (ad-hoc) dağıtım.

## Tablet uyumu

Arayüz dokunmatik kullanıma ve iPad yatay/dikey ekrana göre düzenlenecek (geniş tablolar yatay kaydırılır, menü daraltılabilir).

## Bağlantı

iPad Logo GO 3'e doğrudan bağlanmaz; merkezi DEMİRBEY sunucusuna HTTPS üzerinden bağlanır.
Yatırım Kasası için ayrıca Face ID / Touch ID ile kilit açma eklenebilir.
