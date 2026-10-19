// Windows sürümünde ek konsol penceresi açılmasın
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    demirbey_finance_ai_lib::run()
}
