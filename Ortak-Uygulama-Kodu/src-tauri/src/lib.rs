// Masaüstü ve mobil (iPad) uygulamanın ortak giriş noktası.
// Arayüz React'tir; tüm finansal hesaplamalar ASP.NET Core sunucusunda yapılır.
#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("DEMİRBEY FINANCE AI başlatılamadı");
}
