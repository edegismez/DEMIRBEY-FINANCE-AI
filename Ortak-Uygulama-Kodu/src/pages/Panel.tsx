import { api, Role, tl, trDate } from '../api';
import { useApi } from '../useApi';
import { Kpi, State } from '../ui';

export function Panel({ role }: { role: Role }) {
  const { data, error } = useApi(() => api.cashflow(role, 30), [role]);
  if (!data) return <State error={error} />;
  return (
    <>
      {data.firstDeficit && (
        <div className="alert">
          <b>⚠ Nakit açığı uyarısı:</b> {trDate(data.firstDeficit.date)} tarihinde mevcut ödeme planınıza göre{' '}
          <b>{tl(Math.abs(data.firstDeficit.balance))}</b> geçici nakit açığı oluşabilir.
        </div>
      )}
      <div className="grid g4">
        <Kpi label="Başlangıç bakiyesi" value={tl(data.openingBalance)} />
        <Kpi label="30 gün beklenen tahsilat" value={tl(data.expectedInflow)} color="var(--yesil)" sub="doğrulanmamış" />
        <Kpi label="30 gün ticari ödeme" value={tl(data.outflow)} color="var(--kirmizi)" />
        <Kpi label="Dönem sonu bakiye" value={tl(data.closingBalance)} color={data.closingBalance < 0 ? 'var(--kirmizi)' : 'var(--yesil)'} />
      </div>
      <div className="card" style={{ marginTop: 14 }}>
        <h2>Gün bazında nakit akışı (hareketli günler)</h2>
        <table>
          <thead><tr><th>Tarih</th><th className="n">Tahsilat</th><th className="n">Ödeme</th><th className="n">Bakiye</th></tr></thead>
          <tbody>
            {data.daily.filter(d => d.inflow || d.outflow).map(d => (
              <tr key={d.date}>
                <td>{trDate(d.date)}</td>
                <td className="n" style={{ color: 'var(--yesil)' }}>{d.inflow ? tl(d.inflow) : '–'}</td>
                <td className="n" style={{ color: 'var(--kirmizi)' }}>{d.outflow ? tl(d.outflow) : '–'}</td>
                <td className="n" style={{ color: d.balance < 0 ? 'var(--kirmizi)' : undefined }}>{tl(d.balance)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
