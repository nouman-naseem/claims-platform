export function errMsg(e: any): string {
  const p = e?.error;
  if (p?.errors) return (Object.values(p.errors) as string[][]).flat().join(' ');
  return p?.detail || p?.title || (e?.status === 0 ? 'Cannot reach the server.' : e?.message) || 'Something went wrong.';
}

export function label(value: string | null | undefined): string {
  return (value ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
}
