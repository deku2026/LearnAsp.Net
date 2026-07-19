import http from 'k6/http';
import { check } from 'k6';

export const options = {
  scenarios: {
    observability_workload: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 5),
      duration: __ENV.DURATION || '10s',
    },
  },
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<250', 'p(99)<500'],
  },
};

const baseUrl = __ENV.BASE_URL || 'http://host.docker.internal:6081';

export default function () {
  const workId = crypto.randomUUID();
  const response = http.get(
    `${baseUrl}/api/observability/work/${workId}?delayMs=25`,
    { tags: { operation: 'instrumented-work' } },
  );

  check(response, {
    'workload returns 200': (result) => result.status === 200,
    'workload returns W3C trace context': (result) => {
      const body = result.json();
      return body.workId === workId
        && body.traceId?.length === 32
        && body.spanId?.length === 16;
    },
  });
}
