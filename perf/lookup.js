// k6 load test for GET /api/lookup (spec §11.5, NFR-1: p95 < 200 ms).
//   docker run --rm -i -v "$PWD/perf:/perf" -e BASE_URL=http://host.docker.internal:5080 grafana/k6 run /perf/lookup.js
// 10 virtual users for 60 s; 70 % of requests ask about the 7 most-listed books, 30 % about long-tail books.
// Run the API with a high lookup rate limit (PRICERADAR_LOOKUP_PER_MINUTE=100000), otherwise k6 measures 429s.
import http from 'k6/http';
import { check } from 'k6';

const books = JSON.parse(open('./lookup-books.json'));
const base = __ENV.BASE_URL || 'http://host.docker.internal:5080';

export const options = {
  vus: 10,
  duration: '60s',
  thresholds: {
    http_req_duration: ['p(95)<200'],
    checks: ['rate>0.99'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  const pool = Math.random() < 0.7 ? books.hot : books.cold;
  const book = pool[Math.floor(Math.random() * pool.length)];
  const price = 200 + Math.floor(Math.random() * 1200);
  const url = `${base}/api/lookup?title=${encodeURIComponent(book.title)}&author=${encodeURIComponent(book.author || '')}&askingPrice=${price}`;

  const res = http.get(url, { tags: { pool: pool === books.hot ? 'hot' : 'cold' } });
  check(res, {
    'status 200': (r) => r.status === 200,
    'book matched': (r) => r.status === 200 && r.json('match.status') === 'matched',
  });
}
