import { useState } from 'react';
import type { Role } from './api';
import { Panel } from './pages/Panel';
import { WeeklyReport } from './pages/WeeklyReport';

type PageId = 'panel' | 'rapor' | 'cek' | 'cari' | 'limit' | 'kart' | 'kredi' | 'nakit' | 'urun' | 'wa' | 'ai' | 'yetki';

const PAGES: { id: PageId; icon: string; title: string; sub: string; roles: Role[] }[] = [
  { id: 'panel', icon: '▦', title: 'Ana Panel', sub: 'Finansal durum özeti', roles: ['SistemYoneticisi', 'FinansYoneticisi'] },
  { id: 'rapor', icon: '▥', title: 'Haftalık Rapor', sub: 'Pazartesi–Pazar · gün gün tahsilat ve ödemeler', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'SahsiFinans'] },
  { id: 'cek', icon: '▤', title: 'Çek Takibi', sub: 'Alınan ve verilen çekler', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'Muhasebe'] },
  { id: 'cari', icon: '▣', title: 'Cari Ödemeler', sub: 'Tedarikçi borçları · önerilen ödeme tarihi', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'Muhasebe'] },
  { id: 'limit', icon: '◉', title: 'Müşteri Limit & Risk', sub: 'Limit, vade aşımı, vade farkı', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'SatisYoneticisi'] },
  { id: 'kart', icon: '▭', title: 'Kredi Kartları', sub: 'Şahsi ve ticari kartlar ayrı', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'SahsiFinans'] },
  { id: 'kredi', icon: '◈', title: 'Banka Kredileri', sub: 'Taksit ve finansman maliyeti', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'SahsiFinans'] },
  { id: 'nakit', icon: '≋', title: 'Nakit Akışı', sub: '7–180 gün tahmin', roles: ['SistemYoneticisi', 'FinansYoneticisi'] },
  { id: 'urun', icon: '◧', title: 'Ürün & Maliyet', sub: 'Alış, satış, kârlılık', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'Muhasebe', 'SatisYoneticisi'] },
  { id: 'wa', icon: '✆', title: 'WhatsApp Bildirim', sub: 'Şablonlar ve kurallar', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'SahsiFinans'] },
  { id: 'ai', icon: '✦', title: 'AI Finans Asistanı', sub: 'Kayıtlara dayalı yanıtlar', roles: ['SistemYoneticisi', 'FinansYoneticisi', 'Muhasebe', 'SatisYoneticisi', 'SahsiFinans'] },
  { id: 'yetki', icon: '⚿', title: 'Yetki & Güvenlik', sub: 'Rol bazlı erişim', roles: ['SistemYoneticisi'] },
];

const ROLES: [Role, string][] = [
  ['SistemYoneticisi', 'Sistem yöneticisi'], ['FinansYoneticisi', 'Finans yöneticisi'], ['Muhasebe', 'Muhasebe kullanıcısı'],
  ['SatisYoneticisi', 'Satış yöneticisi'], ['SahsiFinans', 'Şahsi finans kullanıcısı'],
];

export function App() {
  const [page, setPage] = useState<PageId>('panel');
  const [role, setRole] = useState<Role>('SistemYoneticisi');
  const current = PAGES.find(p => p.id === page)!;
  const allowed = current.roles.includes(role);

  return (
    <div className="shell">
      <aside>
        <div className="brand"><img src="/logo-yatay.png" alt="Demirbey Kimya Sanayi" /><small>FINANCE AI</small></div>
        <nav>
          {PAGES.map(p => (
            <a key={p.id} className={`${p.id === page ? 'on' : ''} ${p.roles.includes(role) ? '' : 'lock'}`} onClick={() => setPage(p.id)}>
              <span>{p.icon}</span>{p.title}{!p.roles.includes(role) && ' 🔒'}
            </a>
          ))}
        </nav>
      </aside>
      <main>
        <header className="top">
          <div><h1>{current.title}</h1><div className="muted">{current.sub}</div></div>
          <label className="role">Rol (demo):{' '}
            <select value={role} onChange={e => setRole(e.target.value as Role)}>
              {ROLES.map(([v, n]) => <option key={v} value={v}>{n}</option>)}
            </select>
          </label>
        </header>
        <div className="view">
          {!allowed ? <div className="card state">🔒 Bu modüle erişim yetkiniz yok.</div>
            : page === 'panel' ? <Panel role={role} />
            : page === 'rapor' ? <WeeklyReport role={role} />
            : <div className="card state">Bu ekran onaylanan prototipten (Prototip/index.html) taşınacak.</div>}
        </div>
      </main>
    </div>
  );
}
