// ASP.NET Core API istemcisi. Tauri'de tam adres, tarayıcıda Vite proxy kullanılır.
const BASE = (import.meta as unknown as { env: Record<string, string | undefined> }).env.VITE_API_URL ?? '';

export type Role = 'SistemYoneticisi' | 'FinansYoneticisi' | 'Muhasebe' | 'SatisYoneticisi' | 'SahsiFinans';

export type CashEventKind = 'CekOdeme' | 'CekTahsilat' | 'KrediTaksit' | 'TicariKart' | 'SahsiKart' | 'DigerPlanli';

export interface WeeklyItem { kind: CashEventKind; scope: 'Ticari' | 'Sahsi'; label: string; amount: number; isInflow: boolean }
export interface Cheque { serialNo: string; bank: string; party: string; dueDate: string; amount: number; category: string }
export interface WeeklyDay { date: string; items: WeeklyItem[]; discountedOrCollateralDue: Cheque[]; inflow: number; commercialOutflow: number }
export interface WeeklyReport {
  start: string; end: string; includesPersonal: boolean;
  totals: Record<CashEventKind, number>;
  commercialPayments: number; personalPayments: number; expectedCommercialCollections: number; commercialNet: number;
  days: WeeklyDay[]; discountedAndCollateralCheques: Cheque[];
}
export interface CashFlowDay { date: string; inflow: number; outflow: number; balance: number }
export interface CashFlowForecast {
  openingBalance: number; expectedInflow: number; outflow: number; closingBalance: number;
  firstDeficit: CashFlowDay | null; daily: CashFlowDay[];
}

async function get<T>(path: string, role: Role): Promise<T> {
  const res = await fetch(`${BASE}/api${path}`, { headers: { 'X-Demo-Role': role } });
  if (res.status === 403) throw new Error('Bu bilgiye erişim yetkiniz yok.');
  if (!res.ok) throw new Error(`Sunucu hatası (${res.status})`);
  return res.json() as Promise<T>;
}

export const api = {
  cashflow: (role: Role, days = 30) => get<CashFlowForecast>(`/cashflow?days=${days}`, role),
  weekly: (role: Role) => get<WeeklyReport>('/reports/weekly', role),
};

export const tl = (v: number) => new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 0 }).format(Math.round(v)) + ' TL';
export const trDate = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('tr-TR');
export const trDay = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('tr-TR', { weekday: 'long' });
