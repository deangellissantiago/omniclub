interface Props {
  startDate: string;
  endDate: string;
  onChange: (startDate: string, endDate: string) => void;
}

export function DateRangeFilter({ startDate, endDate, onChange }: Props) {
  return (
    <div className="filter-bar">
      <label>
        De
        <input type="date" value={startDate} onChange={(e) => onChange(e.target.value, endDate)} />
      </label>
      <label>
        Até
        <input type="date" value={endDate} onChange={(e) => onChange(startDate, e.target.value)} />
      </label>
      {(startDate || endDate) && (
        <button className="btn-ghost" onClick={() => onChange("", "")}>Limpar filtro</button>
      )}
    </div>
  );
}
