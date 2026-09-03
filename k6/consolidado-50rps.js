// Teste de carga do endpoint de leitura do saldo consolidado (Daily Balance API).
// Metodologia e critérios de aceite documentados em docs/performance-and-capacity.md.
//
// Uso local:
//   1) docker compose up -d
//   2) TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/dev/token | ...) # extrair accessToken
//   3) k6 run -e BASE_URL=http://localhost:5081 -e TOKEN=$TOKEN -e BUSINESS_DATE=2026-09-02 k6/consolidado-50rps.js
import http from "k6/http";
import { check } from "k6";

const BASE_URL = __ENV.BASE_URL || "http://localhost:5081";
const TOKEN = __ENV.TOKEN || "";
const BUSINESS_DATE = __ENV.BUSINESS_DATE || new Date().toISOString().slice(0, 10);

export const options = {
  scenarios: {
    consolidado_50rps: {
      executor: "constant-arrival-rate",
      rate: 50,
      timeUnit: "1s",
      duration: "2m",
      preAllocatedVUs: 50,
      maxVUs: 200,
      startTime: "30s", // após o estágio de aquecimento abaixo
    },
    aquecimento: {
      executor: "constant-arrival-rate",
      rate: 10,
      timeUnit: "1s",
      duration: "30s",
      preAllocatedVUs: 10,
      maxVUs: 50,
      startTime: "0s",
    },
  },
  thresholds: {
    // Meta declarada em docs/non-functional-requirements.md: erro < 5% a 50 RPS.
    http_req_failed: ["rate<0.05"],
    // Meta de latência da consulta (p95); ver docs/performance-and-capacity.md para o racional.
    http_req_duration: ["p(95)<300"],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/api/v1/daily-balances/${BUSINESS_DATE}`, {
    headers: {
      Authorization: `Bearer ${TOKEN}`,
      "X-Correlation-ID": crypto.randomUUID ? crypto.randomUUID() : `${__VU}-${__ITER}`,
    },
    tags: { name: "GetDailyBalance" },
  });

  check(res, {
    "status é 200": (r) => r.status === 200,
    "corpo contém balance": (r) => {
      try {
        return JSON.parse(r.body).balance !== undefined;
      } catch {
        return false;
      }
    },
  });
}
