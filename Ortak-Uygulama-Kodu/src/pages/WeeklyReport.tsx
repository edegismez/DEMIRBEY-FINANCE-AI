import { api, CashEventKind, Role, tl, trDate, trDay } from '../api';
import { useApi } from '../useApi';
import { Kpi, State, Tag } from '../ui';

const KIND: Record<CashEventKind, [string, string, string]> = {
  CekTahsilat: ['Çek tahsilatı', '🟢', 'var(--yesil)'],
  CekOdeme: ['Çek ödemesi', '🔴', 'var(--kirmizi)'],
  KrediTaksit: ['Kredi taksiti', '🟣', 'var(--mor)'],
  TicariKart: ['Ticari kredi kartı', '🔵', 'var(--mavi)'],
  SahsiKart: ['Şahsi kredi kartı', '💳', 'var(--sahsi)'],
  DigerPlanli: ['Diğer planlı', '🟠', 'var(--turuncu)'],
};

export function WeeklyReport({ role }: { role: Role }) {
  const { data: r, error } = useApi(() => api.weekly(role), [role]);
  if (!r) return <State error={error} />;
  return (
    <>
      <div className="toolbar">
        <b className="period">{trDate(r.start)} – {trDate(r.end)}</b>
        <button className="p" onClick={() => window.print()}>Yazdır / PDF</button>
      </div>
      <div className="grid g4">
        <Kpi label="🟢 Çek tahsilatları" value={tl(r.totals.CekTahsilat)} color="var(--yesil)" sub="Beklenen · doğrulanmamış" />
        <Kpi label="🔴 Çek ödemeleri" value={tl(r.totals.CekOdeme)} color="var(--kirmizi)" />
        <Kpi label="🟣 Kredi taksitleri" value={tl(r.totals.KrediTaksit)} color="var(--mor)" />
        <Kpi label="🔵 Ticari kredi kartları" value={tl(r.totals.TicariKart)} color="var(--mavi)" />
        {r.includesPersonal && <Kpi label="💳 Şahsi kredi kartları" value={tl(r.totals.SahsiKart)} color="var(--sahsi)" sub="Ticari toplama dahil değil" />}
        <Kpi label="Tahmini ticari net" value={tl(r.commercialNet)} color={r.commercialNet < 0 ? 'var(--kirmizi)' : 'var(--yesil)'} sub={`Ödeme ${tl(r.commercialPayments)}`} />
      </div>

      <div className="card" style={{ marginTop: 14 }}>
        <h2>Gün gün tahsilat ve ödemeler</h2>
        {r.days.map(d => (
          <div className="day" key={d.date}>
            <div className="dh">
              <span>{trDate(d.date)} · {trDay(d.date)}</span>
              <span>{[d.inflow ? `Tahsilat +${tl(d.inflow)}` : '', d.commercialOutflow ? `Ticari ödeme −${tl(d.commercialOutflow)}` : ''].filter(Boolean).join(' · ') || '—'}</span>
            </div>
            {d.items.length + d.discountedOrCollateralDue.length === 0 ? <div className="empty">Planlı hareket yok.</div> : (
              <table><tbody>
                {d.items.map((i, n) => (
                  <tr key={n}>
                    <td style={{ width: 200 }}>{KIND[i.kind][1]} {KIND[i.kind][0]} {i.scope === 'Sahsi' && <Tag color="var(--sahsi)">Şahsi</Tag>}</td>
                    <td>{i.label}</td>
                    <td className="n" style={{ width: 150, color: i.isInflow ? 'var(--yesil)' : 'var(--kirmizi)' }}>{i.isInflow ? '+' : '−'}{tl(i.amount)}</td>
                  </tr>
                ))}
                {d.discountedOrCollateralDue.map(c => (
                  <tr key={c.serialNo}>
                    <td>{c.category === 'Iskontoda' ? '🟠 İskontodaki çek vadesi' : '🔵 Teminattaki çek vadesi'}</td>
                    <td>{c.party} · {c.bank} · Çek {c.serialNo}</td>
                    <td className="n muted">{tl(c.amount)}</td>
                  </tr>
                ))}
              </tbody></table>
            )}
          </div>
        ))}
      </div>

      {r.discountedAndCollateralCheques.length > 0 && (
        <div className="card" style={{ marginTop: 14 }}>
          <h2>İskonto kredisine ve teminata verilen çekler</h2>
          <table>
            <thead><tr><th>Vade</th><th>Durum</th><th>Çek no</th><th>Müşteri</th><th>Banka</th><th className="n">Tutar</th></tr></thead>
            <tbody>
              {r.discountedAndCollateralCheques.map(c => (
                <tr key={c.serialNo}>
                  <td>{trDate(c.dueDate)}</td>
                  <td>{c.category === 'Iskontoda' ? <Tag color="var(--turuncu)">İskontoda</Tag> : <Tag color="var(--mavi)">Teminatta</Tag>}</td>
                  <td>{c.serialNo}</td><td>{c.party}</td><td>{c.bank}</td><td className="n">{tl(c.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
