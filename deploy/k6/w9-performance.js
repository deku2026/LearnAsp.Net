import http from 'k6/http';
import { check } from 'k6';

export const options = {
  scenarios: {
    perf_async: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || '15s',
    },
  },
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500', 'p(99)<1000'],
  },
};

const baseUrl = __ENV.BASE_URL || 'http://127.0.0.1:5027';

export default function () {
  const response = http.get(`${baseUrl}/lab/threadpool/async?delayMs=25`, {
    headers: { 'X-Lab-Token': __ENV.LAB_TOKEN || 'lab-token' },
  });
  check(response, {
    'async returns 200': (r) => r.status === 200,
  });
}
