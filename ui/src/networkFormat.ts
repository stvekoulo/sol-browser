export function matchesRequest(request: { url: string; method: string; type: string; status: number }, query: string) {
  const text = `${request.url} ${request.method} ${request.type} ${request.status}`.toLowerCase();
  return query.toLowerCase().trim().split(/\s+/).every(word => text.includes(word));
}

// La commande Bash reste du texte à copier ; les champs masqués ne sont pas rejoués.
export function toCurl(request: { url: string; method: string; requestHeaders: Record<string, string>; postData: string }) {
  const quote = (value: string) => `'${value.replaceAll("'", "'\\''")}'`;
  const args = ['curl', '--request', quote(request.method), '--url', quote(request.url)];
  for (const [name, value] of Object.entries(request.requestHeaders)) {
    if (/cookie|authorization|token|secret|api[-_]?key|host|content-length|connection|^sec-/i.test(name) || value.includes('[masqué]')) continue;
    args.push('--header', quote(`${name}: ${value}`));
  }
  if (request.postData && !request.postData.includes('[masqué]')) args.push('--data-raw', quote(request.postData));
  return args.join(' ');
}
